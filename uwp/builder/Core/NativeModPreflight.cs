using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

public sealed record NativeModPreflightResult(NativeModCatalog Catalog, string HorizonCommit, int HookApi,
    IReadOnlyList<NativeModListing> Mods);

/// <summary>Checks registry versions before the picker is shown, using the selected patch's source commit and hooks API.</summary>
public static class NativeModPreflight
{
    private sealed record BundleCompatibility(string Horizon, string Mixins, int? Hooks);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheGates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<NativeModPreflightResult> ResolveAsync(string horizonCommit, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(horizonCommit, "^[0-9a-f]{40}$")) throw new InvalidDataException("The selected Horizon release has an invalid commit.");
        progress?.Report("Reading the selected Horizon release’s hook API…");
        var hookApi = await FetchHookApiAsync(horizonCommit, cancellationToken);
        progress?.Report("Loading Horizon’s current native-mod registry…");
        var catalog = await NativeModIndex.FetchCatalogAsync(cancellationToken);
        var resolved = new NativeModListing[catalog.Mods.Count];
        var checkedCount = 0;
        var totalCandidates = catalog.Mods.Sum(mod => mod.Versions.Count);
        var checkedCandidates = 0;
        using var transport = new CatalogTransport();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, catalog.Mods.Count), options, async (index, token) =>
        {
            var listing = catalog.Mods[index];
            var compatible = new List<NativeModCandidate>();
            var reasons = new List<string>();
            foreach (var candidate in listing.Versions)
            {
                var candidateNumber = Interlocked.Increment(ref checkedCandidates);
                progress?.Report($"Checking native-mod release {candidateNumber}/{totalCandidates}: {listing.Name} {candidate.Version}…");
                if (candidate.BrokenCommits.Contains(horizonCommit, StringComparer.OrdinalIgnoreCase))
                {
                    reasons.Add($"{candidate.Version} is marked broken on this Horizon commit.");
                    continue;
                }
                if (!TryReadApi(candidate.Api, out var indexedHorizon, out var indexedMixins))
                {
                    reasons.Add($"{candidate.Version} has no usable compatibility metadata.");
                    continue;
                }
                if (indexedHorizon.Equals(horizonCommit, StringComparison.OrdinalIgnoreCase))
                {
                    compatible.Add(candidate);
                    continue;
                }
                if (candidate.Url.Length == 0 || candidate.Size <= 0)
                {
                    reasons.Add($"{candidate.Version} is missing its bundle download details.");
                    continue;
                }
                try
                {
                    var bundlePath = await EnsureBundleAsync(transport, candidate, token);
                    var bundle = ReadCompatibility(bundlePath, candidate);
                    if (!bundle.Mixins.Equals(indexedMixins, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{listing.Name} {candidate.Version} does not match its registry compatibility record.");
                    if (bundle.Horizon.Equals(horizonCommit, StringComparison.OrdinalIgnoreCase)
                        || bundle.Hooks is { } hooks && hooks <= hookApi)
                    {
                        compatible.Add(candidate);
                    }
                    else
                    {
                        var requires = bundle.Hooks is { } required ? $"hook API {required}" : $"Horizon commit {bundle.Horizon}";
                        reasons.Add($"{candidate.Version} requires {requires}.");
                    }
                }
                catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException
                    or KeyNotFoundException or FormatException or OverflowException)
                {
                    reasons.Add($"{candidate.Version} has invalid compatibility metadata: {error.Message}");
                }
            }
            var message = compatible.Count > 0
                ? $"Compatible with Horizon {horizonCommit}; newest supported version is {compatible[0].Version}."
                : listing.Versions.Count == 0
                    ? $"No Minecraft {LocalRecipe.MinecraftVersion} release is available."
                    : $"No release supports Horizon commit {horizonCommit} and hook API {hookApi}. " + string.Join(" ", reasons.Distinct(StringComparer.Ordinal));
            resolved[index] = listing with { CompatibleVersions = compatible, CompatibilityMessage = message };
            var done = Interlocked.Increment(ref checkedCount);
            progress?.Report($"Checking mod compatibility: {done}/{catalog.Mods.Count} · {listing.Name}");
        });
        return new(catalog, horizonCommit, hookApi, resolved);
    }

    internal static async Task<string> EnsureBundleAsync(CatalogTransport transport, NativeModCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Sha256.Length != 64 || !candidate.Sha256.All(char.IsAsciiHexDigit)
            || candidate.Url.Length == 0 || candidate.Size is <= 0 or > 64L * 1024 * 1024)
            throw new InvalidDataException($"The registry entry for {candidate.Name} {candidate.Version} is invalid.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCHorizonUWP", "Builder", "Cache", "NativeMods");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, candidate.Sha256.ToLowerInvariant() + ".hnmod");
        var gate = CacheGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                await using var cached = File.OpenRead(path);
                if (cached.Length == candidate.Size
                    && Convert.ToHexString(await SHA256.HashDataAsync(cached, cancellationToken)).Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                    return path;
                File.Delete(path);
            }
            await transport.DownloadArtifactAsync(new CatalogArtifact(candidate.Url, candidate.Sha256), path, cancellationToken);
            if (new FileInfo(path).Length != candidate.Size)
            {
                File.Delete(path);
                throw new InvalidDataException($"The downloaded {candidate.Name} {candidate.Version} bundle has the wrong size.");
            }
            return path;
        }
        finally { gate.Release(); }
    }

    private static async Task<int> FetchHookApiAsync(string commit, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://raw.githubusercontent.com/horizon-rust/horizon/{commit}/crates/horizon-client/src/game/native_hooks.rs");
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1024 * 1024) throw new InvalidDataException("Horizon’s native hook source is unexpectedly large.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 1024 * 1024) throw new InvalidDataException("Horizon’s native hook source is unexpectedly large.");
        var match = Regex.Match(text, @"(?m)^pub\(crate\) const NATIVE_HOOKS_API: u32 = (\d+);\s*$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var value) || value <= 0)
            throw new InvalidDataException("The selected Horizon commit does not declare a valid native hooks API.");
        return value;
    }

    private static BundleCompatibility ReadCompatibility(string path, NativeModCandidate candidate)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 512) throw new InvalidDataException($"{candidate.Name} {candidate.Version} contains too many files.");
        var metadataEntries = zip.Entries.Where(entry => entry.FullName == "horizon-native.json").ToArray();
        if (metadataEntries.Length != 1 || metadataEntries[0].Length is <= 0 or > 65536)
            throw new InvalidDataException($"{candidate.Name} {candidate.Version} has invalid native-mod metadata.");
        using var stream = metadataEntries[0].Open();
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var id = root.GetProperty("id").GetString() ?? "";
        var version = root.GetProperty("version").GetString() ?? "";
        var game = root.GetProperty("game").GetString() ?? "";
        var horizon = root.GetProperty("horizon").GetString() ?? "";
        var mixins = root.GetProperty("mixins").GetString() ?? "";
        int? hooks = null;
        if (root.TryGetProperty("hooks", out var hooksJson) && hooksJson.ValueKind != JsonValueKind.Null)
        {
            if (hooksJson.ValueKind != JsonValueKind.Number || !hooksJson.TryGetInt32(out var parsed) || parsed <= 0)
                throw new InvalidDataException($"{candidate.Name} {candidate.Version} declares an invalid hook API.");
            hooks = parsed;
        }
        if (id != candidate.Id || version != candidate.Version || game != candidate.Game
            || !Regex.IsMatch(horizon, "^[0-9a-f]{40}$") || !Regex.IsMatch(mixins, "^[0-9a-f]{40}$"))
            throw new InvalidDataException($"{candidate.Name} {candidate.Version} metadata does not match the registry entry.");
        if (!TryReadApi(candidate.Api, out var indexedHorizon, out var indexedMixins)
            || !horizon.Equals(indexedHorizon, StringComparison.OrdinalIgnoreCase)
            || !mixins.Equals(indexedMixins, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{candidate.Name} {candidate.Version} does not match its registry compatibility record.");
        return new(horizon, mixins, hooks);
    }

    private static bool TryReadApi(string api, out string horizon, out string mixins)
    {
        horizon = "";
        mixins = "";
        var parts = api.Split('/');
        if (parts.Length != 3 || parts[0] != "native-1"
            || !Regex.IsMatch(parts[1], "^[0-9a-f]{40}$") || !Regex.IsMatch(parts[2], "^[0-9a-f]{40}$")) return false;
        horizon = parts[1];
        mixins = parts[2];
        return true;
    }
}

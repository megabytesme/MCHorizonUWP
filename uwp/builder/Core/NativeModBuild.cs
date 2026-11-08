using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Stages verified registry bundles and applies their mixins to an isolated source tree.</summary>
internal static class NativeModBuild
{
    private const string HooksFile = "crates/horizon-client/src/game/native_hooks.rs";
    private sealed record BundleCompatibility(string Horizon, string Mixins, int? Hooks);

    internal static Task<string> ApplyAsync(string source, string workspace, IReadOnlyList<string> requested,
        string cargo, IReadOnlyDictionary<string, string> hostEnvironment, IProgress<string>? progress)
        => ApplyAsync(source, workspace, requested, cargo, hostEnvironment, LocalRecipe.UpstreamNightlyCommit, null, progress);

    internal static async Task<string> ApplyAsync(string source, string workspace, IReadOnlyList<string> requested,
        string cargo, IReadOnlyDictionary<string, string> hostEnvironment, string targetCommit,
        IReadOnlyDictionary<string, string>? expectedVersions, IProgress<string>? progress)
    {
        if (requested.Count == 0) return source;
        if (requested.Count > 100 || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
            throw new InvalidDataException("The selected native-mod list is invalid.");
        if (!Regex.IsMatch(targetCommit, "^[0-9a-f]{40}$")) throw new InvalidDataException("The selected Horizon release has an invalid commit.");
        progress?.Report($"Finding native-mod versions compatible with Horizon commit {targetCommit}.");
        using var transport = new CatalogTransport();
        var stager = await NativeBundleStager.CreateAsync(transport, Path.Combine(workspace, "native-download"), RuntimeInformation.ProcessArchitecture);
        var catalog = await NativeModIndex.FetchCatalogAsync();
        var hookText = await File.ReadAllTextAsync(Path.Combine(source, HooksFile));
        var hookMatch = Regex.Match(hookText, @"(?m)^pub\(crate\) const NATIVE_HOOKS_API: u32 = (\d+);\s*$");
        if (!hookMatch.Success || !int.TryParse(hookMatch.Groups[1].Value, out var availableHooks))
            throw new InvalidDataException("This patch does not declare a supported native-mod hook version.");

        var discovered = new Dictionary<string, List<ResolvedNativeMod>>(StringComparer.Ordinal);
        foreach (var id in requested)
        {
            var listing = catalog.Mods.SingleOrDefault(mod => mod.Id == id)
                ?? throw new InvalidDataException($"{id} is no longer available in Horizon's native-mod registry for Minecraft {LocalRecipe.MinecraftVersion}.");
            var compatible = new List<ResolvedNativeMod>();
            var candidates = listing.Versions.AsEnumerable();
            if (expectedVersions is not null && expectedVersions.TryGetValue(id, out var expectedVersion))
                candidates = candidates.Where(candidate => candidate.Version == expectedVersion);
            foreach (var candidate in candidates)
            {
                if (candidate.BrokenCommits.Contains(targetCommit, StringComparer.OrdinalIgnoreCase))
                {
                    progress?.Report($"Skipping {listing.Name} {candidate.Version}: the registry marks it broken on Horizon {targetCommit}.");
                    continue;
                }
                if (candidate.Url.Length == 0 || candidate.Size <= 0)
                    throw new InvalidDataException($"The registry entry for {listing.Name} {candidate.Version} is missing its download details.");
                progress?.Report($"Checking {listing.Name} {candidate.Version} against Horizon {targetCommit}.");
                var bundlePath = await NativeModPreflight.EnsureBundleAsync(transport, candidate);
                var metadata = ReadCompatibility(bundlePath, candidate);
                var matchesHooks = metadata.Hooks is { } hooks && hooks > 0 && hooks <= availableHooks;
                if (!matchesHooks && !metadata.Horizon.Equals(targetCommit, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report($"{listing.Name} {candidate.Version} targets Horizon {metadata.Horizon}; checking older releases.");
                    continue;
                }
                compatible.Add(new(candidate, bundlePath, metadata.Horizon, metadata.Mixins, metadata.Hooks));
            }
            if (compatible.Count == 0)
                throw new InvalidOperationException(expectedVersions is not null && expectedVersions.ContainsKey(id)
                    ? $"The previously selected {listing.Name} version is no longer compatible with Horizon commit {targetCommit}. Reopen the mod picker to refresh compatibility."
                    : $"No version of {listing.Name} is compatible with Horizon commit {targetCommit} and hook API {availableHooks}.");
            discovered.Add(id, compatible);
        }

        var commonMixins = discovered.Values.First().Select(mod => mod.MixinsRevision).ToHashSet(StringComparer.Ordinal);
        foreach (var options in discovered.Values.Skip(1)) commonMixins.IntersectWith(options.Select(mod => mod.MixinsRevision));
        if (commonMixins.Count == 0)
            throw new InvalidOperationException("The selected native mods have no versions that share a Horizon mixin-tool revision. Choose a different combination.");
        var sharedMixins = commonMixins
            .Select(revision => (Revision: revision, Score: discovered.Values.Sum(options => options.FindIndex(mod => mod.MixinsRevision == revision))))
            .OrderBy(option => option.Score).ThenBy(option => option.Revision, StringComparer.Ordinal).First().Revision;
        var selected = requested.Select(id => discovered[id].First(mod => mod.MixinsRevision == sharedMixins)).ToArray();
        foreach (var mod in selected)
            progress?.Report($"Using {mod.Candidate.Name} {mod.Candidate.Version} with Horizon commit {targetCommit}.");
        var bundles = (await stager.InstallResolvedAsync(catalog, selected)).ToDictionary(bundle => bundle.Id, StringComparer.Ordinal);
        var mods = Path.Combine(workspace, "native-mixins");
        Directory.CreateDirectory(mods);
        string? mixinsRevision = null;
        foreach (var bundle in bundles.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var revision = ExtractBundle(bundle, mods, targetCommit, availableHooks);
            if (mixinsRevision is not null && mixinsRevision != revision)
                throw new InvalidOperationException("These selected mods require different versions of Horizon's mixin tool; choose compatible mods.");
            mixinsRevision = revision;
        }
        if (mixinsRevision is null) throw new InvalidDataException("No native mods were staged.");
        var toolRoot = Path.Combine(workspace, "native-tool");
        progress?.Report("Building Horizon's native-mod transformation tool.");
        await LocalBuildRunner.RunProcess(cargo, workspace,
            ["+" + LocalRecipe.RustToolchain, "install", "--locked", "--git", "https://github.com/horizon-rust/horizon-mixins", "--rev", mixinsRevision,
                "--root", toolRoot, "horizon-mixins-cli"], hostEnvironment, progress: progress);
        var executable = Path.Combine(toolRoot, "bin", "horizon-mixins.exe");
        var staged = Path.Combine(workspace, "native-source");
        await File.WriteAllTextAsync(Path.Combine(source, ".horizon-revision"), targetCommit + "\n");
        var lib = Path.Combine(source, "crates", "horizon-client", "src", "lib.rs");
        var original = await File.ReadAllTextAsync(lib);
        // The upstream transformer mistakes this built-in crate setting for an attribute macro.
        // Remove it only while transforming, and restore it byte-for-byte in the staged output.
        const string attribute = "#![recursion_limit = \"256\"]";
        var index = original.IndexOf(attribute, StringComparison.Ordinal);
        if (index < 0 || original.IndexOf(attribute, index + attribute.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Unexpected client crate settings; cannot safely apply native mods.");
        var stripped = original.Remove(index, attribute.Length);
        try
        {
            await File.WriteAllTextAsync(lib, stripped, new UTF8Encoding(false));
            progress?.Report("Applying the selected native mods to the release source.");
            await LocalBuildRunner.RunProcess(executable, workspace, ["apply", "--workspace", source, "--mods", mods, "--out", staged], hostEnvironment);
        }
        finally { await File.WriteAllTextAsync(lib, original, new UTF8Encoding(false)); }
        var stagedLib = Path.Combine(staged, "crates", "horizon-client", "src", "lib.rs");
        if (await File.ReadAllTextAsync(stagedLib) != stripped)
            throw new InvalidDataException("A native mod changes the client crate root; this combination is not supported yet.");
        await File.WriteAllTextAsync(stagedLib, original, new UTF8Encoding(false));
        return staged;
    }

    private static BundleCompatibility ReadCompatibility(string bundlePath, NativeModCandidate candidate)
    {
        using var zip = ZipFile.OpenRead(bundlePath);
        if (zip.Entries.Count > 512) throw new InvalidDataException($"{candidate.Name} {candidate.Version} contains too many files.");
        var metadataEntries = zip.Entries.Where(entry => entry.FullName == "horizon-native.json").ToArray();
        if (metadataEntries.Length != 1 || metadataEntries[0].Length is <= 0 or > 65536)
            throw new InvalidDataException($"{candidate.Name} {candidate.Version} has invalid native-mod metadata.");
        using var metadataStream = metadataEntries[0].Open();
        using var metadata = JsonDocument.Parse(metadataStream);
        var root = metadata.RootElement;
        var revision = root.GetProperty("mixins").GetString() ?? "";
        var horizon = root.GetProperty("horizon").GetString() ?? "";
        var id = root.GetProperty("id").GetString() ?? "";
        var version = root.GetProperty("version").GetString() ?? "";
        var game = root.GetProperty("game").GetString() ?? "";
        int? hooks = null;
        if (root.TryGetProperty("hooks", out var hooksValue))
        {
            if (hooksValue.ValueKind != JsonValueKind.Number || !hooksValue.TryGetInt32(out var parsedHooks) || parsedHooks <= 0)
                throw new InvalidDataException($"{candidate.Name} {candidate.Version} has an invalid hook API declaration.");
            hooks = parsedHooks;
        }
        if (id != candidate.Id || version != candidate.Version || game != LocalRecipe.MinecraftVersion
            || !Regex.IsMatch(revision, "^[0-9a-f]{40}$") || !Regex.IsMatch(horizon, "^[0-9a-f]{40}$"))
            throw new InvalidDataException($"{candidate.Name} {candidate.Version} metadata does not match the signed registry entry.");
        return new(horizon, revision, hooks);
    }

    private static string ExtractBundle(StagedNativeBundle bundle, string modsRoot, string targetCommit, int availableHooks)
    {
        using var zip = ZipFile.OpenRead(bundle.Path);
        var metadataEntries = zip.Entries.Where(e => e.FullName == "horizon-native.json").ToArray();
        if (metadataEntries.Length != 1 || metadataEntries[0].Length is <= 0 or > 65536)
            throw new InvalidDataException("Native mod metadata is invalid.");
        using var metadataStream = metadataEntries[0].Open();
        using var metadata = JsonDocument.Parse(metadataStream);
        var root = metadata.RootElement;
        var revision = root.GetProperty("mixins").GetString() ?? "";
        var horizon = root.GetProperty("horizon").GetString() ?? "";
        if (root.GetProperty("id").GetString() != bundle.Id || root.GetProperty("game").GetString() != LocalRecipe.MinecraftVersion
            || !Regex.IsMatch(revision, "^[0-9a-f]{40}$") || !Regex.IsMatch(horizon, "^[0-9a-f]{40}$"))
            throw new InvalidDataException("Native mod metadata does not match the selected release.");
        var hasHooks = root.TryGetProperty("hooks", out var hooks);
        if (hasHooks && (hooks.ValueKind != JsonValueKind.Number || !hooks.TryGetInt32(out var required) || required <= 0 || required > availableHooks))
            throw new InvalidDataException($"{bundle.Id} needs a different version of Horizon's native hooks.");
        if (!hasHooks && !horizon.Equals(targetCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{bundle.Id} requires a different Horizon revision.");
        var prefix = $"mixins/{bundle.Id}/";
        var destination = Path.Combine(modsRoot, bundle.Id);
        Directory.CreateDirectory(destination);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == "horizon-native.json") continue;
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                throw new InvalidDataException($"{bundle.Id} contains native-mod data this builder cannot yet include.");
            var relative = entry.FullName[prefix.Length..];
            if (relative.Length == 0 || relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or "..")
                || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000))
                throw new InvalidDataException("Native mod contains an unsafe archive entry.");
            if (entry.Length < 0 || entry.Length > 64L * 1024 * 1024 - total) throw new InvalidDataException("Native mod is too large.");
            total += entry.Length;
            var path = Path.GetFullPath(Path.Combine(destination, relative));
            if (!path.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native mod entry leaves its workspace.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew);
            input.CopyTo(output);
        }
        var manifestPath = Path.Combine(destination, "horizon-mixin.toml");
        var manifest = File.ReadAllText(manifestPath);
        if (Regex.IsMatch(manifest, @"(?im)^\s*\[\[?\s*post[-_]build\b"))
            throw new InvalidDataException($"{bundle.Id} requires a post-build step this builder does not yet support.");
        if (hasHooks)
        {
            var pattern = $"(?m)^([ \\t]*horizon_revision[ \\t]*=[ \\t]*\"{Regex.Escape(horizon)}\")[ \\t]*\\r?$";
            var matches = Regex.Matches(manifest, pattern);
            if (matches.Count != 1) throw new InvalidDataException("Native mod Horizon revision is not unambiguous.");
            manifest = manifest[..matches[0].Index] + matches[0].Value.Replace(horizon, targetCommit, StringComparison.Ordinal)
                + manifest[(matches[0].Index + matches[0].Length)..];
            File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
        }
        return revision;
    }
}

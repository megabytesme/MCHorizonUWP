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

    internal static async Task<string> ApplyAsync(string source, string workspace, IReadOnlyList<string> requested,
        string cargo, IReadOnlyDictionary<string, string> hostEnvironment, IProgress<string>? progress)
    {
        if (requested.Count == 0) return source;
        if (requested.Count > 100 || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
            throw new InvalidDataException("The selected native-mod list is invalid.");
        progress?.Report("Downloading and verifying selected native mods with Horizon.");
        using var transport = new CatalogTransport();
        var stager = await NativeBundleStager.CreateAsync(transport, Path.Combine(workspace, "native-download"), RuntimeInformation.ProcessArchitecture);
        var bundles = new Dictionary<string, StagedNativeBundle>(StringComparer.Ordinal);
        foreach (var id in requested)
            foreach (var bundle in await stager.InstallAsync(id)) bundles[bundle.Id] = bundle;
        var hookText = await File.ReadAllTextAsync(Path.Combine(source, HooksFile));
        var hookMatch = Regex.Match(hookText, @"(?m)^pub\(crate\) const NATIVE_HOOKS_API: u32 = (\d+);\s*$");
        if (!hookMatch.Success || !int.TryParse(hookMatch.Groups[1].Value, out var availableHooks))
            throw new InvalidDataException("This patch does not declare a supported native-mod hook version.");
        var mods = Path.Combine(workspace, "native-mixins");
        Directory.CreateDirectory(mods);
        string? mixinsRevision = null;
        foreach (var bundle in bundles.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var revision = ExtractBundle(bundle, mods, availableHooks);
            if (mixinsRevision is not null && mixinsRevision != revision)
                throw new InvalidOperationException("These selected mods require different versions of Horizon's mixin tool; choose compatible mods.");
            mixinsRevision = revision;
        }
        if (mixinsRevision is null) throw new InvalidDataException("No native mods were staged.");
        var toolRoot = Path.Combine(workspace, "native-tool");
        progress?.Report("Building Horizon's native-mod transformation tool.");
        await LocalBuildRunner.RunProcess(cargo, workspace,
            ["+stable", "install", "--locked", "--git", "https://github.com/horizon-rust/horizon-mixins", "--rev", mixinsRevision,
                "--root", toolRoot, "horizon-mixins-cli"], hostEnvironment);
        var executable = Path.Combine(toolRoot, "bin", "horizon-mixins.exe");
        var staged = Path.Combine(workspace, "native-source");
        await File.WriteAllTextAsync(Path.Combine(source, ".horizon-revision"), LocalRecipe.UpstreamNightlyCommit + "\n");
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

    private static string ExtractBundle(StagedNativeBundle bundle, string modsRoot, int availableHooks)
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
        if (hasHooks && (hooks.ValueKind != JsonValueKind.Number || !hooks.TryGetInt32(out var required) || required < 0 || required > availableHooks))
            throw new InvalidDataException($"{bundle.Id} needs a different version of Horizon's native hooks.");
        if (!hasHooks && horizon != LocalRecipe.UpstreamNightlyCommit)
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
            manifest = manifest[..matches[0].Index] + matches[0].Value.Replace(horizon, LocalRecipe.UpstreamNightlyCommit, StringComparison.Ordinal)
                + manifest[(matches[0].Index + matches[0].Length)..];
            File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
        }
        return revision;
    }
}

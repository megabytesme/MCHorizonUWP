using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

public sealed record StagedNativeBundle(string Id, string Version, string Path, string Sha256, string Verification);
internal sealed record ResolvedNativeMod(NativeModCandidate Candidate, string Path, string HorizonCommit,
    string MixinsRevision, int? Hooks);

/// <summary>Uses a pinned Horizon launcher to verify registry bundles in an isolated workspace.</summary>
public sealed class NativeBundleStager
{
    private readonly string root;
    private readonly string launcher;
    private readonly SemaphoreSlim gate = new(1, 1);
    private NativeBundleStager(string root, string launcher) => (this.root, this.launcher) = (root, launcher);
    internal string Workspace => root;

    public static CatalogArtifact LauncherFor(Architecture architecture)
    {
        var (name, digest) = architecture switch
        {
            Architecture.X64 => ("x64", "add9bbd03e35335cf1750716c603cf2a5591959f56c689fb2ef4e608f01a73d3"),
            Architecture.Arm64 => ("arm64", "51e6493d752bac8824211af975d295197e49093cbf1f3ea82218c0322add95e5"),
            _ => throw new PlatformNotSupportedException("A Horizon launcher is not available for this builder architecture."),
        };
        return new($"https://github.com/horizon-rust/horizon/releases/download/launcher%400.2.42/horizon-launcher-windows-{name}.zip", digest);
    }

    public static async Task<NativeBundleStager> CreateAsync(CatalogTransport transport, string newWorkspace,
        Architecture architecture, CancellationToken cancellationToken = default)
    {
        var artifact = LauncherFor(architecture);
        var root = Path.GetFullPath(newWorkspace);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The native-mod workspace already exists.");
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "launcher.zip");
        await transport.DownloadArtifactAsync(artifact, archive, cancellationToken);
        return OpenVerifiedArchive(archive, root, artifact.Sha256);
    }

    internal static NativeBundleStager OpenVerifiedArchive(string archive, string workspace, string expectedHash)
    {
        using (var stream = File.OpenRead(archive))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Horizon launcher archive does not match the pinned release.");
        using var zip = ZipFile.OpenRead(archive);
        var candidates = zip.Entries.Where(entry => entry.FullName == "horizon-launcher.exe").ToArray();
        if (candidates.Length != 1 || candidates[0].Length is <= 0 or > 120 * 1024 * 1024
            || ((candidates[0].ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000))
            throw new InvalidDataException("The Horizon launcher archive is invalid.");
        var executable = Path.Combine(Path.GetFullPath(workspace), "horizon-launcher.exe");
        using var input = candidates[0].Open();
        using var output = new FileStream(executable, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        return new(Path.GetFullPath(workspace), executable);
    }

    public async Task<IReadOnlyList<StagedNativeBundle>> InstallAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(id, "^[a-z0-9_]{1,64}$")) throw new ArgumentException("Invalid native mod ID.", nameof(id));
        await gate.WaitAsync(cancellationToken);
        try { return await InstallCoreAsync(id, null, null, cancellationToken); }
        finally { gate.Release(); }
    }

    internal async Task<IReadOnlyList<StagedNativeBundle>> InstallResolvedAsync(NativeModCatalog catalog,
        IReadOnlyList<ResolvedNativeMod> mods, CancellationToken cancellationToken = default)
    {
        if (mods.Count == 0 || mods.Count > 100 || mods.Select(mod => mod.Candidate.Id).Distinct(StringComparer.Ordinal).Count() != mods.Count)
            throw new InvalidDataException("The resolved native-mod list is invalid.");
        var indexPath = Path.Combine(root, "verified-compatible-mod-index.json");
        var indexBytes = NativeModIndex.CreatePinnedIndex(catalog, mods.Select(mod => (mod.Candidate, mod.Path)).ToArray());
        await File.WriteAllBytesAsync(indexPath, indexBytes, cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var result = new List<StagedNativeBundle>();
            foreach (var mod in mods)
            {
                var staged = await InstallCoreAsync(mod.Candidate.Id, mod.Candidate.Version, indexPath, cancellationToken);
                if (!staged.Any(bundle => bundle.Id == mod.Candidate.Id && bundle.Version == mod.Candidate.Version))
                    throw new InvalidDataException($"Horizon did not install the selected {mod.Candidate.Name} version {mod.Candidate.Version}.");
                result.AddRange(staged);
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<StagedNativeBundle>> InstallCoreAsync(string id, string? expectedVersion,
        string? modIndexPath, CancellationToken cancellationToken)
    {
        var game = Path.Combine(root, "game");
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var arg in new[] { "--json", "mods", "install", id, "--source", "horizon" }) start.ArgumentList.Add(arg);
        foreach (var (name, folder) in new[] { ("HORIZON_HOME", "game"), ("HORIZON_LAUNCHER_HOME", "launcher-home"),
            ("XDG_DATA_HOME", "xdg-data"), ("XDG_CONFIG_HOME", "xdg-config"), ("HORIZON_LAUNCHER_TRASH", "trash"),
            ("HORIZON_LAUNCHER_CLIPBOARD", "clipboard"), ("HORIZON_LAUNCHER_DESKTOP", "desktop") })
            start.Environment[name] = Path.Combine(root, folder);
        start.Environment["HORIZON_LAUNCHER_NO_SELF_UPDATE"] = "1";
        start.Environment["HORIZON_NO_AUDIO"] = "1";
        if (modIndexPath is null) start.Environment.Remove("HORIZON_MOD_INDEX");
        else start.Environment["HORIZON_MOD_INDEX"] = modIndexPath;
        start.Environment.Remove("HORIZON_GITHUB_TOKEN");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Horizon launcher.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Horizon could not stage this native mod (exit code {process.ExitCode}); workspace retained.");
        using var document = JsonDocument.Parse(await stdout);
        var bundles = new List<StagedNativeBundle>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var project = entry.GetProperty("project").GetString() ?? "";
            var file = entry.GetProperty("file").GetString() ?? "";
            var checksum = entry.GetProperty("checksum").GetString() ?? "";
            var version = entry.GetProperty("version").GetString() ?? "";
            var verification = entry.GetProperty("verdict").GetString() ?? "";
            if (!Regex.IsMatch(project, "^[a-z0-9_]{1,64}$") || file != $"horizon-native-mods/{project}.hnmod"
                || !checksum.StartsWith("SHA-256:", StringComparison.Ordinal) || checksum.Length != 72
                || !checksum[8..].All(char.IsAsciiHexDigit) || version.Length == 0)
                throw new InvalidDataException("Horizon returned an unexpected native-mod result.");
            if (project == id && expectedVersion is not null && version != expectedVersion)
                throw new InvalidDataException($"Horizon selected native mod version {version}, but this build pinned {expectedVersion}.");
            var path = Path.Combine(game, "horizon-native-mods", project + ".hnmod");
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(checksum[8..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The staged native mod does not match Horizon's verified checksum.");
            bundles.Add(new(project, version, path, actual, verification));
        }
        if (!bundles.Any(bundle => bundle.Id == id)) throw new InvalidDataException("Horizon did not stage the requested native mod.");
        return bundles;
    }
}

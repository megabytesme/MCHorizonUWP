using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Builder.Core;

/// <summary>Downloads and privately extracts only the Microsoft CRT files needed for UWP linking.</summary>
internal static class PrivateMsvcToolset
{
    internal const string Version = "14.44.35207";
    internal static string PrivateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Toolchains", "MSVC", Version);
    internal static string DownloadRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Downloads", "MSVC-14.44.35226");

    private const string OwnershipMarker = ".mchorizon-private-msvc";
    private const string OwnershipMarkerContent = "MCHorizonUWP private MSVC CRT components 14.44.35226; toolset layout 14.44.35207\n";
    private const long MaxExtractedBytes = 1024L * 1024 * 1024;
    private const string MicrosoftCdn = "https://download.visualstudio.microsoft.com/download/pr/";

    private sealed record Package(string FileName, CatalogArtifact Artifact);

    private static readonly Package Headers = new(
        $"Microsoft.VC.14.44.17.14.CRT.Headers.base.vsix",
        new(MicrosoftCdn + "c610cd8c-801b-44b8-a80a-82cc382aeb43/852382a9aa73502b7849c1bcadfb603ba7175c4e8b60e6aba03c7de711d4ece5/Microsoft.VC.14.44.17.14.CRT.Headers.base.vsix",
            "852382a9aa73502b7849c1bcadfb603ba7175c4e8b60e6aba03c7de711d4ece5"));

    private static readonly Package DesktopX64 = new(
        "Microsoft.VC.14.44.17.14.CRT.x64.Desktop.base.vsix",
        new(MicrosoftCdn + "67cf767c-5e71-47c2-a54a-cd5631e28942/f01f701a7bcd9587a340898c851424f6a52bb913a70c185ff0d5bf0288c5831a/Microsoft.VC.14.44.17.14.CRT.x64.Desktop.base.vsix",
            "f01f701a7bcd9587a340898c851424f6a52bb913a70c185ff0d5bf0288c5831a"));

    private static readonly IReadOnlyDictionary<string, Package> StorePackages = new Dictionary<string, Package>(StringComparer.Ordinal)
    {
        ["x64"] = new("Microsoft.VC.14.44.17.14.CRT.x64.Store.base.vsix",
            new(MicrosoftCdn + "67cf767c-5e71-47c2-a54a-cd5631e28942/9135b03c0df53c7a0aa9bef7230a1c2ff4263a0ee7baa7e419d034f484f6bb56/Microsoft.VC.14.44.17.14.CRT.x64.Store.base.vsix",
                "9135b03c0df53c7a0aa9bef7230a1c2ff4263a0ee7baa7e419d034f484f6bb56")),
        ["x86"] = new("Microsoft.VC.14.44.17.14.CRT.x86.Store.base.vsix",
            new(MicrosoftCdn + "67cf767c-5e71-47c2-a54a-cd5631e28942/d29d5bd117b1b3a4485a9172d57180417adcb47e004d492b47e031d4c2933dd5/Microsoft.VC.14.44.17.14.CRT.x86.Store.base.vsix",
                "d29d5bd117b1b3a4485a9172d57180417adcb47e004d492b47e031d4c2933dd5")),
        ["arm"] = new("Microsoft.VC.14.44.17.14.CRT.arm.Store.base.vsix",
            new(MicrosoftCdn + "67cf767c-5e71-47c2-a54a-cd5631e28942/ebc804ca48804328e99491c65b172a28fc2537ee1523a16c1aa17dd624450eff/Microsoft.VC.14.44.17.14.CRT.arm.Store.base.vsix",
                "ebc804ca48804328e99491c65b172a28fc2537ee1523a16c1aa17dd624450eff")),
        ["arm64"] = new("Microsoft.VC.14.44.17.14.CRT.ARM64.Store.base.vsix",
            new(MicrosoftCdn + "67cf767c-5e71-47c2-a54a-cd5631e28942/57ece91747be72fdd9ed0c39b83225011c8afba6b6f616a23ace54d0522f79e9/Microsoft.VC.14.44.17.14.CRT.ARM64.Store.base.vsix",
                "57ece91747be72fdd9ed0c39b83225011c8afba6b6f616a23ace54d0522f79e9"))
    };

    internal static bool IsComplete(IProbe probe, string root, string architecture) =>
        architecture is "x86" or "x64" or "arm" or "arm64"
        && BaseFiles(root).All(path => probe.FileExists(Path.Combine(root, path)))
        && StoreFiles(root, architecture).All(path => probe.FileExists(Path.Combine(root, path)));

    internal static async Task EnsureAsync(IProbe probe, string architecture, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateArchitecture(architecture);
        if (VisualCppInstallation.FindInstalledLinkToolset(probe, architecture) is not null) return;
        await EnsurePrivateAsync(architecture, PrivateRoot, DownloadRoot, progress, cancellationToken);
    }

    internal static async Task EnsurePrivateAsync(string architecture, string privateRoot, string downloadRoot,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateArchitecture(architecture);
        privateRoot = Path.GetFullPath(privateRoot);
        downloadRoot = Path.GetFullPath(downloadRoot);
        if (IsComplete(new SystemProbe(), privateRoot, architecture)) return;

        var rootExists = Directory.Exists(privateRoot) || File.Exists(privateRoot);
        if (rootExists && (!Directory.Exists(privateRoot) || !IsOwnedRoot(privateRoot)))
            throw new IOException("The existing private Microsoft C++ toolchain is not builder-managed; it was left unchanged.");

        var baseReady = BaseFiles(privateRoot).All(File.Exists);
        var packages = new List<Package>();
        if (!baseReady) packages.AddRange([Headers, DesktopX64]);
        if (!StoreFiles(privateRoot, architecture).All(File.Exists)) packages.Add(StorePackages[architecture]);
        if (packages.Count == 0) return;

        Directory.CreateDirectory(downloadRoot);
        using var transport = new CatalogTransport();
        var completed = 0;
        await Parallel.ForEachAsync(packages, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken }, async (package, token) =>
        {
            var artifactPath = Path.Combine(downloadRoot, package.FileName);
            progress?.Report($"Checking Microsoft C++ package {package.FileName}…");
            await PinnedDownload.EnsureAsync(transport, package.Artifact, artifactPath, token);
            var index = Interlocked.Increment(ref completed);
            progress?.Report($"Verified Microsoft C++ package {index}/{packages.Count}: {package.FileName}");
        });

        var parent = Path.GetDirectoryName(privateRoot) ?? throw new IOException("The private Microsoft C++ toolchain has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".msvc-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Extracting {package.FileName}…");
                ExtractPackage(Path.Combine(downloadRoot, package.FileName), staging, cancellationToken);
            }

            if ((!baseReady && !BaseFiles(staging).All(File.Exists)) || !StoreFiles(staging, architecture).All(File.Exists))
                throw new InvalidDataException($"The Microsoft C++ packages do not contain the expected UWP linker files for {architecture}.");

            if (!rootExists)
            {
                File.WriteAllText(Path.Combine(staging, OwnershipMarker), OwnershipMarkerContent, new UTF8Encoding(false));
                Directory.Move(staging, privateRoot);
            }
            else
            {
                await MergeOwnedTreeAsync(staging, privateRoot, cancellationToken);
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        if (!IsComplete(new SystemProbe(), privateRoot, architecture))
            throw new InvalidDataException($"The private Microsoft C++ toolchain is incomplete for {architecture}.");
        progress?.Report($"Microsoft C++ libraries are ready for {architecture}.");
    }

    private static IEnumerable<string> BaseFiles(string root) => new[]
    {
        Path.Combine("include", "vcruntime.h"),
        Path.Combine("lib", "x64", "msvcrt.lib"),
        Path.Combine("lib", "x64", "vcruntime.lib"),
        Path.Combine("lib", "x64", "oldnames.lib"),
        Path.Combine("lib", "x64", "libcmt.lib"),
        Path.Combine("lib", "x64", "legacy_stdio_definitions.lib")
    }.Select(path => Path.Combine(root, path));

    private static IEnumerable<string> StoreFiles(string root, string architecture) =>
        new[] { "msvcrt.lib", "vcruntime.lib", "oldnames.lib" }
            .Select(name => Path.Combine(root, "lib", architecture, "store", name));

    private static bool IsOwnedRoot(string root)
    {
        try
        {
            var directory = new DirectoryInfo(root);
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            foreach (var child in directory.EnumerateDirectories("*", SearchOption.AllDirectories))
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            var marker = new FileInfo(Path.Combine(root, OwnershipMarker));
            return marker.Exists && (marker.Attributes & FileAttributes.ReparsePoint) == 0
                && File.ReadAllText(marker.FullName, Encoding.UTF8) == OwnershipMarkerContent;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void ExtractPackage(string archivePath, string staging, CancellationToken cancellationToken)
    {
        const string prefix = "Contents/VC/Tools/MSVC/" + Version + "/";
        long extracted = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var relative = entry.FullName[prefix.Length..];
            if (relative.Length == 0 || relative.EndsWith('/')) continue;
            if (relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("A Microsoft C++ package contains an unsafe path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000))
                throw new InvalidDataException("A Microsoft C++ package contains an unsupported file entry.");
            if (entry.Length < 0 || entry.Length > MaxExtractedBytes - extracted)
                throw new InvalidDataException("A Microsoft C++ package exceeds the extraction size limit.");
            extracted += entry.Length;

            var path = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A Microsoft C++ package entry leaves the private toolchain folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static async Task MergeOwnedTreeAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!IsOwnedRoot(destination))
            throw new IOException("The private Microsoft C++ toolchain ownership marker is missing; existing files were left unchanged.");
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A Microsoft C++ package entry leaves the private toolchain folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (!await HaveSameContentAsync(file, target, cancellationToken))
                    throw new IOException("A private Microsoft C++ file has different content; it was left unchanged.");
            }
            else File.Copy(file, target, overwrite: false);
        }
    }

    private static async Task<bool> HaveSameContentAsync(string first, string second, CancellationToken cancellationToken)
    {
        var a = new FileInfo(first);
        var b = new FileInfo(second);
        if (a.Length != b.Length) return false;
        await using var firstStream = File.OpenRead(first);
        await using var secondStream = File.OpenRead(second);
        var firstHash = await SHA256.HashDataAsync(firstStream, cancellationToken);
        var secondHash = await SHA256.HashDataAsync(secondStream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(firstHash, secondHash);
    }

    private static void ValidateArchitecture(string architecture)
    {
        if (!StorePackages.ContainsKey(architecture)) throw new ArgumentException("Unsupported target architecture.", nameof(architecture));
    }
}

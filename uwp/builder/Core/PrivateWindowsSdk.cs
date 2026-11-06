using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Builder.Core;

/// <summary>
/// Private SDK layout assembled from Microsoft's pinned NuGet packages.
/// </summary>
internal static class PrivateWindowsSdk
{
    internal const string Version = "10.0.19041.0";
    internal static string PrivateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "SDK", "10.0.19041.5");
    internal static string DownloadRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Downloads", "SDK-19041.5");
    private const string PackageVersion = "10.0.19041.5";
    private const string OwnershipMarker = ".mchorizon-private-sdk";
    private const string OwnershipMarkerContent = "MCHorizonUWP Windows SDK package 10.0.19041.5\n";
    private const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly (string Suffix, string Sha256)[] Packages =
    [
        ("", "35074CAFACC99A47E87C25641F98E0952576487FE29607103F83D61540AD0523"),
        (".arm", "89342E1DA1A6C1482F7E50E7CF913AEA22A6124F21545D7AB891CF12B4FF6261"),
        (".arm64", "8780346374B5AC9FFA8E4B85025000578A4060ADCFDB294B41C1637425E7C8BE"),
        (".x86", "7D7213F0A675877437C9B1CA4AB4FA8CBFF9F999A3F0121D2763C17A9B26BB42"),
        (".x64", "EE8B7BC43ED58D17BE795C8062AD32E355847AADAD7DC8096CC04CCD1F2FDC2E")
    ];

    internal static string PackageFileName(string suffix) => $"microsoft.windows.sdk.cpp{suffix}.{PackageVersion}.nupkg";
    private static readonly string[] AllArchitectures = ["x86", "x64", "arm", "arm64"];

    private static string[] SelectArchitectures(IEnumerable<string>? architectures)
    {
        var selected = (architectures ?? AllArchitectures).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0 || selected.Any(arch => !AllArchitectures.Contains(arch, StringComparer.Ordinal)))
            throw new ArgumentException("Select at least one supported SDK architecture.", nameof(architectures));
        return selected;
    }

    private static IEnumerable<(string Suffix, string Sha256)> SelectPackages(string[] architectures) =>
        Packages.Where(package => package.Suffix.Length == 0 || architectures.Contains(package.Suffix[1..], StringComparer.Ordinal));

    // A caller must still validate the selected recipe and its exact toolchain.
    internal static async Task<string> FindOrPrepareAsync(string installedRoot, string privateRoot, string downloadRoot,
        IEnumerable<string>? architectures = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var selected = SelectArchitectures(architectures);
        if (selected.All(arch => IsComplete(installedRoot, [arch, "x64"]))) return Path.GetFullPath(installedRoot);
        if (selected.All(arch => IsComplete(privateRoot, [arch, "x64"]))) return Path.GetFullPath(privateRoot);
        if (Directory.Exists(privateRoot) || File.Exists(privateRoot))
        {
            if (!Directory.Exists(privateRoot) || !IsOwnedPrivateRoot(privateRoot) || !IsComplete(privateRoot, ["x64"]))
                throw new IOException("The existing private SDK is incomplete and is not a builder-managed SDK; it was left unchanged.");
            var missing = selected.Where(arch => !IsComplete(privateRoot, [arch, "x64"])).ToArray();
            if (missing.Length > 0)
            {
                await DownloadAsync(downloadRoot, missing, progress, cancellationToken, includeCommon: false);
                progress?.Report("Reusing the private Windows SDK and adding the selected architecture…");
                await ExtendPrivateAsync(downloadRoot, privateRoot, missing, cancellationToken);
            }
            return Path.GetFullPath(privateRoot);
        }
        if (IsComplete(installedRoot, ["x64"]))
        {
            var missing = selected.Where(arch => !IsComplete(installedRoot, [arch, "x64"])).ToArray();
            await DownloadAsync(downloadRoot, missing, progress, cancellationToken, includeCommon: false);
            progress?.Report("Reusing installed SDK files and adding the selected architecture…");
            await AssembleSupplementAsync(installedRoot, downloadRoot, privateRoot, missing, cancellationToken);
        }
        else
        {
            var required = selected.Append("x64").Distinct(StringComparer.Ordinal).ToArray();
            await DownloadAsync(downloadRoot, required, progress, cancellationToken);
            await AssembleAsync(downloadRoot, privateRoot, required, cancellationToken);
        }
        return Path.GetFullPath(privateRoot);
    }

    internal static async Task DownloadAsync(string destination, IEnumerable<string>? architectures = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool includeCommon = true)
    {
        var selected = SelectArchitectures(architectures);
        Directory.CreateDirectory(destination);
        using var transport = new CatalogTransport();
        await Parallel.ForEachAsync(SelectPackages(selected).Where(package => includeCommon || package.Suffix.Length != 0), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (package, ct) =>
        {
            var name = PackageFileName(package.Suffix);
            var path = Path.Combine(destination, name);
            var id = $"microsoft.windows.sdk.cpp{package.Suffix}";
            if (!File.Exists(path)) progress?.Report($"Downloading {name}");
            await PinnedDownload.EnsureAsync(transport, new CatalogArtifact($"https://api.nuget.org/v3-flatcontainer/{id}/{PackageVersion}/{name}", package.Sha256), path, ct);
        });
    }

    internal static async Task AssembleAsync(string archives, string destination, IEnumerable<string>? architectures = null, CancellationToken cancellationToken = default)
    {
        var selected = SelectArchitectures(architectures);
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("SDK destination already exists.");
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("SDK destination has no parent.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".sdk-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractPackagesAsync(archives, staging, SelectPackages(selected), cancellationToken);
            var missing = MissingFiles(staging, selected, File.Exists);
            if (missing.Count != 0) throw new InvalidDataException("The extracted SDK is incomplete: " + string.Join(", ", missing));
            WriteOwnershipMarker(staging);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static async Task AssembleSupplementAsync(string installedRoot, string archives, string destination,
        IEnumerable<string> architectures, CancellationToken cancellationToken = default)
    {
        var selected = SelectArchitectures(architectures);
        if (!IsComplete(installedRoot, ["x64"])) throw new InvalidDataException("The installed SDK lacks the x64 host files needed for reuse.");
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("SDK destination already exists.");
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("SDK destination has no parent.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".sdk-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var relative in new[] { Path.Combine("Include", Version), Path.Combine("UnionMetadata", Version),
                Path.Combine("References", Version), Path.Combine("bin", Version, "x64"),
                Path.Combine("Lib", Version, "um", "x64"), Path.Combine("Lib", Version, "ucrt", "x64") })
                CopyDirectory(Path.Combine(installedRoot, relative), Path.Combine(staging, relative), cancellationToken);
            await ExtractPackagesAsync(archives, staging, SelectPackages(selected).Where(package => package.Suffix.Length != 0), cancellationToken);
            var missing = MissingFiles(staging, selected.Append("x64"), File.Exists);
            if (missing.Count != 0) throw new InvalidDataException("The supplemented SDK is incomplete: " + string.Join(", ", missing));
            WriteOwnershipMarker(staging);
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static async Task ExtendPrivateAsync(string archives, string destination, IEnumerable<string> architectures,
        CancellationToken cancellationToken)
    {
        var selected = SelectArchitectures(architectures);
        if (!IsOwnedPrivateRoot(destination) || !IsComplete(destination, ["x64"]))
            throw new InvalidDataException("The private SDK is not a complete builder-managed SDK.");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? throw new IOException("SDK destination has no parent.");
        var staging = Path.Combine(parent, ".sdk-extend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractPackagesAsync(archives, staging, SelectPackages(selected).Where(package => package.Suffix.Length != 0), cancellationToken);
            var missingLibraries = selected.SelectMany(arch => new[]
            {
                Path.Combine("Lib", Version, "um", arch, "WindowsApp.lib"),
                Path.Combine("Lib", Version, "ucrt", arch, "ucrt.lib")
            }).Where(path => !File.Exists(Path.Combine(staging, path))).ToArray();
            if (missingLibraries.Length != 0)
                throw new InvalidDataException("The selected SDK architecture is incomplete: " + string.Join(", ", missingLibraries));
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging, source);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    if (!await HasSameContentAsync(source, target, cancellationToken))
                        throw new IOException("A private SDK file already exists with different content; it was left unchanged.");
                }
                else File.Copy(source, target, overwrite: false);
            }
            var required = selected.Append("x64").Distinct(StringComparer.Ordinal).ToArray();
            var missing = MissingFiles(destination, required, File.Exists);
            if (missing.Count != 0) throw new InvalidDataException("The extended private SDK is incomplete: " + string.Join(", ", missing));
            WriteOwnershipMarker(destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static bool IsOwnedPrivateRoot(string root)
    {
        try
        {
            var directory = new DirectoryInfo(root);
            var marker = Path.Combine(root, OwnershipMarker);
            return directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0
                && File.ReadAllText(marker, Encoding.UTF8) == OwnershipMarkerContent;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void WriteOwnershipMarker(string root) => File.WriteAllText(Path.Combine(root, OwnershipMarker), OwnershipMarkerContent, new UTF8Encoding(false));

    private static async Task<bool> HasSameContentAsync(string left, string right, CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        await using var leftStream = File.OpenRead(left);
        await using var rightStream = File.OpenRead(right);
        var leftHash = await SHA256.HashDataAsync(leftStream, cancellationToken);
        var rightHash = await SHA256.HashDataAsync(rightStream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        var info = new DirectoryInfo(source);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("An installed SDK directory is unavailable or is a reparse point.");
        Directory.CreateDirectory(destination);
        foreach (var file in info.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("An installed SDK file is a reparse point.");
            file.CopyTo(Path.Combine(destination, file.Name), overwrite: false);
        }
        foreach (var child in info.EnumerateDirectories()) CopyDirectory(child.FullName, Path.Combine(destination, child.Name), cancellationToken);
    }

    private static async Task ExtractPackagesAsync(string archives, string staging, IEnumerable<(string Suffix, string Sha256)> packages,
        CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var (suffix, digest) in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archive = Path.Combine(archives, PackageFileName(suffix));
            await VerifyAsync(archive, digest, cancellationToken);
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.FullName.StartsWith("c/", StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;
                var name = Uri.UnescapeDataString(entry.FullName[2..]);
                var parts = name.Split('/');
                if (name.Contains('\\') || parts.Any(part => part is "" or "." or ".." || part.Contains(':'))
                    || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000)) throw new InvalidDataException("Unsafe SDK archive entry.");
                if (suffix.Length != 0)
                {
                    if (parts[0] is not ("um" or "ucrt")) continue;
                    parts = ["Lib", Version, .. parts];
                }
                if (entry.Length < 0 || entry.Length > MaxExtractedBytes - total) throw new InvalidDataException("SDK archives are too large.");
                total += entry.Length;
                var path = Path.GetFullPath(Path.Combine([staging, .. parts]));
                if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SDK entry leaves its destination.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var input = entry.Open();
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken);
            }
        }
    }

    internal static bool IsComplete(string root, IEnumerable<string>? architectures = null, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return MissingFiles(root, architectures, exists).Count == 0;
    }

    private static IReadOnlyList<string> MissingFiles(string root, IEnumerable<string>? architectures, Func<string, bool> exists)
    {
        var paths = new List<string>();
        foreach (var name in new[] { "makeappx.exe", "signtool.exe", "makepri.exe", "fxc.exe" }) paths.Add(Path.Combine("bin", Version, "x64", name));
        foreach (var file in new[] { Path.Combine("um", "Windows.h"), Path.Combine("ucrt", "corecrt.h"), Path.Combine("shared", "winapifamily.h"),
            Path.Combine("cppwinrt", "winrt", "base.h"), Path.Combine("winrt", "windows.ui.xaml.h") }) paths.Add(Path.Combine("Include", Version, file));
        paths.Add(Path.Combine("UnionMetadata", Version, "Windows.winmd"));
        foreach (var arch in SelectArchitectures(architectures))
        {
            paths.Add(Path.Combine("Lib", Version, "um", arch, "WindowsApp.lib"));
            paths.Add(Path.Combine("Lib", Version, "ucrt", arch, "ucrt.lib"));
        }
        return paths.Where(path => !exists(Path.Combine(root, path))).ToArray();
    }

    internal static string? FindReady(IProbe probe, IEnumerable<string>? architectures = null)
    {
        var installed = Path.Combine(probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Windows Kits", "10");
        return IsComplete(installed, architectures, probe.FileExists) ? installed
            : IsComplete(PrivateRoot, architectures, probe.FileExists) ? PrivateRoot : null;
    }

    internal static bool AllReady(IProbe probe) => AllArchitectures.All(arch => FindReady(probe, [arch, "x64"]) is not null);

    private static async Task VerifyAsync(string path, string digest, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        if (!actual.Equals(digest, StringComparison.Ordinal)) throw new InvalidDataException($"SDK package digest mismatch: {Path.GetFileName(path)}");
    }
}

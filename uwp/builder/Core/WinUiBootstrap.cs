using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>Restores the exact WinUI 2 package used by the UWP client without requiring NuGet or the .NET SDK.</summary>
internal static class WinUiBootstrap
{
    private const string PackageName = "microsoft.ui.xaml.2.7.3.nupkg";
    private static readonly CatalogArtifact Package = new(
        "https://api.nuget.org/v3-flatcontainer/microsoft.ui.xaml/2.7.3/" + PackageName,
        "9ef3c54aa8c185603ba87d61673efb527062054adc736e27f2c4a033b5f797a8");

    internal static async Task EnsureAsync(string userProfile, string workspace, string architecture, IProgress<string>? progress = null)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64")) throw new ArgumentException("Unsupported WinUI architecture.");
        var parent = Path.Combine(Path.GetFullPath(userProfile), ".nuget", "packages", "microsoft.ui.xaml");
        var target = Path.Combine(parent, "2.7.3");
        if (IsComplete(target, architecture)) return;
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("The existing Microsoft.UI.Xaml 2.7.3 package cache is incomplete; repair it with NuGet before continuing.");
        var archive = Path.Combine(workspace, PackageName);
        progress?.Report("Downloading the pinned Microsoft WinUI framework.");
        using (var transport = new CatalogTransport()) await transport.DownloadArtifactAsync(Package, archive);
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".horizon-2.7.3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        using (var zip = ZipFile.OpenRead(archive))
        {
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.Contains('\\') || name.StartsWith('/') || name.Split('/').Any(part => part is "." or "..")
                    || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000 or 0x4000))
                    throw new InvalidDataException("WinUI package contains an unsafe archive entry.");
                if (entry.Length < 0 || entry.Length > 256L * 1024 * 1024 - total)
                    throw new InvalidDataException("WinUI package is too large.");
                total += entry.Length;
                var destination = Path.GetFullPath(Path.Combine(staging, name));
                if (!destination.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("WinUI package entry leaves its cache directory.");
                if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew);
                input.CopyTo(output);
            }
        }
        if (!IsComplete(staging, architecture)) throw new InvalidDataException("The pinned WinUI package is incomplete.");
        string digest;
        await using (var input = File.OpenRead(archive)) digest = Convert.ToBase64String(await SHA512.HashDataAsync(input));
        File.Copy(archive, Path.Combine(staging, PackageName));
        await File.WriteAllTextAsync(Path.Combine(staging, PackageName + ".sha512"), digest);
        await File.WriteAllTextAsync(Path.Combine(staging, ".nupkg.metadata"), JsonSerializer.Serialize(new
            { version = 2, contentHash = digest, source = "https://api.nuget.org/v3/index.json" }));
        try { Directory.Move(staging, target); }
        catch (IOException) when (IsComplete(target, architecture))
        {
            // Another NuGet process won the race. Leave our isolated staging folder for later cleanup.
        }
    }

    private static bool IsComplete(string root, string architecture) =>
        File.Exists(Path.Combine(root, "lib", "uap10.0", "Microsoft.UI.Xaml.winmd"))
        && File.Exists(Path.Combine(root, "tools", "AppX", architecture, "Release", "Microsoft.UI.Xaml.2.7.appx"));
}

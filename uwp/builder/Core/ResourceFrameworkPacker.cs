using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record ResourceFramework(string Package, string Name);

/// <summary>Packages a private prepared resource snapshot for reuse by clients.</summary>
public static class ResourceFrameworkPacker
{
    public const string Publisher = "CN=HorizonUWPPrototype";

    public static async Task<ResourceFramework> PackAsync(string resources, string newOutputDirectory, IProgress<string>? progress = null)
    {
        var gameVersion = ReadGameVersion(resources);
        var output = Path.GetFullPath(newOutputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new framework output directory.");
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException("The framework output parent does not exist.");
        if (LocalBuildRunner.ResourcesContainWorkspace(resources, output))
            throw new InvalidDataException("Framework output cannot be inside the selected resources.");
        var makeAppx = SyntheticFrameworkPacker.FindMakeAppx();
        var stage = Path.Combine(output, "framework");
        Directory.CreateDirectory(Path.Combine(stage, "Assets"));
        var stagedResources = Path.Combine(stage, "resources");
        progress?.Report("Copying prepared resources to a private framework workspace.");
        await Task.Run(() => LocalBuildRunner.StageResources(resources, stagedResources));
        if (ReadGameVersion(stagedResources) != gameVersion)
            throw new InvalidDataException("The resource version changed while staging; choose a stable resource directory.");
        await using (var logo = typeof(ResourceFrameworkPacker).Assembly.GetManifestResourceStream(
            "Horizon.Builder.Core.Assets.FrameworkLogo.png") ?? throw new InvalidOperationException("Framework logo missing."))
        await using (var file = new FileStream(Path.Combine(stage, "Assets", "StoreLogo.png"), FileMode.CreateNew))
            await logo.CopyToAsync(file);

        // Include paths and content hashes from the staged copy, so different
        // prepared trees cannot silently replace one another as one framework.
        progress?.Report("Computing the resource framework identity.");
        var digest = await Task.Run(() => ResourceDigest(stagedResources));
        var prefix = "Horizon.Asset." + gameVersion.Replace('.', '-') + ".";
        var digestLength = Math.Min(30, 50 - prefix.Length);
        var name = prefix + digest[..digestLength];
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var manifest = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "Package",
                new XElement(ns + "Identity", new XAttribute("Name", name), new XAttribute("Publisher", Publisher),
                    new XAttribute("Version", "1.0.0.0"), new XAttribute("ProcessorArchitecture", "neutral")),
                new XElement(ns + "Properties", new XElement(ns + "Framework", "true"),
                    new XElement(ns + "DisplayName", "Horizon Minecraft resources"),
                    new XElement(ns + "PublisherDisplayName", "Horizon UWP Prototype"),
                    new XElement(ns + "Logo", @"Assets\StoreLogo.png")),
                new XElement(ns + "Dependencies", new XElement(ns + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Universal"), new XAttribute("MinVersion", "10.0.15063.0"),
                    new XAttribute("MaxVersionTested", "10.0.15063.0"))),
                new XElement(ns + "Resources", new XElement(ns + "Resource", new XAttribute("Language", "en-us")))));
        using (var file = new FileStream(Path.Combine(stage, "AppxManifest.xml"), FileMode.CreateNew)) manifest.Save(file);

        var package = Path.Combine(output, name + ".appx");
        progress?.Report("Building the resource framework package. This may take several minutes.");
        var start = new ProcessStartInfo(makeAppx) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "pack", "/d", stage, "/p", package }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start MakeAppx.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr); // Tool output can contain private resource paths.
        if (process.ExitCode != 0 || !File.Exists(package))
            throw new InvalidOperationException($"MakeAppx failed (exit code {process.ExitCode}); framework workspace retained.");
        return new(package, name);
    }

    private static string ReadGameVersion(string resources)
    {
        var manifest = Path.Combine(Path.GetFullPath(resources), "horizon-resources.json");
        var info = new FileInfo(manifest);
        if (!info.Exists || info.Length > 1024 * 1024) throw new InvalidDataException("Select prepared resources with a valid Horizon manifest.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var version = document.RootElement.TryGetProperty("minecraft", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        if (version is null || !Regex.IsMatch(version, @"^[0-9]{1,5}(\.[0-9]{1,5}){1,3}$", RegexOptions.CultureInvariant)
            || ("Horizon.Asset." + version + ".").Length > 30)
            throw new InvalidDataException("The prepared resources have an unsupported Minecraft version.");
        return version;
    }

    private static string ResourceDigest(string root)
    {
        using var treeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(root, path).Replace('\\', '/')))
            .OrderBy(item => item.Relative, StringComparer.Ordinal))
        {
            var relative = Encoding.UTF8.GetBytes(file.Relative);
            treeHash.AppendData(BitConverter.GetBytes(relative.Length));
            treeHash.AppendData(relative);
            using var input = File.OpenRead(file.Path);
            treeHash.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(treeHash.GetHashAndReset()).ToLowerInvariant();
    }
}

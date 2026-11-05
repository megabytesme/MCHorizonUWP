using System.IO.Compression;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

/// <summary>Locates and verifies the Microsoft framework packages needed when sideloading.</summary>
internal static class FrameworkDependencies
{
    private const string Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
    // Microsoft Visual C++ 14.44.35208 framework redistributables. The CAB and
    // its contained APPX are both Microsoft-signed; the digest pins the payload.
    private static readonly CatalogArtifact RuntimeCab = new(
        "https://download.visualstudio.microsoft.com/download/pr/d2fa077f-a814-4fb2-b903-1fca7658d17e/dd4dbe036d235dd902ef41f2cb3ec7ce7ab1ebf93e6cc26fb6d7283301188e5a/cab1.cab",
        "dd4dbe036d235dd902ef41f2cb3ec7ce7ab1ebf93e6cc26fb6d7283301188e5a");
    private static readonly CatalogArtifact RuntimeArm64Cab = new(
        "https://download.visualstudio.microsoft.com/download/pr/d2fa077f-a814-4fb2-b903-1fca7658d17e/7c0c4411e978befc4f2e577ddebdae154798a82214d14b47c63c2c2dc17c2bbd/cab1.cab",
        "7c0c4411e978befc4f2e577ddebdae154798a82214d14b47c63c2c2dc17c2bbd");

    internal static async Task<IReadOnlyList<string>> FindAsync(string architecture, string programFilesX86,
        string userProfile, string signTool, string workingDirectory)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64")) throw new ArgumentException("Unsupported framework architecture.");
        var runtime = Path.Combine(programFilesX86, "Microsoft SDKs", "Windows Kits", "10", "ExtensionSDKs",
            "Microsoft.VCLibs", "14.0", "Appx", "Retail", architecture, $"Microsoft.VCLibs.{architecture}.14.00.appx");
        if (!File.Exists(runtime)) runtime = await DownloadRuntimeAsync(architecture, signTool, workingDirectory);
        var xaml = Path.Combine(userProfile, ".nuget", "packages", "microsoft.ui.xaml", "2.7.3", "tools", "AppX", architecture, "Release", "Microsoft.UI.Xaml.2.7.appx");
        foreach (var (path, name, minimum) in new[] { (runtime, "Microsoft.VCLibs.140.00", "14.0.0.0"), (xaml, "Microsoft.UI.Xaml.2.7", "7.2208.15002.0") })
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"The {architecture} Microsoft framework {name} is missing. Install the UWP SDK components and restore Microsoft.UI.Xaml 2.7.3 before building.", path);
            await LocalBuildRunner.RunProcess(signTool, workingDirectory, ["verify", "/pa", path]);
            using var zip = ZipFile.OpenRead(path);
            var manifest = zip.GetEntry("AppxManifest.xml") ?? throw new InvalidDataException("A Microsoft framework manifest is missing.");
            using var stream = manifest.Open();
            var document = XDocument.Load(stream);
            var identity = document.Root?.Element(document.Root.Name.Namespace + "Identity") ?? throw new InvalidDataException("A Microsoft framework identity is missing.");
            if ((string?)identity.Attribute("Name") != name || (string?)identity.Attribute("Publisher") != Publisher
                || !string.Equals((string?)identity.Attribute("ProcessorArchitecture"), architecture, StringComparison.OrdinalIgnoreCase)
                || !Version.TryParse((string?)identity.Attribute("Version"), out var version) || version < new Version(minimum))
                throw new InvalidDataException($"The {name} package does not match the target architecture and version.");
        }
        return [runtime, xaml];
    }

    private static async Task<string> DownloadRuntimeAsync(string architecture, string signTool, string workspace)
    {
        var directory = Path.Combine(Path.GetFullPath(workspace), "microsoft-vclibs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cab = Path.Combine(directory, "runtime.cab");
        using (var transport = new CatalogTransport())
            await transport.DownloadArtifactAsync(architecture == "arm64" ? RuntimeArm64Cab : RuntimeCab, cab);
        await LocalBuildRunner.RunProcess(signTool, directory, ["verify", "/pa", cab]);
        var name = $"Microsoft.VCLibs.{architecture}.14.00.appx";
        var expand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe");
        await LocalBuildRunner.RunProcess(expand, directory, [cab, "-F:" + name, directory]);
        var package = Path.Combine(directory, name);
        if (!File.Exists(package)) throw new InvalidDataException("The Microsoft VC runtime package was missing from its signed payload.");
        return package;
    }
}

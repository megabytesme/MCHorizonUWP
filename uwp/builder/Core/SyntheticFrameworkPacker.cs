using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

/// <summary>Creates an unsigned framework containing only a synthetic marker.</summary>
public static class SyntheticFrameworkPacker
{
    public const string Publisher = "CN=Horizon.Builder.Synthetic";

    public static string PackageName(string gameVersion)
    {
        if (!Regex.IsMatch(gameVersion, @"^[0-9]{1,5}(\.[0-9]{1,5}){1,3}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Game version must have two to four numeric components.");
        return "Horizon.Synthetic.Minecraft" + gameVersion.Replace('.', '-');
    }

    public static async Task<string> PackAsync(string newOutputDirectory, string gameVersion, TargetProfile target)
    {
        var name = PackageName(gameVersion);
        var architecture = target switch
        {
            TargetProfile.DesktopX64 => "x64",
            TargetProfile.SurfaceProX => "arm64",
            TargetProfile.WindowsMobile => "arm",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        var makeAppx = FindMakeAppx();
        var output = Path.GetFullPath(newOutputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Choose a new output directory; existing files are never overwritten.");
        if (!Directory.Exists(Path.GetDirectoryName(output)))
            throw new DirectoryNotFoundException("The parent of the output directory does not exist.");

        var stage = Path.Combine(output, "framework");
        Directory.CreateDirectory(Path.Combine(stage, "Assets"));
        Directory.CreateDirectory(Path.Combine(stage, "Payload"));
        await using (var logo = typeof(SyntheticFrameworkPacker).Assembly.GetManifestResourceStream(
            "Horizon.Builder.Core.Assets.FrameworkLogo.png") ?? throw new InvalidOperationException("Framework logo missing."))
        await using (var file = new FileStream(Path.Combine(stage, "Assets", "StoreLogo.png"), FileMode.CreateNew))
            await logo.CopyToAsync(file);
        await using (var marker = new StreamWriter(new FileStream(Path.Combine(stage, "Payload", "marker.txt"), FileMode.CreateNew), new UTF8Encoding(false)))
            await marker.WriteAsync("Synthetic framework test data.\n");

        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var manifest = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "Package",
                new XElement(ns + "Identity", new XAttribute("Name", name), new XAttribute("Publisher", Publisher),
                    new XAttribute("Version", "1.0.0.0"), new XAttribute("ProcessorArchitecture", architecture)),
                new XElement(ns + "Properties", new XElement(ns + "Framework", "true"),
                    new XElement(ns + "DisplayName", "Horizon synthetic asset framework"),
                    new XElement(ns + "PublisherDisplayName", "Horizon builder test"),
                    new XElement(ns + "Logo", @"Assets\StoreLogo.png")),
                new XElement(ns + "Dependencies", new XElement(ns + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Universal"), new XAttribute("MinVersion", "10.0.15063.0"),
                    new XAttribute("MaxVersionTested", "10.0.15063.0"))),
                new XElement(ns + "Resources", new XElement(ns + "Resource", new XAttribute("Language", "en-us")))));
        using (var file = new FileStream(Path.Combine(stage, "AppxManifest.xml"), FileMode.CreateNew))
            manifest.Save(file);

        var package = Path.Combine(output, name + ".appx");
        var start = new ProcessStartInfo(makeAppx) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "pack", "/d", stage, "/p", package }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start MakeAppx.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(package))
            throw new InvalidOperationException($"MakeAppx failed (exit code {process.ExitCode}); inspect the new staging directory.");
        return package;
    }

    internal static string FindMakeAppx()
    {
        var kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (!Directory.Exists(kits)) throw new FileNotFoundException("Install the Windows SDK packaging tools to create a framework.");
        var candidates = Directory.GetDirectories(kits)
            .Select(path => (Path: path, Version: Version.TryParse(Path.GetFileName(path), out var version) ? version : null))
            .Where(item => item.Version is not null && item.Version >= new Version(10, 0, 15063, 0))
            .OrderBy(item => item.Version)
            .Select(item => Path.Combine(item.Path, "x64", "makeappx.exe"));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("MakeAppx was not found in the standard Windows SDK location.");
    }
}

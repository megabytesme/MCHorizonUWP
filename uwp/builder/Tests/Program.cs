using Horizon.Builder.Core;

if (args.Contains("--scan"))
{
    foreach (var profile in Enum.GetValues<TargetProfile>())
    {
        Console.WriteLine(profile);
        foreach (var check in new SetupChecker(new SystemProbe()).Scan(profile))
            Console.WriteLine($"  {check.Name}: {check.Status} — {check.Detail}");
    }
    return;
}

var selection = new BuildSelection(TargetProfile.WindowsMobile, true);
Require(selection.RustTarget == "thumbv7a-uwp-windows-msvc", "mobile target");
Require(selection.Features.SequenceEqual(["native-d3d11", "native-touch-mod"]), "native touch feature selection");
Require(new BuildSelection(TargetProfile.SurfaceProX, false).RustTarget == "aarch64-uwp-windows-msvc", "Pro X target");
Require(new BuildSelection(TargetProfile.DesktopX64, false).Features.SequenceEqual(["native-d3d11"]), "touch can be omitted");
try { _ = new BuildSelection((TargetProfile)99, false).RustTarget; throw new Exception("invalid target accepted"); }
catch (ArgumentOutOfRangeException) { }

var missing = new SetupChecker(new FakeProbe()).Scan(TargetProfile.WindowsMobile);
Require(missing.Single(c => c.Name == "Git").Status == CheckStatus.Missing, "missing Git");
Require(missing.Single(c => c.Name == "Windows SDK and packaging tools").Detail.Contains("15063"), "mobile SDK required");
Require(missing.Any(c => c.Status == CheckStatus.Review && c.Name == "Rust UWP standard library"), "unverified UWP std");

var modernOnly = new FakeProbe();
var modernKits = Path.Combine(modernOnly.Root, "Windows Kits", "10");
var modernSdk = Path.Combine(modernKits, "bin", "10.0.26100.0");
modernOnly.AddDirectory(Path.Combine(modernKits, "bin"), modernSdk);
modernOnly.Files.UnionWith([Path.Combine(modernSdk, "x64", "makeappx.exe"), Path.Combine(modernSdk, "x64", "signtool.exe"), Path.Combine(modernKits, "Include", "10.0.26100.0", "um", "Windows.h")]);
Require(new SetupChecker(modernOnly).Scan(TargetProfile.WindowsMobile).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Missing, "modern SDK cannot substitute for 15063");
Require(new SetupChecker(modernOnly).Scan(TargetProfile.DesktopX64).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Found, "modern SDK detected for desktop");

var fake = new FakeProbe();
fake.Executables.Add("git.exe");
var kits = Path.Combine(fake.Root, "Windows Kits", "10");
var sdk = Path.Combine(kits, "bin", "10.0.15063.0");
fake.AddDirectory(Path.Combine(kits, "bin"), sdk);
fake.Files.UnionWith([Path.Combine(sdk, "x64", "makeappx.exe"), Path.Combine(sdk, "x64", "signtool.exe"), Path.Combine(kits, "Include", "10.0.15063.0", "um", "Windows.h")]);
var vs = Path.Combine(fake.Root, "Microsoft Visual Studio");
var year = Path.Combine(vs, "2022"); var edition = Path.Combine(year, "BuildTools"); var versions = Path.Combine(edition, "VC", "Tools", "MSVC"); var toolset = Path.Combine(versions, "14.44");
fake.AddDirectory(vs, year); fake.AddDirectory(year, edition); fake.AddDirectory(versions, toolset);
fake.Files.UnionWith([Path.Combine(toolset, "bin", "Hostx64", "arm", "cl.exe"), Path.Combine(toolset, "lib", "arm", "store", "vcruntime.lib")]);
var mobile = new SetupChecker(fake).Scan(TargetProfile.WindowsMobile);
Require(mobile.Single(c => c.Name == "Git").Status == CheckStatus.Found, "Git found");
Require(mobile.Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Found, "mobile SDK found");
Require(mobile.Single(c => c.Name == "Visual C++ UWP tools").Status == CheckStatus.Found, "ARM tools found");
var desktop = new SetupChecker(fake).Scan(TargetProfile.DesktopX64);
Require(desktop.Single(c => c.Name == "Visual C++ UWP tools").Status == CheckStatus.Missing, "ARM tools do not satisfy x64");
fake.Files.Remove(Path.Combine(sdk, "x64", "signtool.exe"));
Require(new SetupChecker(fake).Scan(TargetProfile.WindowsMobile).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Missing, "incomplete SDK rejected");
Console.WriteLine("Setup checker tests passed.");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class FakeProbe : IProbe
{
    public string Root => @"C:\FakeProgramFiles";
    public HashSet<string> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> directories = new(StringComparer.OrdinalIgnoreCase);
    public void AddDirectory(string parent, string child) { if (!directories.TryGetValue(parent, out var list)) directories[parent] = list = []; list.Add(child); }
    public string? FindOnPath(string executable) => Executables.Contains(executable) ? executable : null;
    public bool FileExists(string path) => Files.Contains(path);
    public IEnumerable<string> Directories(string path) => directories.TryGetValue(path, out var value) ? value : [];
    public string? EnvironmentValue(string name) => Root;
}

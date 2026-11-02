using Horizon.Builder.Core;

if (args.Length == 3 && args[0] == "--validate-local")
{
    var probe = new SystemProbe();
    var localSelection = new BuildSelection(TargetProfile.WindowsMobile, true);
    await LocalRecipe.ValidateSourceAsync(args[1], probe);
    LocalRecipe.ValidateResources(args[2]);
    var blockers = LocalRecipe.ToolchainBlockers(localSelection, probe, await LocalRecipe.InspectRustAsync(probe));
    foreach (var blocker in blockers) Console.WriteLine(blocker);
    foreach (var step in LocalRecipe.Plan(localSelection).Steps) Console.WriteLine(step.Name);
    Console.WriteLine($"Prerequisite blockers: {blockers.Count}");
    return;
}

if (args.Contains("--expect-missing-winget"))
{
    try { await WindowsPackageManager.InstallAsync(new SystemProbe(), InstallableTool.Rustup); throw new Exception("missing WinGet accepted"); }
    catch (InvalidOperationException error) { Require(error.Message.Contains("not found"), "missing WinGet reported"); }
    Console.WriteLine("Missing Windows Package Manager reported without installing anything.");
    return;
}

if (args.Contains("--expect-missing-framework-sdk"))
{
    var output = Path.Combine(Path.GetTempPath(), "HorizonBuilderMissingSdk-" + Guid.NewGuid().ToString("N"));
    try { await SyntheticFrameworkPacker.PackAsync(output, "26.3", TargetProfile.DesktopX64); throw new Exception("missing SDK accepted"); }
    catch (FileNotFoundException) { Require(!Directory.Exists(output), "missing SDK must not create staging"); }
    Console.WriteLine("Missing SDK reported without creating a package.");
    return;
}

if (args.Contains("--framework-probe"))
{
    try { _ = SyntheticFrameworkPacker.PackageName("../invalid"); throw new Exception("invalid version accepted"); }
    catch (ArgumentException) { }
    var root = Path.Combine(Path.GetTempPath(), "HorizonBuilderFrameworkProbe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    foreach (var (target, architecture) in new[] { (TargetProfile.DesktopX64, "x64"), (TargetProfile.WindowsMobile, "arm") })
    {
        var output = Path.Combine(root, architecture);
        var package = await SyntheticFrameworkPacker.PackAsync(output, "26.3", target);
        using var zip = System.IO.Compression.ZipFile.OpenRead(package);
        var entry = zip.GetEntry("AppxManifest.xml") ?? throw new Exception("missing framework manifest");
        using var stream = entry.Open();
        var manifest = System.Xml.Linq.XDocument.Load(stream);
        var ns = manifest.Root!.Name.Namespace;
        var identity = manifest.Root.Element(ns + "Identity")!;
        Require(identity.Attribute("Name")!.Value == "Horizon.Synthetic.Minecraft26-3", "version identity");
        Require(identity.Attribute("ProcessorArchitecture")!.Value == architecture, "framework architecture");
        Require(manifest.Root.Element(ns + "Properties")!.Element(ns + "Framework")!.Value == "true", "framework property");
        Require(zip.GetEntry("Payload/marker.txt") is not null, "synthetic marker");
        Require(zip.GetEntry("AppxSignature.p7x") is null, "package must be unsigned");
        try { await SyntheticFrameworkPacker.PackAsync(output, "26.3", target); throw new Exception("existing output accepted"); }
        catch (IOException) { }
        Console.WriteLine($"Verified unsigned synthetic {architecture} framework: {package}");
    }
    return;
}

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
var recipe = LocalRecipe.Plan(selection);
Require(recipe.SourceCommit == LocalRecipe.SourceCommit && recipe.Target == "thumbv7a-uwp-windows-msvc", "pinned ARM recipe");
Require(recipe.Steps.Any(step => step.Name == "Prepare ARM32 overrides" && step.Arguments.Contains("--offline-arm32")), "ARM offline override selected");
Require(recipe.Notes.Any(note => note.Contains("HTTPS")), "ARM HTTPS limitation disclosed");
Require(recipe.Steps.Single(step => step.Name == "Build game bridge").Arguments.Contains("native-d3d11,native-touch-mod"), "touch build features");
var desktopRecipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false));
Require(desktopRecipe.Steps.All(step => !step.Arguments.Contains("--offline-arm32")), "desktop excludes ARM override");
try { _ = LocalRecipe.Plan(new BuildSelection(TargetProfile.SurfaceProX, false)); throw new Exception("unverified ARM64 recipe accepted"); }
catch (NotSupportedException) { }
var missingToolchain = LocalRecipe.ToolchainBlockers(selection, new FakeProbe(), new RustToolchainFacts("rustc 1.98.0", false));
Require(missingToolchain.Any(blocker => blocker.Contains("1.99.1")), "rust version pinned");
Require(missingToolchain.Any(blocker => blocker.Contains("rust-src")), "rust-src checked");
Require(WindowsPackageManager.Arguments(InstallableTool.Rustup).SequenceEqual(["install", "--id", "Rustlang.Rustup", "--exact", "--source", "winget", "--interactive", "--no-upgrade"]), "fixed interactive Rustup install");
Require(WindowsPackageManager.Arguments(InstallableTool.Git)[2] == "Git.Git", "fixed Git package");
Require(WindowsPackageManager.Arguments(InstallableTool.Python)[2] == "Python.Python.3.13", "fixed Python package");
try { _ = WindowsPackageManager.Arguments((InstallableTool)99); throw new Exception("invalid package accepted"); }
catch (ArgumentOutOfRangeException) { }
try { await WindowsPackageManager.InstallAsync(new FakeProbe(), InstallableTool.Rustup); throw new Exception("missing WinGet accepted"); }
catch (InvalidOperationException error) { Require(error.Message.Contains("not found"), "missing WinGet reported"); }
Require(selection.RustTarget == "thumbv7a-uwp-windows-msvc", "mobile target");
Require(selection.Features.SequenceEqual(["native-d3d11", "native-touch-mod"]), "native touch feature selection");
Require(selection.ModIds.SequenceEqual(["horizon_touch"]), "stable native touch mod ID");
Require(new BuildSelection(TargetProfile.SurfaceProX, false).RustTarget == "aarch64-uwp-windows-msvc", "Pro X target");
Require(new BuildSelection(TargetProfile.DesktopX64, false).Features.SequenceEqual(["native-d3d11"]), "touch can be omitted");
Require(new BuildSelection(TargetProfile.DesktopX64, false).ModIds.Count == 0, "omitted mod ID");
try { _ = new BuildSelection((TargetProfile)99, false).RustTarget; throw new Exception("invalid target accepted"); }
catch (ArgumentOutOfRangeException) { }

string hash = new('a', 64), commit = new('b', 40);
var inputs = BuildInputs.Create(selection, commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.ModIds.SequenceEqual(["horizon_touch"]), "input record includes stable mod ID");
var repeated = BuildInputs.Create(selection, commit.ToUpperInvariant(), hash.ToUpperInvariant(), hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() == repeated.Sha256(), "canonical input hash");
var withoutTouch = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, false), commit, hash, hash, hash, hash, null, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != withoutTouch.Sha256(), "touch selection changes input hash");
try { _ = BuildInputs.Create(selection, commit, hash, hash, hash, hash, null, "stable", "14.44", "10.0.15063.0"); throw new Exception("missing touch digest accepted"); }
catch (ArgumentException) { }
try { _ = BuildInputs.Create(selection, commit, "not-a-hash", hash, hash, hash, hash, "stable", "14.44", "10.0.15063.0"); throw new Exception("invalid digest accepted"); }
catch (ArgumentException) { }
try { _ = BuildInputs.Create(selection, commit, hash, hash, hash, hash, hash, "C:\\secret", "14.44", "10.0.15063.0"); throw new Exception("path accepted as toolchain"); }
catch (ArgumentException) { }

var missing = new SetupChecker(new FakeProbe()).Scan(TargetProfile.WindowsMobile);
Require(missing.Single(c => c.Name == "Git").Status == CheckStatus.Missing, "missing Git");
Require(missing.Single(c => c.Name == "Git").Instructions?.Host == "git-scm.com", "official Git instructions");
Require(missing.Single(c => c.Name == "Windows SDK and packaging tools").Instructions?.Host == "learn.microsoft.com", "official SDK instructions");
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

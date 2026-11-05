using Horizon.Builder.Core;

if (args.Length == 1 && args[0] == "--nightly-probe")
{
    var latest = await NightlyRelease.FetchAsync();
    Console.WriteLine($"Published nightly source {latest.SourceCommit}, package version {LocalRecipe.PackageVersionFor(latest.PublishedAt)}");
    return;
}

if (args.Length == 3 && args[0] == "--pack-resources")
{
    var framework = await ResourceFrameworkPacker.PackAsync(args[1], args[2]);
    Console.WriteLine($"Unsigned resource framework: {framework.Package}");
    Console.WriteLine($"Framework identity: {framework.Name}");
    return;
}

if (args.Length == 2 && args[0] == "--framework-client-probe")
{
    var root = Path.Combine(Path.GetTempPath(), "HorizonFrameworkClientProbe-" + Guid.NewGuid().ToString("N"));
    var resources = Path.Combine(root, "source");
    Directory.CreateDirectory(resources);
    File.WriteAllText(Path.Combine(resources, "horizon-resources.json"), "{\"minecraft\":\"26.3\"}");
    File.WriteAllText(Path.Combine(resources, "fixture.txt"), "synthetic fixture");
    var framework = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "framework-output"));
    var output = Path.Combine(root, "client-output");
    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var arg in new[] { "-NoProfile", "-File", Path.GetFullPath("uwp/host/package.ps1"), "-Architecture", "x64",
        "-Executable", Path.GetFullPath(args[1]), "-OutputDirectory", output, "-ResourceFrameworkName", framework.Name })
        start.ArgumentList.Add(arg);
    using (var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("package script did not start"))
    {
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "client packaging succeeded");
    }
    using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(output, "HorizonUWP-x64.appx"));
    using var manifestStream = (zip.GetEntry("AppxManifest.xml") ?? throw new Exception("client manifest missing")).Open();
    var manifest = System.Xml.Linq.XDocument.Load(manifestStream);
    var ns = manifest.Root!.Name.Namespace;
    var dependency = manifest.Root.Element(ns + "Dependencies")!.Elements(ns + "PackageDependency")
        .Single(element => (string?)element.Attribute("Name") == framework.Name);
    Require((string?)dependency.Attribute("Publisher") == ResourceFrameworkPacker.Publisher, "framework dependency publisher");
    Require(zip.GetEntry("resources/fixture.txt") is null, "resources are separate from the client");
    Console.WriteLine("Verified framework and dependent client packaging using synthetic resources.");
    return;
}

if (args.Length == 4 && args[0] == "--prepare-sounds")
{
    try
    {
        Require(LocalRecipe.SoundWarning(args[2]) is not null, "fixture starts without sounds");
        var prepared = await LocalBuildRunner.PrepareSoundResourcesAsync(args[1], args[2], args[3], new SystemProbe(), new ConsoleProgress());
        Require(LocalRecipe.SoundWarning(prepared) is null, "prepared sounds present");
        Require(LocalRecipe.SoundWarning(args[2]) is not null, "selected resources remain unchanged");
        Console.WriteLine($"Prepared private sound resources: {prepared}");
    }
    catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    return;
}

if (args.Length == 4 && (args[0] == "--build-local" || args[0] == "--build-local-framework"))
{
    try
    {
        var profile = args[3] switch { "x64" => TargetProfile.DesktopX64, "arm" => TargetProfile.WindowsMobile, _ => throw new ArgumentException("Expected x64 or arm.") };
        var result = await LocalBuildRunner.RunAsync(args[1], args[2],
            new BuildSelection(profile, true, UseResourceFramework: args[0] == "--build-local-framework"),
            new SystemProbe(), new ConsoleProgress());
        Console.WriteLine($"Unsigned package: {result.UnsignedPackage}");
        if (result.ResourceFrameworkPackage is { } framework) Console.WriteLine($"Unsigned resource framework: {framework}");
        if (result.SoundWarning is not null) Console.WriteLine(result.SoundWarning);
    }
    catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    return;
}

if (args.Length == 2 && args[0] == "--snapshot-local")
{
    try
    {
        var root = await LocalBuildRunner.CreateSnapshotAsync(args[1], new SystemProbe());
        Require(File.Exists(Path.Combine(root, "source", "mods", "horizon-touch-native", "Cargo.toml")), "touch mod present in snapshot");
        Require(File.Exists(Path.Combine(root, "source", "uwp", "host", "build-game.cmd")), "checked-in host script present");
        Require(!Directory.Exists(Path.Combine(root, "source", ".git")), "snapshot has no Git metadata");
        Console.WriteLine($"Pinned source snapshot verified: {root}");
    }
    catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    return;
}

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

if (args.Contains("--resource-framework-probe"))
{
    var root = Path.Combine(Path.GetTempPath(), "HorizonResourceFrameworkProbe-" + Guid.NewGuid().ToString("N"));
    var resources = Path.Combine(root, "source");
    Directory.CreateDirectory(Path.Combine(resources, "assets", "minecraft"));
    File.WriteAllText(Path.Combine(resources, "horizon-resources.json"), "{\"minecraft\":\"26.3\"}");
    var marker = Path.Combine(resources, "assets", "minecraft", "fixture.txt");
    File.WriteAllText(marker, "synthetic fixture");
    var first = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "one"));
    var second = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "two"));
    Require(first.Name == second.Name, "identical resources use the same framework identity");
    using (var zip = System.IO.Compression.ZipFile.OpenRead(first.Package))
    {
        var entry = zip.GetEntry("AppxManifest.xml") ?? throw new Exception("missing framework manifest");
        using var stream = entry.Open();
        var manifest = System.Xml.Linq.XDocument.Load(stream);
        var ns = manifest.Root!.Name.Namespace;
        var identity = manifest.Root.Element(ns + "Identity")!;
        Require(identity.Attribute("Name")!.Value == first.Name, "framework identity");
        Require(identity.Attribute("Publisher")!.Value == ResourceFrameworkPacker.Publisher, "framework publisher");
        Require(identity.Attribute("ProcessorArchitecture")!.Value == "neutral", "framework architecture");
        Require(manifest.Root.Element(ns + "Properties")!.Element(ns + "Framework")!.Value == "true", "framework property");
        Require(zip.GetEntry("resources/assets/minecraft/fixture.txt") is not null, "resource payload");
        Require(zip.GetEntry("AppxSignature.p7x") is null, "framework must be unsigned");
    }
    try { await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "one")); throw new Exception("existing output accepted"); }
    catch (IOException) { }
    try { await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(resources, "nested")); throw new Exception("nested output accepted"); }
    catch (InvalidDataException) { }
    Require(!Directory.Exists(Path.Combine(resources, "nested")), "selected resources left untouched");
    File.WriteAllText(marker, "changed synthetic fixture");
    var third = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "three"));
    Require(first.Name != third.Name, "different resources use different framework identities");
    File.WriteAllText(Path.Combine(resources, "horizon-resources.json"), "{\"minecraft\":\"1.20.4\"}");
    var otherVersion = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "other-version"));
    Require(otherVersion.Name.StartsWith("Horizon.Asset.1-20-4.", StringComparison.Ordinal)
        && otherVersion.Name.Length <= 50, "another Minecraft version has a valid identity");
    Console.WriteLine("Verified reusable unsigned resource frameworks using synthetic resources.");
    return;
}

if (args.Contains("--framework-probe"))
{
    try { _ = SyntheticFrameworkPacker.PackageName("../invalid"); throw new Exception("invalid version accepted"); }
    catch (ArgumentException) { }
    var root = Path.Combine(Path.GetTempPath(), "HorizonBuilderFrameworkProbe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    foreach (var (target, architecture) in new[] { (TargetProfile.DesktopX64, "x64"), (TargetProfile.DesktopX86, "x86"), (TargetProfile.WindowsMobile, "arm") })
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

var soundFixture = Path.Combine(Path.GetTempPath(), "HorizonBuilderSoundFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(soundFixture);
Require(LocalRecipe.SoundWarning(soundFixture) is not null, "missing sounds reported");
try { LocalRecipe.ValidateSoundPreparation(soundFixture, null); throw new Exception("missing Minecraft installation accepted"); }
catch (DirectoryNotFoundException) { }
var minecraftSounds = Path.Combine(soundFixture, "assets", "minecraft");
Directory.CreateDirectory(Path.Combine(minecraftSounds, "sounds"));
File.WriteAllText(Path.Combine(minecraftSounds, "sounds.json"), "{}");
Require(LocalRecipe.SoundWarning(soundFixture) is null, "sound presence recognized");
var stagedFixture = Path.Combine(Path.GetTempPath(), "HorizonBuilderStageFixture-" + Guid.NewGuid().ToString("N"));
LocalBuildRunner.StageResources(soundFixture, stagedFixture);
File.WriteAllText(Path.Combine(stagedFixture, "assets", "minecraft", "sounds.json"), "changed");
Require(File.ReadAllText(Path.Combine(minecraftSounds, "sounds.json")) == "{}", "staging preserves source bytes");
try { LocalBuildRunner.StageResources(soundFixture, stagedFixture); throw new Exception("existing staging accepted"); }
catch (IOException) { }
try { LocalBuildRunner.StageResources(soundFixture, Path.Combine(soundFixture, "nested-output")); throw new Exception("nested staging accepted"); }
catch (InvalidDataException) { }
try { LocalRecipe.ValidateSoundPreparation(soundFixture, soundFixture); throw new Exception("existing sounds accepted for preparation"); }
catch (InvalidDataException) { }


var selection = new BuildSelection(TargetProfile.WindowsMobile, true);
Require(selection.ValidatedPackageVersion == LocalRecipe.DefaultPackageVersion, "default package version");
Require(LocalRecipe.UpstreamNightlyCommit.StartsWith(LocalRecipe.UpstreamNightlyShortCommit, StringComparison.Ordinal), "nightly label matches source commit");
var publicationVersion = LocalRecipe.PackageVersionFor(DateTimeOffset.Parse("2026-11-03T09:30:50Z"));
Require(publicationVersion == "2026.1103.930.50", "nightly publication time maps to package version");
Require(new Version(LocalRecipe.DefaultPackageVersion) > new Version(publicationVersion), "port rebuild advances past upstream publication version");
Require(new Version(LocalRecipe.PackageVersionFor(DateTimeOffset.Parse("2026-11-03T09:30:51Z"))) > new Version(publicationVersion), "later nightly package versions sort later");
Require(LocalRecipe.PackageVersionFor(DateTimeOffset.Parse("2026-11-03T10:30:50+01:00")) == publicationVersion, "package version uses UTC");
Require(new Version(LocalRecipe.PackageVersionFor(DateTimeOffset.Parse("2026-11-01T00:00:00Z"))) > new Version(LocalRecipe.PackageVersionFor(DateTimeOffset.Parse("2026-10-31T23:59:59Z"))), "package versions sort across month boundaries");
var nightly = NightlyRelease.Parse("{\"tag_name\":\"nightly\",\"prerelease\":true,\"target_commitish\":\"f42ef42ef6ceccb3c6a20bea2481d87949a51c81\",\"published_at\":\"2026-11-03T09:30:50Z\"}");
Require(nightly.SourceCommit == LocalRecipe.UpstreamNightlyCommit && LocalRecipe.PackageVersionFor(nightly.PublishedAt) == publicationVersion, "nightly metadata preserves source and publication time");
try { _ = NightlyRelease.Parse("{\"tag_name\":\"release\",\"prerelease\":false}"); throw new Exception("non-nightly release accepted"); }
catch (InvalidDataException) { }
try { _ = NightlyRelease.Parse("{\"tag_name\":\"nightly\",\"prerelease\":true,\"target_commitish\":\"main\",\"published_at\":\"2026-11-03T09:30:50Z\"}"); throw new Exception("moving nightly source accepted"); }
catch (InvalidDataException) { }
Require(new BuildSelection(TargetProfile.WindowsMobile, true, "1.0.9.0").ValidatedPackageVersion == "1.0.9.0", "version above phone test");
var manifestTest = System.Xml.Linq.XDocument.Parse("<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10'><Identity Name='HorizonUWPPrototype' Publisher='CN=HorizonUWPPrototype' Version='1.0.1.0'/></Package>");
LocalRecipe.ConfigureManifest(manifestTest, new BuildSelection(TargetProfile.WindowsMobile, true, "1.0.9.0"));
Require(manifestTest.Root!.Elements().Single().Attribute("Version")!.Value == "1.0.9.0", "manifest receives selected version");
var wrongManifest = System.Xml.Linq.XDocument.Parse("<Package><Identity Name='other' Publisher='CN=HorizonUWPPrototype' Version='1.0.1.0'/></Package>");
try { LocalRecipe.ConfigureManifest(wrongManifest, selection); throw new Exception("wrong package identity accepted"); }
catch (InvalidDataException) { }
foreach (var bad in new[] { "1.0.1", "1.0.0.9", "1.0.65536.0", "1.0.-1.0", "1.0.01.0", "1.0.1.0 & echo bad" })
{
    try { _ = new BuildSelection(TargetProfile.WindowsMobile, true, bad).ValidatedPackageVersion; throw new Exception("invalid package version accepted"); }
    catch (ArgumentException) { }
}
var testHome = Path.Combine(Path.GetTempPath(), "horizon-builder-test-home");
Require(LocalBuildRunner.ResourcesContainWorkspace(testHome, Path.Combine(testHome, ".huwp", "b")), "resource ancestor rejected");
Require(!LocalBuildRunner.ResourcesContainWorkspace(Path.Combine(testHome, "resources"), Path.Combine(testHome, ".huwp", "b")), "separate resources accepted");
var recipe = LocalRecipe.Plan(selection);
Require(recipe.SourceCommit == LocalRecipe.SourceCommit && recipe.Target == "thumbv7a-uwp-windows-msvc", "pinned ARM recipe");
Require(recipe.Steps.Any(step => step.Name == "Prepare ARM32 overrides" && step.Arguments.Contains("--offline-arm32")), "ARM offline override selected");
var fetchStep = recipe.Steps.Single(step => step.Name == "Fetch Cargo dependencies");
Require(fetchStep.Arguments.Contains("fetch") && fetchStep.Arguments.Contains("thumbv7a-uwp-windows-msvc")
    && fetchStep.Arguments.Contains("x86_64-pc-windows-msvc")
    && fetchStep.Arguments.Contains(".local/arm32-windows-sys/patches-offline-arm32.toml"), "fetch uses target, host and overrides");
Require(recipe.Steps.Single(step => step.Name == "Build game bridge").Arguments.Contains("--offline"), "build uses fetched dependencies offline");
Require(recipe.Notes.Any(note => note.Contains("HTTPS")), "ARM HTTPS limitation disclosed");
Require(recipe.Steps.Single(step => step.Name == "Build game bridge").Arguments.Contains("native-d3d11,uwp-dx12-renderer,native-touch-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell"), "touch and controller build features");
var withDevice = new BuildSelection(TargetProfile.WindowsMobile, true, NativeDeviceVibration: true);
Require(withDevice.ModIds.Contains(BuildSelection.DeviceVibrationModId)
    && withDevice.Features.Contains("native-device-vibration-mod"), "device vibration is an optional native mod");
var prepareRecipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false, PrepareSounds: true));
Require(prepareRecipe.Steps.Single(step => step.Name == "Build resource preparer").Arguments.Contains("x86_64-pc-windows-msvc")
    && prepareRecipe.Steps.Any(step => step.Name == "Prepare sound assets"), "optional sound preparation uses the desktop host target");
var desktopRecipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false));
Require(desktopRecipe.Steps.All(step => !step.Arguments.Contains("--offline-arm32")), "desktop excludes ARM override");
Require(LocalBuildRunner.CargoJobCount(32, 32L * 1024 * 1024 * 1024) == 32, "parallelism uses every available processor");
Require(LocalBuildRunner.CargoJobCount(4, 64L * 1024 * 1024 * 1024) == 4, "parallelism uses available CPUs");
Require(LocalBuildRunner.CargoJobCount(32, 1) == 32, "parallelism does not impose a memory-based CPU cap");
var frameworkRecipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false, UseResourceFramework: true));
Require(frameworkRecipe.Steps.Any(step => step.Name == "Package reusable resources")
    && frameworkRecipe.Steps.Single(step => step.Name == "Create unsigned client package").Arguments.Contains("-ResourceFrameworkName"), "framework recipe includes dependency");
const string installedFramework = "Horizon.Asset.26-3.321aa1f73c8a1a627df3642f1dcee0";
var reuse = new BuildSelection(TargetProfile.WindowsMobile, false, ReuseResourceFramework: true, ExistingResourceFramework: installedFramework);
var reuseRecipe = LocalRecipe.Plan(reuse);
LocalRecipe.ValidateResourceSelection(reuse, null, null);
try { LocalRecipe.ValidateResourceSelection(new BuildSelection(TargetProfile.WindowsMobile, false), null, null); throw new Exception("missing embedded resources accepted"); }
catch (InvalidOperationException) { }
Require(reuseRecipe.Steps.All(step => step.Name != "Package reusable resources"), "reuse does not build another framework");
Require(LocalRecipe.ResourceArguments(reuse).SequenceEqual(["-ResourceFrameworkName", installedFramework]), "reuse packages the exact installed framework dependency");
Require(reuseRecipe.Steps.Single(step => step.Name == "Create unsigned client package").Arguments.Contains(installedFramework), "reuse recipe records the selected framework");
Require(LocalRecipe.ResourceArguments(new BuildSelection(TargetProfile.WindowsMobile, false), "prepared").SequenceEqual(["-Resources", "prepared"]), "embedded resources remain available");
Require(LocalRecipe.ResourceArguments(new BuildSelection(TargetProfile.WindowsMobile, false, UseResourceFramework: true), builtFramework: installedFramework).SequenceEqual(["-ResourceFrameworkName", installedFramework]), "new framework mode remains available");
foreach (var invalid in new[] { "", "Horizon.Asset.26-2.321aa1f73c8a1a627df3642f1dcee0", installedFramework + ";bad", installedFramework.ToUpperInvariant() })
{
    try { _ = LocalRecipe.Plan(reuse with { ExistingResourceFramework = invalid }); throw new Exception("invalid framework name accepted"); }
    catch (ArgumentException) { }
}
try { _ = LocalRecipe.Plan(reuse with { PrepareSounds = true }); throw new Exception("sound preparation accepted with existing framework"); }
catch (ArgumentException) { }
try { _ = LocalRecipe.Plan(reuse with { UseResourceFramework = true }); throw new Exception("conflicting framework modes accepted"); }
catch (ArgumentException) { }
var arm64Recipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.SurfaceProX, false));
Require(new BuildSelection(TargetProfile.WindowsMobile, false, Renderer: "angle").ValidatedRenderer == "angle", "ARM32 ANGLE can be requested when runtime DLLs are available");
Require(arm64Recipe.Target == "aarch64-uwp-windows-msvc"
    && arm64Recipe.Features.Contains("uwp-dx12-renderer"), "ARM64 recipe includes the tablet target and DX12 renderer");
var x86Recipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX86, false));
Require(x86Recipe.Target == "i686-uwp-windows-msvc" && x86Recipe.Features.Contains("uwp-dx12-renderer")
    && x86Recipe.Steps.Single(step => step.Name == "Strict-link host").Arguments.Contains("x86"), "x86 recipe includes the DX12 backend");
foreach (var target in Enum.GetValues<TargetProfile>())
{
    var dx12Selection = new BuildSelection(target, false, Renderer: "dx12");
    Require(dx12Selection.ValidatedRenderer == "dx12" && dx12Selection.Features.Contains("uwp-dx12-renderer"), $"DX12 available in the {target} build");
}
var missingToolchain = LocalRecipe.ToolchainBlockers(selection, new FakeProbe(), new RustToolchainFacts("rustc 1.98.0", false));
Require(missingToolchain.Any(blocker => blocker.Contains("1.99.1")), "rust version pinned");
Require(missingToolchain.Any(blocker => blocker.Contains("rust-src")), "rust-src checked");
var soundBlockers = LocalRecipe.ToolchainBlockers(new BuildSelection(TargetProfile.WindowsMobile, true, PrepareSounds: true), new FakeProbe(), new RustToolchainFacts("rustc 1.99.1 (test)", true));
Require(soundBlockers.Any(blocker => blocker.Contains("Sound preparation requires the x64")), "ARM sound preparation checks host compiler");
Require(WindowsPackageManager.Arguments(InstallableTool.Rustup).SequenceEqual(["install", "--id", "Rustlang.Rustup", "--exact", "--source", "winget", "--interactive", "--no-upgrade"]), "fixed interactive Rustup install");
Require(WindowsPackageManager.Arguments(InstallableTool.Git)[2] == "Git.Git", "fixed Git package");
Require(WindowsPackageManager.Arguments(InstallableTool.Python)[2] == "Python.Python.3.13", "fixed Python package");
try { _ = WindowsPackageManager.Arguments((InstallableTool)99); throw new Exception("invalid package accepted"); }
catch (ArgumentOutOfRangeException) { }
try { await WindowsPackageManager.InstallAsync(new FakeProbe(), InstallableTool.Rustup); throw new Exception("missing WinGet accepted"); }
catch (InvalidOperationException error) { Require(error.Message.Contains("not found"), "missing WinGet reported"); }
Require(new BuildSelection(TargetProfile.DesktopX64, false, MicrosoftClientId: " 01234567-89AB-CDEF-0123-456789ABCDEF ").ValidatedMicrosoftClientId == "01234567-89ab-cdef-0123-456789abcdef", "public client ID canonicalized");
try { _ = new BuildSelection(TargetProfile.DesktopX64, false, MicrosoftClientId: "not-an-id").ValidatedMicrosoftClientId; throw new Exception("invalid client ID accepted"); }
catch (ArgumentException) { }
Require(selection.RustTarget == "thumbv7a-uwp-windows-msvc", "mobile target");
Require(selection.Features.SequenceEqual(["native-d3d11", "uwp-dx12-renderer", "native-touch-mod", "uwp-audio", "uwp-https", "uwp-controller", "xaml-shell"]), "native touch feature selection");
Require(selection.ModIds.SequenceEqual(["horizon_touch"]), "stable native touch mod ID");
Require(new BuildSelection(TargetProfile.SurfaceProX, false).RustTarget == "aarch64-uwp-windows-msvc", "Pro X target");
Require(new BuildSelection(TargetProfile.DesktopX64, false).Features.SequenceEqual(["native-d3d11", "uwp-dx12-renderer", "uwp-audio", "uwp-https", "uwp-controller", "xaml-shell"]), "touch can be omitted");
Require(new BuildSelection(TargetProfile.DesktopX64, false).ModIds.Count == 0, "omitted mod ID");
try { _ = new BuildSelection((TargetProfile)99, false).RustTarget; throw new Exception("invalid target accepted"); }
catch (ArgumentOutOfRangeException) { }

string hash = new('a', 64), commit = new('b', 40);
var inputs = BuildInputs.Create(selection, commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.ModIds.SequenceEqual(["horizon_touch"]), "input record includes stable mod ID");
var repeated = BuildInputs.Create(selection, commit.ToUpperInvariant(), hash.ToUpperInvariant(), hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() == repeated.Sha256(), "canonical input hash");
var withClient = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, true, MicrosoftClientId: "01234567-89ab-cdef-0123-456789abcdef"), commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != withClient.Sha256(), "public application ID changes input hash");
var withoutTouch = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, false), commit, hash, hash, hash, hash, null, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != withoutTouch.Sha256(), "touch selection changes input hash");
var nextVersion = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, true, "1.0.11.0"), commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != nextVersion.Sha256(), "package version changes input hash");
var withSounds = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, true, PrepareSounds: true), commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != withSounds.Sha256(), "sound preparation changes input hash");
var withFramework = BuildInputs.Create(new BuildSelection(TargetProfile.WindowsMobile, true, UseResourceFramework: true), commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(inputs.Sha256() != withFramework.Sha256(), "framework selection changes input hash");
var reusedInputs = BuildInputs.Create(reuse, commit, hash, hash, hash, null, null, "stable-1.91.0", "14.44.35207", "10.0.15063.0");
Require(reusedInputs.ExistingResourceFramework == installedFramework && reusedInputs.ResourceInventorySha256 is null, "reuse records the framework identity without inventing a resource inventory");
Require(reusedInputs.Sha256() != withoutTouch.Sha256(), "reused framework changes input hash");
var withVibration = BuildInputs.Create(withDevice, commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0", hash);
Require(inputs.Sha256() != withVibration.Sha256(), "device vibration changes input hash");
try { _ = BuildInputs.Create(withDevice, commit, hash, hash, hash, hash, hash, "stable-1.91.0", "14.44.35207", "10.0.15063.0"); throw new Exception("missing vibration digest accepted"); }
catch (ArgumentException) { }
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
Require(missing.Single(c => c.Name == "Build recipe and signing").Detail.Contains("pinned local build recipe"), "available mobile recipe reported");
Require(new SetupChecker(new FakeProbe()).Scan(TargetProfile.SurfaceProX).Single(c => c.Name == "Build recipe and signing").Detail.Contains("ARM64 build recipe is available"), "available ARM64 recipe reported");

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
sealed class ConsoleProgress : IProgress<string>
{
    public void Report(string value) => Console.WriteLine(value);
}

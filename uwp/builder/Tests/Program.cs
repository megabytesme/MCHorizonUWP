using Horizon.Builder.Core;

if (args.Length == 1 && args[0] == "--native-index-probe")
{
    var nativeResults = await NativeModIndex.FetchAsync();
    Console.WriteLine($"Horizon registry: {nativeResults.Count} native projects list Minecraft {LocalRecipe.MinecraftVersion}; UWP compatibility was not checked.");
    return;
}

if (args.Length == 4 && args[0] == "--signing-proof")
{
    var identity = PackageSigning.GetOrCreateIdentity();
    Require(PackageSigning.GetOrCreateIdentity() == identity, "signing identity remains stable");
    var output = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(output);
    var stage = Path.Combine(output, "stage");
    Directory.CreateDirectory(Path.Combine(stage, "Assets"));
    await File.WriteAllBytesAsync(Path.Combine(stage, "Assets", "StoreLogo.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/aWQAAAAASUVORK5CYII="));
    System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    var manifest = new System.Xml.Linq.XDocument(new System.Xml.Linq.XDeclaration("1.0", "utf-8", null),
        new System.Xml.Linq.XElement(ns + "Package",
            new System.Xml.Linq.XElement(ns + "Identity", new System.Xml.Linq.XAttribute("Name", "MCHorizonUWP.SigningProof"),
                new System.Xml.Linq.XAttribute("Publisher", identity.Publisher), new System.Xml.Linq.XAttribute("Version", "1.0.0.0"), new System.Xml.Linq.XAttribute("ProcessorArchitecture", "neutral")),
            new System.Xml.Linq.XElement(ns + "Properties", new System.Xml.Linq.XElement(ns + "Framework", "true"),
                new System.Xml.Linq.XElement(ns + "DisplayName", "MCHorizonUWP signing proof"), new System.Xml.Linq.XElement(ns + "PublisherDisplayName", "MCHorizonUWP"),
                new System.Xml.Linq.XElement(ns + "Logo", "Assets/StoreLogo.png")),
            new System.Xml.Linq.XElement(ns + "Dependencies", new System.Xml.Linq.XElement(ns + "TargetDeviceFamily", new System.Xml.Linq.XAttribute("Name", "Windows.Universal"),
                new System.Xml.Linq.XAttribute("MinVersion", "10.0.15063.0"), new System.Xml.Linq.XAttribute("MaxVersionTested", "10.0.15063.0"))),
            new System.Xml.Linq.XElement(ns + "Resources", new System.Xml.Linq.XElement(ns + "Resource", new System.Xml.Linq.XAttribute("Language", "en-us")))));
    manifest.Save(Path.Combine(stage, "AppxManifest.xml"));
    var unsigned = Path.Combine(output, "unsigned.appx");
    var start = new System.Diagnostics.ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var item in new[] { "pack", "/d", stage, "/p", unsigned }) start.ArgumentList.Add(item);
    using (var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("MakeAppx did not start"))
    {
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "synthetic package created");
    }
    var signed = Path.Combine(output, "signed.appx");
    var certificate = Path.Combine(output, "public.cer");
    try
    {
        await PackageSigning.SignAsync(unsigned, signed, certificate, identity with { Publisher = "CN=Wrong" }, args[2]);
        throw new Exception("a mismatched signing identity was accepted");
    }
    catch (InvalidOperationException) { Require(!File.Exists(signed), "mismatched identity did not create a package"); }
    await PackageSigning.SignAsync(unsigned, signed, certificate, identity, args[2]);
    var secondPackage = Path.Combine(output, "second-signed.appx");
    await PackageSigning.SignAsync(unsigned, secondPackage, null, identity, args[2]);
    using (var second = System.IO.Compression.ZipFile.OpenRead(secondPackage)) Require(second.GetEntry("AppxSignature.p7x") is not null, "second package signed without a duplicate certificate export");
    using (var zip = System.IO.Compression.ZipFile.OpenRead(signed)) Require(zip.GetEntry("AppxSignature.p7x") is not null, "signed package contains signature");
    using (var publicCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate))
        Require(publicCert.Thumbprint == identity.Thumbprint && !publicCert.HasPrivateKey, "export contains only the public certificate");
    var set = await PackageOutput.CreateAsync(unsigned, unsigned, identity, args[2], Path.Combine(output, "signed-set"));
    Require(File.Exists(set.Client) && File.Exists(set.Certificate) && File.Exists(set.ResourceFramework) && File.Exists(set.InstallationGuide), "signed package set, public certificate and installation guide created");
    Require((await File.ReadAllTextAsync(set.InstallationGuide)).Contains("Windows 10 Mobile"), "installation guide covers phones");
    using (var frameworkZip = System.IO.Compression.ZipFile.OpenRead(set.ResourceFramework!))
        Require(frameworkZip.GetEntry("AppxSignature.p7x") is not null, "resource framework in set is signed");
    try { await PackageOutput.CreateAsync(unsigned, null, identity, args[2], Path.Combine(output, "signed-set")); throw new Exception("existing output accepted"); }
    catch (IOException) { }
    Console.WriteLine($"Synthetic package signed with {identity.Publisher}; public certificate exported.");
    return;
}

if (args.Length == 2 && args[0] == "--inspect-candidate-catalog")
{
    var candidate = CompatibilityCatalog.Parse(await File.ReadAllTextAsync(args[1]));
    foreach (var architecture in new[] { "x86", "x64", "arm", "arm64" })
    {
        var result = candidate.Check("0.1.0", "0.1.0", "0.1.0", architecture);
        Require(result.Allowed && !result.BuildVerified && !result.DeviceVerified, "candidate status is declared but unverified");
        Console.WriteLine($"{architecture}: compatible candidate, no build or device evidence");
    }
    return;
}

if (args.Length == 5 && args[0] == "--assemble-candidate")
{
    static string HashInput(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git was not found.");
    await SourceAssembler.AssembleAsync(args[1], HashInput(args[1]), args[2], HashInput(args[2]), args[3], HashInput(args[3]), args[4], git);
    Console.WriteLine("Candidate source inputs assembled; this does not authenticate them or build a package.");
    return;
}

Require(BuildSelection.DefaultTarget(System.Runtime.InteropServices.Architecture.X64) == TargetProfile.DesktopX64, "x64 default target");
Require(BuildSelection.DefaultTarget(System.Runtime.InteropServices.Architecture.X86) == TargetProfile.DesktopX86, "x86 default target");
Require(BuildSelection.DefaultTarget(System.Runtime.InteropServices.Architecture.Arm64) == TargetProfile.SurfaceProX, "ARM64 default target");
Require(BuildSelection.DefaultTarget(System.Runtime.InteropServices.Architecture.Arm) == TargetProfile.WindowsMobile, "ARM32 default target");
Require(Guid.TryParse(HorizonIdentity.ClientId, out _), "Horizon public client ID has the expected format");

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
    const string publisher = "CN=MCHorizonUWP-proof";
    var framework = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "framework-output"), publisher: publisher);
    var output = Path.Combine(root, "client-output");
    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var arg in new[] { "-NoProfile", "-File", Path.GetFullPath("uwp/host/package.ps1"), "-Architecture", "x64",
        "-Executable", Path.GetFullPath(args[1]), "-OutputDirectory", output, "-ResourceFrameworkName", framework.Name, "-Publisher", publisher })
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
    Require((string?)dependency.Attribute("Publisher") == publisher, "framework dependency publisher");
    Require((string?)manifest.Root.Element(ns + "Identity")!.Attribute("Publisher") == publisher, "client publisher");
    using var frameworkZip = System.IO.Compression.ZipFile.OpenRead(framework.Package);
    using var frameworkManifest = frameworkZip.GetEntry("AppxManifest.xml")!.Open();
    Require((string?)System.Xml.Linq.XDocument.Load(frameworkManifest).Root!.Element(ns + "Identity")!.Attribute("Publisher") == publisher, "framework publisher");
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


await TestSourceAssembler();
await TestReleaseSource();
var nativeFixture = $$$"""
{"schema":1,"projects":[
 {"id":"example","kind":"native-mod","name":"Example","summary":"A native mod","authors":["A"],"versions":[
  {"version":"2.0","game":"26.3","file":{"name":"example.hnmod","sha256":"{{{new string('a', 64)}}}"}},
  {"version":"1.0","game":"26.3","file":{"name":"example.hnmod","sha256":"{{{new string('b', 64)}}}"}}]},
 {"id":"revoked","kind":"native-mod","name":"Revoked","summary":"gone","authors":[],"versions":[
  {"version":"1.0","game":"26.3","file":{"name":"revoked.hnmod","sha256":"{{{new string('c', 64)}}}"}}]},
 {"id":"other","kind":"mod","name":"Not native","summary":"","versions":[]}],
 "revoked":[{"id":"example","sha256":"{{{new string('a', 64)}}}","reason":"taken down"},{"id":"revoked","reason":"taken down"}]}
""";
var nativeMods = NativeModIndex.Parse(System.Text.Encoding.UTF8.GetBytes(nativeFixture));
Require(nativeMods.Count == 1 && nativeMods[0].Id == "example" && nativeMods[0].Version == "1.0", "native index excludes revoked files, projects and other kinds");
try { _ = NativeModIndex.Parse(System.Text.Encoding.UTF8.GetBytes(nativeFixture.Replace("\"schema\":1", "\"schema\":2"))); throw new Exception("unsupported mod schema accepted"); }
catch (InvalidDataException) { }

var catalogJson = $$"""
{
  "schema_version": 1,
  "patches": [
    { "version": "1.4.0", "upstream_commit": "{{new string('a', 40)}}", "bridge": 3, "upstream": { "url": "https://example.com/source", "sha256": "{{new string('a', 64)}}" },
      "artifact": { "url": "https://example.com/patch", "sha256": "{{new string('b', 64)}}" } },
    { "version": "1.6.0", "upstream_commit": "{{new string('c', 40)}}", "bridge": 4, "upstream": { "url": "https://example.com/source2", "sha256": "{{new string('c', 64)}}" },
      "artifact": { "url": "https://example.com/next", "sha256": "{{new string('d', 64)}}" } }
  ],
  "clients": [
    { "version": "2.2.0", "bridge": 3, "artifact": { "url": "https://example.com/client", "sha256": "{{new string('e', 64)}}" } },
    { "version": "3.0.0", "bridge": 4, "artifact": { "url": "https://example.com/client3", "sha256": "{{new string('f', 64)}}" } }
  ],
  "rules": [
    { "patch": { "min_inclusive": "1.3.0", "max_exclusive": "1.6.0" },
      "client": { "min_inclusive": "2.1.0", "max_exclusive": "3.0.0" },
      "builder": { "min_inclusive": "1.2.0", "max_exclusive": "2.0.0" },
      "architectures": ["x64", "arm"] }
  ],
  "evidence": [
    { "patch": "1.4.0", "client": "2.2.0", "builder": "1.3.0", "architecture": "x64", "build_passed": true, "device_passed": false }
  ]
}
""";
var catalog = CompatibilityCatalog.Parse(catalogJson);
using (var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
{
    var payload = System.Text.Encoding.UTF8.GetBytes(catalogJson);
    var signature = signingKey.SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    var publicKey = signingKey.ExportSubjectPublicKeyInfo();
    Require(CatalogSignature.Verify(payload, signature, publicKey).Patches.Length == 2, "signed catalog accepted with trusted key");
    var content = System.Text.Encoding.UTF8.GetBytes("synthetic release artifact");
    using (var transport = new CatalogTransport(new CatalogFixtureHandler(payload, signature, content)))
    {
        var fetched = await transport.GetCatalogAsync(new Uri("https://example.test/catalog"), new Uri("https://example.test/signature"), publicKey);
        Require(fetched.Check("1.4.0", "2.2.0", "1.3.0", "x64").BuildVerified, "verified catalog retrieved over HTTPS");
        var folder = Path.Combine(Path.GetTempPath(), "horizon-catalog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var good = Path.Combine(folder, "artifact");
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
            await transport.DownloadArtifactAsync(new CatalogArtifact("https://example.test/artifact", digest), good);
            Require(File.ReadAllBytes(good).SequenceEqual(content), "downloaded artifact digest verified");
            var bad = Path.Combine(folder, "bad");
            try { await transport.DownloadArtifactAsync(new CatalogArtifact("https://example.test/artifact", new string('0', 64)), bad); throw new Exception("wrong download digest accepted"); }
            catch (InvalidDataException) { Require(!File.Exists(bad), "invalid download removed"); }
            try { await transport.DownloadArtifactAsync(new CatalogArtifact("https://example.test/insecure-redirect", digest), bad); throw new Exception("insecure redirect accepted"); }
            catch (InvalidDataException) { Require(!File.Exists(bad), "insecure redirect created no artifact"); }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
    payload[0] ^= 1;
    try { _ = CatalogSignature.Verify(payload, signature, publicKey); throw new Exception("tampered catalog accepted"); }
    catch (InvalidDataException) { }
}
var verified = catalog.Check("1.4.0", "2.2.0", "1.3.0", "x64");
Require(verified.Allowed && verified.BuildVerified && !verified.DeviceVerified, "exact build evidence does not imply a device test");
var unverified = catalog.Check("1.4.0", "2.2.0", "1.3.0", "arm");
Require(unverified.Allowed && !unverified.BuildVerified, "compatible ARM does not inherit x64 evidence");
Require(!catalog.Check("1.4.0", "2.2.0", "2.0.0", "x64").Allowed, "builder upper bound is exclusive");
Require(!catalog.Check("1.4.0", "3.0.0", "1.3.0", "x64").Allowed, "mismatched bridge rejected");
Require(!catalog.Check("1.4.0", "2.2.0", "1.3.0", "arm64").Allowed, "undeclared architecture rejected");
Require(!catalog.Check("9.0.0", "2.2.0", "1.3.0", "x64").Allowed, "unknown release rejected");
try { _ = CompatibilityCatalog.Parse(catalogJson.Replace("https://example.com/patch", "http://example.com/patch")); throw new Exception("insecure artifact accepted"); }
catch (InvalidDataException) { }
try { _ = CompatibilityCatalog.Parse(catalogJson.Replace("\"schema_version\": 1", "\"schema_version\": 2")); throw new Exception("unknown schema accepted"); }
catch (InvalidDataException) { }
try { _ = CompatibilityCatalog.Parse(catalogJson.Replace("\"build_passed\": true, \"device_passed\": false", "\"build_passed\": false, \"device_passed\": true")); throw new Exception("device evidence without a build accepted"); }
catch (InvalidDataException) { }
try { _ = CompatibilityCatalog.Parse(catalogJson.Replace("\"schema_version\": 1", "\"schema_version\": 1, \"schema_version\": 1")); throw new Exception("duplicate property accepted"); }
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
var signedManifest = System.Xml.Linq.XDocument.Parse("<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10'><Identity Name='HorizonUWPPrototype' Publisher='CN=HorizonUWPPrototype' Version='1.0.1.0'/></Package>");
LocalRecipe.ConfigureManifest(signedManifest, selection, new PackageSigningIdentity(new string('a', 40), "CN=MCHorizonUWP-user"));
Require((string?)signedManifest.Root!.Elements().Single().Attribute("Name") == "MCHorizonUWP", "production-style package identity selected");
Require((string?)signedManifest.Root.Elements().Single().Attribute("Publisher") == "CN=MCHorizonUWP-user", "package publisher follows the selected certificate");
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
Require(WindowsPackageManager.Arguments(InstallableTool.Git).Contains("--silent") && WindowsPackageManager.Arguments(InstallableTool.Git).Contains("--disable-interactivity"), "Git setup is noninteractive");
Require(WindowsPackageManager.Arguments(InstallableTool.Python).Contains("--silent") && WindowsPackageManager.Arguments(InstallableTool.Python).Contains("--disable-interactivity"), "Python setup is noninteractive");
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
static async Task TestReleaseSource()
{
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git is needed for the release test.");
    var root = Path.Combine(Path.GetTempPath(), "horizon-release-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var upstream = Path.Combine(root, "upstream.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(upstream, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("upstream-commit/main.txt").Open(), new System.Text.UTF8Encoding(false))) writer.Write("hello\n");
        var patch = System.Text.Encoding.UTF8.GetBytes("diff --git a/main.txt b/main.txt\n--- a/main.txt\n+++ b/main.txt\n@@ -1 +1 @@\n-hello\n+world\n");
        var client = Path.Combine(root, "client.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(client, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("uwp/host/file.txt").Open())) writer.Write("client");
        var inputs = new Dictionary<string, byte[]> { ["/source"] = File.ReadAllBytes(upstream), ["/patch"] = patch, ["/client"] = File.ReadAllBytes(client) };
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var json = $$$"""
        {"schema_version":1,"patches":[{"version":"1.0.0","upstream_commit":"{{{new string('a', 40)}}}","bridge":1,
        "upstream":{"url":"https://example.test/source","sha256":"{{{Hash(inputs["/source"])}}}"},"artifact":{"url":"https://example.test/patch","sha256":"{{{Hash(patch)}}}"}}],
        "clients":[{"version":"1.0.0","bridge":1,"artifact":{"url":"https://example.test/client","sha256":"{{{Hash(inputs["/client"])}}}"}}],
        "rules":[{"patch":{"min_inclusive":"1.0.0","max_exclusive":"2.0.0"},"client":{"min_inclusive":"1.0.0","max_exclusive":"2.0.0"},
        "builder":{"min_inclusive":"1.0.0","max_exclusive":"2.0.0"},"architectures":["x64"]}],"evidence":[]}
        """;
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        inputs["/catalog"] = System.Text.Encoding.UTF8.GetBytes(json);
        inputs["/signature"] = key.SignData(inputs["/catalog"], System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var transport = new CatalogTransport(new ReleaseFixtureHandler(inputs));
        var catalogUri = new Uri("https://example.test/catalog");
        var signatureUri = new Uri("https://example.test/signature");
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var destination = Path.Combine(root, "prepared");
        var prepared = await ReleaseSource.PrepareAsync(transport, catalogUri, signatureUri, publicKey, "1.0.0", "1.0.0", "1.0.0", "x64", destination, git);
        Require((await File.ReadAllTextAsync(Path.Combine(prepared.SourceDirectory, "main.txt"))).Replace("\r\n", "\n") == "world\n", "signed release patch applied");
        Require(File.Exists(Path.Combine(prepared.SourceDirectory, "uwp", "host", "file.txt")) && !prepared.Compatibility.BuildVerified, "client overlaid without implying build evidence");
        var incompatible = Path.Combine(root, "incompatible");
        try { await ReleaseSource.PrepareAsync(transport, catalogUri, signatureUri, publicKey, "1.0.0", "1.0.0", "1.0.0", "arm", incompatible, git); throw new Exception("incompatible release accepted"); }
        catch (InvalidOperationException) { Require(!Directory.Exists(incompatible), "incompatible release makes no workspace"); }
        inputs["/patch"] = System.Text.Encoding.UTF8.GetBytes("tampered");
        var corrupted = Path.Combine(root, "corrupted");
        try { await ReleaseSource.PrepareAsync(transport, catalogUri, signatureUri, publicKey, "1.0.0", "1.0.0", "1.0.0", "x64", corrupted, git); throw new Exception("corrupt artifact accepted"); }
        catch (InvalidDataException) { Require(!Directory.Exists(Path.Combine(corrupted, "source")), "corrupt artifact is never assembled"); }
    }
    finally { Directory.Delete(root, recursive: true); }
}
static async Task TestSourceAssembler()
{
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git is needed for the source assembly test.");
    var root = Path.Combine(Path.GetTempPath(), "horizon-assembly-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var upstream = Path.Combine(root, "upstream.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(upstream, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("upstream-commit/main.txt").Open(), new System.Text.UTF8Encoding(false))) writer.Write("hello\n");
        var patch = Path.Combine(root, "change.patch");
        await File.WriteAllTextAsync(patch, "diff --git a/main.txt b/main.txt\n--- a/main.txt\n+++ b/main.txt\n@@ -1 +1 @@\n-hello\n+world\n", new System.Text.UTF8Encoding(false));
        var client = Path.Combine(root, "client.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(client, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("uwp/host/file.txt").Open(), new System.Text.UTF8Encoding(false))) writer.Write("client");
        static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var output = Path.Combine(root, "source");
        await SourceAssembler.AssembleAsync(upstream, Hash(upstream), patch, Hash(patch), client, Hash(client), output, git);
        Require((await File.ReadAllTextAsync(Path.Combine(output, "main.txt"))).Replace("\r\n", "\n") == "world\n", "patch applied to upstream source");
        Require(await File.ReadAllTextAsync(Path.Combine(output, "uwp", "host", "file.txt")) == "client", "UWP client overlaid after patch");
        try { await SourceAssembler.AssembleAsync(upstream, new string('0', 64), patch, Hash(patch), client, Hash(client), Path.Combine(root, "bad-hash"), git); throw new Exception("incorrect source digest accepted"); }
        catch (InvalidDataException) { Require(!Directory.Exists(Path.Combine(root, "bad-hash")), "digest checked before creating workspace"); }
        var unsafeClient = Path.Combine(root, "unsafe.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(unsafeClient, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("uwp/../../outside.txt").Open())) writer.Write("unsafe");
        try { await SourceAssembler.AssembleAsync(upstream, Hash(upstream), patch, Hash(patch), unsafeClient, Hash(unsafeClient), Path.Combine(root, "bad-path"), git); throw new Exception("archive traversal accepted"); }
        catch (InvalidDataException) { Require(!File.Exists(Path.Combine(root, "outside.txt")), "archive traversal did not write outside the workspace"); }
    }
    finally { Directory.Delete(root, recursive: true); }
}
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
sealed class ReleaseFixtureHandler(Dictionary<string, byte[]> inputs) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(inputs.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
}
sealed class CatalogFixtureHandler(byte[] catalog, byte[] signature, byte[] artifact) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/insecure-redirect")
        {
            var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("http://example.test/artifact");
            return Task.FromResult(redirect);
        }
        var bytes = request.RequestUri.AbsolutePath switch { "/catalog" => catalog, "/signature" => signature, "/artifact" => artifact, _ => null };
        return Task.FromResult(bytes is null ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}

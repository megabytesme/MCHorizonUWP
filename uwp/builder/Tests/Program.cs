using Horizon.Builder.Core;

if (args.Length == 3 && args[0] == "--framework-fallback-proof")
{
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new framework proof workspace.");
    Directory.CreateDirectory(root);
    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    await WinUiBootstrap.EnsureAsync(profile, root, args[1], new ConsoleProgress());
    var systemSdkRoot = Environment.GetEnvironmentVariable("ProgramFiles(x86)")!;
    var signTool = Path.Combine(systemSdkRoot, "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe");
    var packages = await FrameworkDependencies.FindAsync(args[1], Path.Combine(root, "empty-sdk"), profile, signTool, root);
    Require(packages.Count == 2 && packages.All(File.Exists) && packages[0].StartsWith(root, StringComparison.OrdinalIgnoreCase),
        "Microsoft runtime downloaded, extracted and verified without an SDK runtime package");
    Console.WriteLine($"Verified Microsoft VC runtime fallback for {args[1]}: {packages[0]}");
    return;
}

if (args.Length == 5 && args[0] == "--prepare-assets-candidate")
{
    static string InputHash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var root = Path.GetFullPath(args[4]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new asset proof workspace.");
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git was not found.");
    Directory.CreateDirectory(root);
    var source = Path.Combine(root, "source");
    var upstream = new CatalogArtifact("https://example.test/source", InputHash(args[1]));
    var patch = new CatalogArtifact("https://example.test/patch", InputHash(args[2]));
    var client = new CatalogArtifact("https://example.test/client", InputHash(args[3]));
    await SourceAssembler.AssembleAsync(args[1], upstream.Sha256, args[2], patch.Sha256, args[3], client.Sha256, source, git);
    var release = new PreparedRelease(source, new CatalogPatch("0.1.0", LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient("0.1.1", 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
    var resources = await MinecraftAssetPreparation.PrepareAsync(release, new BuildSelection(TargetProfile.DesktopX64, false), true,
        new SystemProbe(), new ConsoleProgress());
    Require(File.Exists(Path.Combine(resources, "horizon-resources.json")), "automatic resource preparation produced its manifest");
    Console.WriteLine($"Verified integrated asset preparation: {resources}");
    return;
}

if (args.Length == 2 && args[0] == "--stage-asset-helper")
{
    var workspace = Path.GetFullPath(args[1]);
    var source = Path.Combine(workspace, "source");
    var staged = await MinecraftAssetPreparation.StageHelperAsync(source, workspace);
    Require(File.Exists(Path.Combine(staged, "Cargo.lock")) && File.Exists(Path.Combine(staged, "src", "mojang.rs")), "asset helper and pinned Horizon downloader staged");
    try { await MinecraftAssetPreparation.StageHelperAsync(source, workspace); throw new Exception("existing asset helper accepted"); }
    catch (IOException) { }
    Console.WriteLine($"Asset helper staged: {staged}");
    return;
}

if (args.Length == 1 && args[0] == "--install-clang-proof")
{
    SystemProbe.RefreshProcessPath();
    var probe = new SystemProbe();
    await WindowsPackageManager.EnsureClangAsync(probe, TargetProfile.WindowsMobile,
        tool => WindowsPackageManager.InstallAsync(probe, tool), new ConsoleProgress());
    Require(probe.FileExists(Path.Combine(probe.EnvironmentValue("ProgramFiles")!, "LLVM", "bin", "clang.exe")), "ARM Clang available after setup");
    Console.WriteLine("Verified automatic ARM Clang setup.");
    return;
}

if (args.Length == 7 && args[0] == "--create-local-install-proof")
{
    var root = Path.GetFullPath(args[4]);
    if (Directory.Exists(root)) throw new IOException("Choose a new proof workspace.");
    var stage = Path.Combine(root, "stage");
    Directory.CreateDirectory(stage);
    using (var source = System.IO.Compression.ZipFile.OpenRead(args[1]))
        foreach (var entry in source.Entries)
        {
            if (entry.FullName is "AppxBlockMap.xml" or "[Content_Types].xml" or "AppxSignature.p7x"
                || entry.FullName.StartsWith("AppxMetadata/", StringComparison.Ordinal)) continue;
            var path = Path.Combine(stage, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, path);
        }
    var manifestPath = Path.Combine(stage, "AppxManifest.xml");
    var manifest = System.Xml.Linq.XDocument.Load(manifestPath);
    var ns = manifest.Root!.Name.Namespace;
    var identity = PackageSigning.GetOrCreateIdentity();
    var identityElement = manifest.Root.Element(ns + "Identity")!;
    identityElement.SetAttributeValue("Name", "MCHorizonUWP.LocalInstallProof");
    identityElement.SetAttributeValue("Publisher", identity.Publisher);
    foreach (var dependency in manifest.Root.Element(ns + "Dependencies")!.Elements(ns + "PackageDependency")
        .Where(element => ((string?)element.Attribute("Name"))?.StartsWith("Horizon.Asset.", StringComparison.Ordinal) == true).ToArray()) dependency.Remove();
    manifest.Save(manifestPath);
    var unsigned = Path.Combine(root, "unsigned.appx");
    await LocalBuildRunner.RunProcess(args[2], root, ["pack", "/d", stage, "/p", unsigned]);
    var output = await PackageOutput.CreateAsync(unsigned, null, identity, args[3], Path.Combine(root, "signed-set"), [args[5], args[6]]);
    Require(File.Exists(output.Client) && File.Exists(output.Certificate), "local installation fixture signed");
    Console.WriteLine("Created a signed synthetic installation fixture.");
    return;
}

if (args.Length == 2 && args[0] == "--install-local-proof")
{
    var directory = Path.GetFullPath(args[1]);
    var dependencies = Directory.GetFiles(Path.Combine(directory, "Dependencies"), "*.appx");
    var output = new SignedPackageOutput(Path.Combine(directory, "HorizonUWP.appx"), Path.Combine(directory, "HorizonUWP.cer"), null,
        Path.Combine(directory, "Install.md"), dependencies);
    await LocalPackageInstaller.InstallAsync(output, new Progress<string>(Console.WriteLine));
    Console.WriteLine("Installed the signed synthetic package and its dependencies for the current user.");
    return;
}

if (args.Length == 3 && args[0] == "--bootstrap-xaml")
{
    var profile = Path.GetFullPath(args[1]);
    var workspace = Path.GetFullPath(args[2]);
    if (Directory.Exists(profile) || Directory.Exists(workspace)) throw new IOException("Choose new proof directories.");
    Directory.CreateDirectory(profile);
    Directory.CreateDirectory(workspace);
    await WinUiBootstrap.EnsureAsync(profile, workspace, "x64", new Progress<string>(Console.WriteLine));
    await WinUiBootstrap.EnsureAsync(profile, workspace, "x64");
    Require(File.Exists(Path.Combine(profile, ".nuget", "packages", "microsoft.ui.xaml", "2.7.3", "lib", "uap10.0", "Microsoft.UI.Xaml.winmd")), "WinUI headers restored");
    Require(File.Exists(Path.Combine(profile, ".nuget", "packages", "microsoft.ui.xaml", "2.7.3", "tools", "AppX", "x64", "Release", "Microsoft.UI.Xaml.2.7.appx")), "WinUI target framework restored");
    Console.WriteLine("Restored the pinned WinUI package and reused the complete cache.");
    return;
}

if (args.Length == 2 && args[0] == "--prepare-published-release")
{
    SystemProbe.RefreshProcessPath();
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git was not found.");
    using var transport = new CatalogTransport();
    var prepared = await ReleaseSource.PrepareAsync(transport, CatalogTrust.CatalogUri, CatalogTrust.SignatureUri,
        CatalogTrust.PublicKey(), "0.1.0", "0.1.0", CatalogTrust.BuilderVersion, "x64", Path.GetFullPath(args[1]), git);
    Require(File.Exists(Path.Combine(prepared.SourceDirectory, "uwp", "host", "build-game.cmd")), "published client source assembled");
    Require(File.Exists(Path.Combine(prepared.SourceDirectory, "Cargo.toml")), "published upstream source assembled");
    Console.WriteLine("Verified the public catalog and assembled its published x64 source release.");
    return;
}

if (args.Length == 2 && args[0] == "--check-frameworks")
{
    var programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? throw new Exception("Windows SDK directory not found.");
    var signtool = Path.Combine(programFiles, "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe");
    var packages = await FrameworkDependencies.FindAsync(args[1], programFiles,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), signtool, Path.GetTempPath());
    Require(packages.Count == 2 && packages.All(File.Exists), "Microsoft framework packages verified");
    Console.WriteLine($"Verified Microsoft UI and VC runtime packages for {args[1]}.");
    return;
}

if (args.Length == 3 && args[0] == "--apply-native-mod")
{
    var workspace = Path.GetFullPath(args[2]);
    if (Directory.Exists(workspace) || File.Exists(workspace)) throw new IOException("Choose a new test workspace.");
    Directory.CreateDirectory(workspace);
    var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.OrdinalIgnoreCase);
    var cargo = new SystemProbe().FindOnPath("cargo.exe") ?? throw new Exception("Cargo was not found.");
    var staged = await NativeModBuild.ApplyAsync(Path.GetFullPath(args[1]), workspace, ["zoom"], cargo, environment,
        new Progress<string>(Console.WriteLine));
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "game.rs")).Contains("official_zoom"), "registry mod applied to the game");
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "lib.rs")).Contains("#![recursion_limit = \"256\"]"), "crate recursion setting restored");
    Console.WriteLine("Verified Horizon registry Zoom mod applied to the UWP source.");
    return;
}

if (args.Length == 3 && args[0] == "--verify-local-catalog")
{
    var candidateCatalog = CatalogSignature.Verify(await File.ReadAllBytesAsync(args[1]), await File.ReadAllBytesAsync(args[2]), CatalogTrust.PublicKey());
    Require(candidateCatalog.Check("0.1.0", "0.1.0", CatalogTrust.BuilderVersion, "x64").Allowed, "signed candidate catalog is readable");
    Console.WriteLine("The embedded key verified the candidate catalog.");
    return;
}

if (args.Length == 1 && args[0] == "--auth-mock-only")
{
    await TestMinecraftSignIn();
    Console.WriteLine("Mock Microsoft and Minecraft ownership checks passed.");
    return;
}

if (args.Length == 3 && args[0] == "--stage-native-mod")
{
    if (Directory.Exists(args[2]) || File.Exists(args[2])) throw new IOException("Choose a new test workspace.");
    Directory.CreateDirectory(args[2]);
    var artifact = NativeBundleStager.LauncherFor(System.Runtime.InteropServices.Architecture.X64);
    var stager = NativeBundleStager.OpenVerifiedArchive(args[1], args[2], artifact.Sha256);
    var bundles = await stager.InstallAsync("zoom");
    Require(bundles.Any(bundle => bundle.Id == "zoom" && File.Exists(bundle.Path)), "requested native bundle staged");
    Console.WriteLine($"Staged {bundles.Count} native bundle(s) using Horizon's registry checks; not applied to a UWP build.");
    return;
}

if (args.Length == 1 && args[0] == "--check-python-path")
{
    SystemProbe.RefreshProcessPath();
    var python = new SystemProbe().FindOnPath("python.exe") ?? throw new Exception("Python was not found.");
    var start = new System.Diagnostics.ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
    start.ArgumentList.Add("--version");
    using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Python did not start.");
    var version = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    Require(process.ExitCode == 0 && version.StartsWith("Python 3.", StringComparison.Ordinal), "detected Python runs successfully");
    Console.WriteLine("Detected WinGet-installed Python successfully.");
    return;
}

if (args.Length == 1 && args[0] == "--native-index-probe")
{
    var nativeResults = await NativeModIndex.FetchAsync();
    Console.WriteLine($"Horizon registry: {nativeResults.Count} native projects list Minecraft {LocalRecipe.MinecraftVersion}; UWP compatibility was not checked.");
    return;
}

if ((args.Length == 4 || args.Length == 5) && args[0] == "--build-unified-candidate")
{
    static string InputHash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git is needed for the candidate build.");
    var root = Path.Combine(Path.GetTempPath(), "horizon-unified-recipe-proof-" + Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    Directory.CreateDirectory(root);
    var upstream = new CatalogArtifact("https://example.test/source", InputHash(args[1]));
    var patch = new CatalogArtifact("https://example.test/patch", InputHash(args[2]));
    var client = new CatalogArtifact("https://example.test/client", InputHash(args[3]));
    await SourceAssembler.AssembleAsync(args[1], upstream.Sha256, args[2], patch.Sha256, args[3], client.Sha256, source, git);
    var prepared = new PreparedRelease(source, new CatalogPatch("0.1.0", LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient("0.1.1", 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
    var hasResources = args.Length == 5;
    var candidateSelection = new BuildSelection(TargetProfile.DesktopX64, true, UseResourceFramework: hasResources,
        NativeDeviceVibration: true, MicrosoftClientId: HorizonIdentity.ClientId, ReuseResourceFramework: !hasResources,
        ExistingResourceFramework: hasResources ? "" : "Horizon.Asset.26-3.2d21cce5593c8e6e7f3fa20b5f9f9f");
    var result = await LocalBuildRunner.RunPreparedAsync(prepared, hasResources ? args[4] : null, candidateSelection, new SystemProbe(), new ConsoleProgress());
    Require(result.SignedOutput is { } signed && File.Exists(signed.Client) && signed.Dependencies is { Count: 2 }, "unified recipe produced a signed client with frameworks");
    Require(!hasResources || File.Exists(result.SignedOutput!.ResourceFramework), "prepared resources produced a signed framework");
    Console.WriteLine($"Unified Build Tools candidate: {result.SignedOutput!.Client}");
    return;
}

if (args.Length == 5 && args[0] == "--build-split-candidate")
{
    // Development proof: the files are hashed locally, not authenticated as releases.
    static string InputHash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git is needed for the candidate build.");
    var root = Path.Combine(Path.GetTempPath(), "horizon-split-recipe-proof-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var source = Path.Combine(root, "source");
    var upstream = new CatalogArtifact("https://example.test/source", InputHash(args[1]));
    var patch = new CatalogArtifact("https://example.test/patch", InputHash(args[2]));
    var client = new CatalogArtifact("https://example.test/client", InputHash(args[3]));
    await SourceAssembler.AssembleAsync(args[1], upstream.Sha256, args[2], patch.Sha256, args[3], client.Sha256, source, git);
    var prepared = new PreparedRelease(source, new CatalogPatch("0.1.0", LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient("0.1.0", 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
    var result = await LocalBuildRunner.RunPreparedAsync(prepared, args[4], new BuildSelection(TargetProfile.DesktopX64, true,
        UseResourceFramework: true, NativeDeviceVibration: true, MicrosoftClientId: HorizonIdentity.ClientId), new SystemProbe(), new ConsoleProgress());
    var signed = result.SignedOutput ?? throw new Exception("The candidate package was not signed.");
    Require(File.Exists(signed.Client) && File.Exists(signed.Certificate) && File.Exists(signed.ResourceFramework), "signed candidate package set produced");
    Console.WriteLine($"Signed x64 candidate package set: {signed.Client}");
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
    var microsoftFrameworks = await FrameworkDependencies.FindAsync("x86", Environment.GetEnvironmentVariable("ProgramFiles(x86)")!,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), args[2], output);
    var set = await PackageOutput.CreateAsync(unsigned, unsigned, identity, args[2], Path.Combine(output, "signed-set"), microsoftFrameworks);
    Require(File.Exists(set.Client) && File.Exists(set.Certificate) && File.Exists(set.ResourceFramework) && File.Exists(set.InstallationGuide), "signed package set, public certificate and installation guide created");
    Require(set.Dependencies is { Count: 2 } && set.Dependencies.All(File.Exists), "Microsoft framework packages included");
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
using (var catalogKey = System.Security.Cryptography.ECDsa.Create())
{
    catalogKey.ImportSubjectPublicKeyInfo(CatalogTrust.PublicKey(), out var consumed);
    Require(consumed == CatalogTrust.PublicKey().Length && catalogKey.KeySize == 256, "embedded catalog verification key is P-256");
}
var fakeLocal = Path.Combine(Path.GetTempPath(), "fake-user");
Require(SystemProbe.IsPythonExecutionAlias("python.exe", Path.Combine(fakeLocal, "Microsoft", "WindowsApps", "python.exe"), fakeLocal, FileAttributes.ReparsePoint), "Python Store execution alias skipped");
Require(!SystemProbe.IsPythonExecutionAlias("python.exe", Path.Combine(fakeLocal, "Programs", "Python", "python.exe"), fakeLocal, FileAttributes.Normal), "real Python executable retained");

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
    var longRelative = Path.Combine("assets", "minecraft", new string('a', 100), new string('b', 100), "fixture.txt");
    var longFile = Path.Combine(resources, longRelative);
    Directory.CreateDirectory(Path.GetDirectoryName(longFile)!);
    File.WriteAllText(longFile, "long-path fixture");
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
        Require(zip.GetEntry("resources/" + longRelative.Replace('\\', '/')) is not null, "resource payload beyond the legacy Windows path limit");
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
await TestNativeBundleStager();
await TestMinecraftSignIn();
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
            using (var retryTransport = new CatalogTransport(new InterruptedDownloadHandler(content)))
            {
                var retry = Path.Combine(folder, "retry");
                await retryTransport.DownloadArtifactAsync(new CatalogArtifact("https://example.test/artifact", digest), retry);
                Require(File.ReadAllBytes(retry).SequenceEqual(content), "interrupted artifact download retried and verified");
            }
            using (var stalledTransport = new CatalogTransport(new StalledDownloadHandler(content), TimeSpan.FromMilliseconds(50)))
            {
                var retry = Path.Combine(folder, "stalled");
                await stalledTransport.DownloadArtifactAsync(new CatalogArtifact("https://example.test/artifact", digest), retry);
                Require(File.ReadAllBytes(retry).SequenceEqual(content), "stalled artifact download timed out, retried and verified");
            }
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
Require(LocalBuildRunner.CargoJobCount(32, 32L * 1024 * 1024 * 1024) == 15, "parallelism leaves memory for the OS");
Require(LocalBuildRunner.CargoJobCount(4, 64L * 1024 * 1024 * 1024) == 4, "parallelism uses available CPUs");
Require(LocalBuildRunner.CargoJobCount(32, 8L * 1024 * 1024 * 1024) == 3, "an 8 GiB Sandbox uses a memory-aware number of jobs");
Require(LocalBuildRunner.CargoJobCount(32, 1) == 1, "parallelism remains positive when memory is scarce");
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
Require(arm64Recipe.Target == "aarch64-uwp-windows-msvc"
    && arm64Recipe.Features.Contains("uwp-dx12-renderer"), "ARM64 recipe includes the tablet target and DX12 renderer");
var x86Recipe = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX86, false));
Require(x86Recipe.Target == "i686-uwp-windows-msvc" && x86Recipe.Features.Contains("uwp-dx12-renderer")
    && x86Recipe.Steps.Single(step => step.Name == "Strict-link host").Arguments.Contains("x86"), "x86 recipe includes the DX12 backend");
foreach (var target in Enum.GetValues<TargetProfile>())
{
    var targetSelection = new BuildSelection(target, false);
    Require(targetSelection.Features.Contains("uwp-dx12-renderer") && targetSelection.Features.Contains("native-d3d11"), $"both packaged renderers available in the {target} build");
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
Require(WindowsPackageManager.Arguments(InstallableTool.Llvm).Contains("23.1.4"), "ARM Clang installation uses the tested release");
var autoProbe = new FakeProbe();
var autoInstalled = new List<InstallableTool>();
Task InstallFake(InstallableTool tool)
{
    autoInstalled.Add(tool);
    autoProbe.Executables.Add(tool == InstallableTool.Git ? "git.exe" : "python.exe");
    return Task.CompletedTask;
}
await WindowsPackageManager.EnsureBuildBasicsAsync(autoProbe, InstallFake, () => { });
await WindowsPackageManager.EnsureBuildBasicsAsync(autoProbe, InstallFake, () => { });
Require(autoInstalled.SequenceEqual([InstallableTool.Git, InstallableTool.Python]), "missing Git and Python installed once and then reused");
Task InstallFakeClang(InstallableTool tool)
{
    autoInstalled.Add(tool);
    autoProbe.Files.Add(Path.Combine(autoProbe.Root, "LLVM", "bin", "clang.exe"));
    return Task.CompletedTask;
}
await WindowsPackageManager.EnsureClangAsync(autoProbe, TargetProfile.DesktopX64, InstallFakeClang);
await WindowsPackageManager.EnsureClangAsync(autoProbe, TargetProfile.WindowsMobile, InstallFakeClang);
await WindowsPackageManager.EnsureClangAsync(autoProbe, TargetProfile.SurfaceProX, InstallFakeClang);
Require(autoInstalled.SequenceEqual([InstallableTool.Git, InstallableTool.Python, InstallableTool.Llvm]), "Clang installed once only for ARM targets");
try
{
    await WindowsPackageManager.EnsureBuildBasicsAsync(new FakeProbe(), _ => Task.CompletedTask, () => { });
    throw new Exception("missing installed tool was accepted");
}
catch (InvalidOperationException) { }
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
var unverifiedOwnershipBlocked = false;
try { await MinecraftAssetPreparation.PrepareAsync(null!, new BuildSelection(TargetProfile.DesktopX64, false), false, new FakeProbe()); }
catch (InvalidOperationException) { unverifiedOwnershipBlocked = true; }
Require(unverifiedOwnershipBlocked, "asset downloads require confirmed Minecraft ownership");
var unifiedProbe = new FakeProbe();
var vsRoot = Path.Combine(unifiedProbe.Root, "Microsoft Visual Studio", "2022", "BuildTools");
var toolsets = Path.Combine(vsRoot, "VC", "Tools", "MSVC");
var fakeToolset = Path.Combine(toolsets, "14.44.35207");
unifiedProbe.Files.Add(Path.Combine(vsRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat"));
unifiedProbe.AddDirectory(toolsets, fakeToolset);
foreach (var arch in new[] { "x64", "arm64" })
{
    unifiedProbe.Files.Add(Path.Combine(fakeToolset, "bin", "Hostx64", arch, "cl.exe"));
    unifiedProbe.Files.Add(Path.Combine(fakeToolset, "lib", arch, "store", "vcruntime.lib"));
}
Require(VisualCppInstallation.FindForTarget(unifiedProbe, TargetProfile.SurfaceProX) == vsRoot, "one Build Tools installation supplies host and target compilers");
Require(VisualCppInstallation.FindForTarget(unifiedProbe, TargetProfile.WindowsMobile) is null, "missing ARM32 tools are not accepted");
Require(LocalPackageInstaller.CanInstall(TargetProfile.DesktopX86, System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 19045)), "x86 can install on x64 Windows");
Require(!LocalPackageInstaller.CanInstall(TargetProfile.WindowsMobile, System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 19045)), "ARM32 is not offered on x64 Windows");
Require(!LocalPackageInstaller.CanInstall(TargetProfile.DesktopX64, System.Runtime.InteropServices.Architecture.Arm64, new Version(10, 0, 19045)), "Windows 10 ARM64 does not offer x64 installation");
Require(LocalPackageInstaller.CanInstall(TargetProfile.DesktopX64, System.Runtime.InteropServices.Architecture.Arm64, new Version(10, 0, 22621)), "Windows 11 ARM64 can offer x64 installation");
Require(missing.Single(c => c.Name == "Git").Status == CheckStatus.Missing, "missing Git");
Require(missing.Single(c => c.Name == "Git").Instructions?.Host == "git-scm.com", "official Git instructions");
Require(missing.Single(c => c.Name == "Windows SDK and packaging tools").Instructions?.Host == "learn.microsoft.com", "official SDK instructions");
Require(missing.Single(c => c.Name == "Windows SDK and packaging tools").Detail.Contains("15063"), "legacy mobile SDK required");
Require(new SetupChecker(new FakeProbe()).Scan(TargetProfile.WindowsMobile, splitRelease: true).Single(c => c.Name == "Windows SDK and packaging tools").Detail.Contains("17763"), "split-release mobile SDK required");
Require(missing.Any(c => c.Status == CheckStatus.Review && c.Name == "Rust UWP standard library"), "unverified UWP std");
Require(missing.Single(c => c.Name == "Build recipe and signing").Detail.Contains("pinned local build recipe"), "available mobile recipe reported");
Require(new SetupChecker(new FakeProbe()).Scan(TargetProfile.SurfaceProX).Single(c => c.Name == "Build recipe and signing").Detail.Contains("ARM64 build recipe is available"), "available ARM64 recipe reported");

var modernOnly = new FakeProbe();
var modernKits = Path.Combine(modernOnly.Root, "Windows Kits", "10");
var modernSdk = Path.Combine(modernKits, "bin", "10.0.26100.0");
modernOnly.AddDirectory(Path.Combine(modernKits, "bin"), modernSdk);
modernOnly.Files.UnionWith([Path.Combine(modernSdk, "x64", "makeappx.exe"), Path.Combine(modernSdk, "x64", "signtool.exe"), Path.Combine(modernKits, "Include", "10.0.26100.0", "um", "Windows.h")]);
Require(new SetupChecker(modernOnly).Scan(TargetProfile.WindowsMobile).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Missing, "modern SDK cannot substitute for the legacy SDK");
Require(new SetupChecker(modernOnly).Scan(TargetProfile.DesktopX64, splitRelease: true).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Missing, "packaging SDK alone does not satisfy the host SDK");
var splitProbe = new FakeProbe();
var splitKits = Path.Combine(splitProbe.Root, "Windows Kits", "10");
var splitSdk = Path.Combine(splitKits, "bin", "10.0.17763.0");
splitProbe.AddDirectory(Path.Combine(splitKits, "bin"), splitSdk);
splitProbe.Files.UnionWith([Path.Combine(splitSdk, "x64", "makeappx.exe"), Path.Combine(splitSdk, "x64", "signtool.exe"),
    Path.Combine(splitSdk, "x64", "fxc.exe"), Path.Combine(splitKits, "Include", "10.0.17763.0", "um", "Windows.h"),
    Path.Combine(splitKits, "bin", "10.0.26100.0", "x64", "makeappx.exe"), Path.Combine(splitKits, "bin", "10.0.26100.0", "x64", "signtool.exe"),
    Path.Combine(splitProbe.Root, "Microsoft Visual Studio", "2022", "BuildTools", "VC", "Auxiliary", "Build", "vcvarsall.bat"),
    Path.Combine(splitProbe.Root, "LLVM", "bin", "clang.exe")]);
splitProbe.Executables.UnionWith(["git.exe", "rustup.exe", "cargo.exe", "python.exe"]);
Require(new SetupChecker(splitProbe).Scan(TargetProfile.WindowsMobile, splitRelease: true).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Found, "split ARM SDK detected");
Require(LocalRecipe.ToolchainBlockers(selection, splitProbe, new RustToolchainFacts("rustc 1.99.1 (test)", true), splitRelease: true).Count == 0, "split ARM recipe accepts the newer toolchain");
Require(LocalRecipe.ToolchainBlockers(selection, splitProbe, new RustToolchainFacts("rustc 1.99.1 (test)", true)).Count > 0, "legacy source still requires its original toolchain");

var fake = new FakeProbe();
fake.Executables.Add("git.exe");
var kits = Path.Combine(fake.Root, "Windows Kits", "10");
var sdk = Path.Combine(kits, "bin", "10.0.15063.0");
fake.AddDirectory(Path.Combine(kits, "bin"), sdk);
var packageSdk = Path.Combine(kits, "bin", "10.0.26100.0", "x64");
fake.Files.UnionWith([Path.Combine(sdk, "x64", "fxc.exe"), Path.Combine(packageSdk, "makeappx.exe"), Path.Combine(packageSdk, "signtool.exe"),
    Path.Combine(kits, "Include", "10.0.15063.0", "um", "Windows.h"), Path.Combine(kits, "Include", "10.0.16299.0", "um", "Windows.h")]);
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
fake.Files.Remove(Path.Combine(packageSdk, "signtool.exe"));
Require(new SetupChecker(fake).Scan(TargetProfile.WindowsMobile).Single(c => c.Name == "Windows SDK and packaging tools").Status == CheckStatus.Missing, "incomplete SDK rejected");
Console.WriteLine("Setup checker tests passed.");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task TestMinecraftSignIn()
{
    using (var signIn = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: true, fallback: false)))
    {
        var challenge = await signIn.BeginAsync();
        Require(challenge.UserCode == "ABCD-EFGH" && challenge.VerificationUri.Host == "www.microsoft.com", "device sign-in instructions returned");
        Require(!challenge.ToString().Contains("device-secret", StringComparison.Ordinal), "device secret is not displayed");
        Require(await signIn.CompleteOwnershipCheckAsync(challenge), "Microsoft, Xbox and Minecraft ownership verified");
    }
    using (var signIn = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: false, fallback: true)))
    {
        var challenge = await signIn.BeginAsync();
        Require(!await signIn.CompleteOwnershipCheckAsync(challenge), "an account without Minecraft is not treated as an owner");
    }
}
static async Task TestNativeBundleStager()
{
    var root = Path.Combine(Path.GetTempPath(), "horizon-native-stager-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var archive = Path.Combine(root, "launcher.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("horizon-launcher.exe").Open())) writer.Write("synthetic executable");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        try { NativeBundleStager.OpenVerifiedArchive(archive, output, new string('0', 64)); throw new Exception("unverified launcher accepted"); }
        catch (InvalidDataException) { Require(!File.Exists(Path.Combine(output, "horizon-launcher.exe")), "invalid launcher was not extracted"); }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        var stager = NativeBundleStager.OpenVerifiedArchive(archive, output, hash);
        try { await stager.InstallAsync("../escape"); throw new Exception("invalid mod ID accepted"); }
        catch (ArgumentException) { }
        try { NativeBundleStager.LauncherFor(System.Runtime.InteropServices.Architecture.X86); throw new Exception("unsupported launcher architecture accepted"); }
        catch (PlatformNotSupportedException) { }
    }
    finally { Directory.Delete(root, recursive: true); }
}
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
        var recipe = new PreparedRelease(prepared.SourceDirectory, prepared.Patch with { Version = "0.1.0", UpstreamCommit = LocalRecipe.UpstreamNightlyCommit },
            prepared.Client with { Version = "0.1.0" }, prepared.Compatibility, CatalogTrust.BuilderVersion, "x64");
        LocalBuildRunner.ValidatePreparedRelease(recipe, new BuildSelection(TargetProfile.DesktopX64, true));
        try { LocalBuildRunner.ValidatePreparedRelease(recipe, new BuildSelection(TargetProfile.WindowsMobile, true)); throw new Exception("wrong build architecture accepted"); }
        catch (InvalidOperationException) { }
        try { LocalBuildRunner.ValidatePreparedRelease(prepared, new BuildSelection(TargetProfile.DesktopX64, true)); throw new Exception("unknown recipe accepted"); }
        catch (InvalidOperationException) { }
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
sealed class MinecraftAuthFixtureHandler(bool owns, bool fallback) : HttpMessageHandler
{
    private int polls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        string json;
        var status = System.Net.HttpStatusCode.OK;
        if (uri.Host == "login.microsoftonline.com" && uri.AbsolutePath.EndsWith("/devicecode", StringComparison.Ordinal))
            json = "{\"user_code\":\"ABCD-EFGH\",\"device_code\":\"device-secret\",\"verification_uri\":\"https://www.microsoft.com/link\",\"interval\":1,\"expires_in\":60}";
        else if (uri.Host == "login.microsoftonline.com" && uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
        {
            polls++;
            if (polls == 1) { status = System.Net.HttpStatusCode.BadRequest; json = "{\"error\":\"authorization_pending\"}"; }
            else json = "{\"access_token\":\"microsoft-token\"}";
        }
        else if (uri.Host == "user.auth.xboxlive.com") json = "{\"Token\":\"xbox-token\"}";
        else if (uri.Host == "xsts.auth.xboxlive.com") json = "{\"Token\":\"xsts-token\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"user-hash\"}]}}";
        else if (uri.AbsolutePath == "/launcher/login")
        {
            if (fallback) { status = System.Net.HttpStatusCode.Forbidden; json = "{}"; }
            else json = "{\"access_token\":\"minecraft-token\"}";
        }
        else if (uri.AbsolutePath == "/authentication/login_with_xbox") json = "{\"access_token\":\"minecraft-token\"}";
        else if (uri.AbsolutePath == "/entitlements/mcstore")
        {
            if (request.Headers.Authorization?.Parameter != "minecraft-token") throw new Exception("Minecraft token not passed to ownership check");
            json = owns ? "{\"items\":[{\"name\":\"game_minecraft\"}]}" : "{\"items\":[]}";
        }
        else { status = System.Net.HttpStatusCode.NotFound; json = "{}"; }
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
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

sealed class InterruptedDownloadHandler(byte[] artifact) : HttpMessageHandler
{
    private int requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref requests) == 1)
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(artifact)) });
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(artifact) });
    }
}

sealed class InterruptedStream(byte[] bytes) : MemoryStream(bytes)
{
    private bool partialRead;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (partialRead) throw new IOException("Simulated connection interruption");
        partialRead = true;
        return base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
}

sealed class StalledDownloadHandler(byte[] artifact) : HttpMessageHandler
{
    private int requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content =
            Interlocked.Increment(ref requests) == 1 ? new StreamContent(new StalledStream()) : new ByteArrayContent(artifact) });
}

sealed class StalledStream : MemoryStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}

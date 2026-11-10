using Horizon.Builder.Core;

if (args.Length == 2 && args[0] == "--dependency-resolution-proof")
{
    var architecture = args[1];
    var probe = new SystemProbe();
    var dependencyMissing = await DependencyProvisioner.MissingAsync(probe, architecture);
    Console.WriteLine($"Missing before setup ({dependencyMissing.Count}): {string.Join("; ", dependencyMissing)}");
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    await DependencyProvisioner.PrepareAsync(new ConsoleDependencyProgress(), architecture);
    var remaining = await DependencyProvisioner.MissingAsync(new SystemProbe(), architecture);
    Require(remaining.Count == 0, "dependency setup resolves every prerequisite: " + string.Join("; ", remaining));
    Console.WriteLine($"All dependencies are ready for {architecture} in {elapsed.Elapsed.TotalSeconds:F1}s.");
    return;
}

if (args.Length == 1 && args[0] == "--msvc-package-proof")
{
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    foreach (var arch in new[] { "x64", "x86", "arm", "arm64" })
    {
        var target = System.Diagnostics.Stopwatch.StartNew();
        await PrivateMsvcToolset.EnsurePrivateAsync(arch, PrivateMsvcToolset.PrivateRoot,
            PrivateMsvcToolset.DownloadRoot, new ConsoleProgress());
        Require(PrivateMsvcToolset.IsComplete(new SystemProbe(), PrivateMsvcToolset.PrivateRoot, arch),
            $"direct Microsoft C++ package set is complete for {arch}");
        Console.WriteLine($"{arch} direct-package setup: {target.Elapsed.TotalSeconds:F1}s");
    }
    Console.WriteLine($"All four direct-package architecture sets are ready in {elapsed.Elapsed.TotalSeconds:F1}s.");
    return;
}

if (args.Length == 5 && args[0] == "--sdk-supplement-proof")
{
    var output = Path.GetFullPath(args[4]);
    await PrivateWindowsSdk.AssembleSupplementAsync(args[1], args[2], output, [args[3]]);
    Require(PrivateWindowsSdk.IsComplete(output, [args[3], "x64"]), "installed host SDK supplemented with selected target");
    Console.WriteLine($"Reused the installed SDK and supplemented {args[3]}: {output}");
    return;
}

if (args.Length == 4 && args[0] == "--compact-integrated-proof")
{
    var source = Path.GetFullPath(args[1]);
    var profile = Enum.Parse<TargetProfile>(args[2]);
    var arch = profile switch { TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86", TargetProfile.SurfaceProX => "arm64", _ => "arm" };
    var artifact = new CatalogArtifact("https://example.test/proof", new string('a', 64));
    var release = new PreparedRelease(source, new CatalogPatch(LocalRecipe.PatchVersion, LocalRecipe.UpstreamNightlyCommit, 1, artifact, artifact),
        new CatalogClient(LocalRecipe.ClientVersion, 1, artifact), new CompatibilityResult(true, false, false, "Synthetic local proof"), CatalogTrust.BuilderVersion, arch);
    var proofSelection = new BuildSelection(profile, true, UseResourceFramework: true, NativeDeviceVibration: true, MicrosoftClientId: HorizonIdentity.ClientId);
    var result = await LocalBuildRunner.RunPreparedAsync(release, Path.GetFullPath(args[3]), proofSelection, new SystemProbe(), new ConsoleProgress());
    Require(result.SignedOutput is { } signed && File.Exists(signed.Client) && File.Exists(signed.Certificate)
        && signed.ResourceFramework is { } framework && File.Exists(framework), "compact pipeline produced a signed client, framework and certificate");
    Console.WriteLine($"Compact integrated synthetic proof: {result.SignedOutput!.Client}");
    return;
}

if (args.Length == 3 && args[0] == "--compact-framework-proof")
{
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new framework proof directory.");
    Directory.CreateDirectory(root);
    var resources = Path.Combine(root, "resources");
    Directory.CreateDirectory(resources);
    await File.WriteAllTextAsync(Path.Combine(resources, "horizon-resources.json"), "{\"minecraft\":\"26.3\"}");
    var result = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "output"), sdkRoot: Path.GetFullPath(args[1]));
    Require(File.Exists(result.Package), "the compact SDK packaged a synthetic resource framework");
    Console.WriteLine($"Packaged a synthetic resource framework with the compact SDK: {result.Package}");
    return;
}

if (args.Length == 1 && args[0] == "--compact-diagnose")
{
    var probe = new SystemProbe();
    foreach (var arch in new[] { "x86", "x64", "arm", "arm64" })
    {
        var compiler = VisualCppInstallation.FindCompact(probe, arch);
        var sdkCandidate = PrivateWindowsSdk.FindReady(probe, [arch, "x64"]);
        Console.WriteLine($"{arch}: compiler={(compiler is null ? "missing" : compiler.Value.Toolset)}, sdk={sdkCandidate ?? "missing"}, clang={ClangFinder.Find(probe) ?? "missing"}");
    }
    return;
}

if (args.Length is 3 or 4 && args[0] == "--client-backport-proof")
{
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new client proof directory.");
    Directory.CreateDirectory(root);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(args[1]))
        foreach (var name in new[] { "uwp/host/build-game.cmd", "uwp/pregame/host/build-shell.cmd", "uwp/host/package.ps1" })
        {
            var target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = (zip.GetEntry(name) ?? throw new Exception("Missing client file.")).Open();
            using var file = new FileStream(target, FileMode.CreateNew);
            source.CopyTo(file);
        }
    var version = args.Length == 4 ? args[3] : "0.1.1";
    ClientToolchainBackport.Apply(root, version);
    ClientToolchainBackport.Apply(root, version);
    ClientTlsBackport.Apply(root, version);
    ClientTlsBackport.Apply(root, version);
    Require(File.ReadAllText(Path.Combine(root, "uwp", "host", "build-game.cmd")).Contains("HORIZON_UWP_PREPARED_ENV"), "client has the compact-toolchain hook");
    if (version == "0.1.3")
    {
        var tlsSource = File.ReadAllText(Path.Combine(root, "uwp", "host", "tls-index.c"));
        var linkScript = File.ReadAllText(Path.Combine(root, "uwp", "host", "strict-bridge-link.ps1"));
        Require(tlsSource.Contains("_tls_used") && tlsSource.Contains("_tls_start"), "client TLS shim defines the PE TLS directory and sentinels");
        Require(linkScript.Contains("/INCLUDE:_tls_used") && linkScript.Contains("IMAGE_DIRECTORY_ENTRY_TLS"), "client linker includes and validates the TLS directory");
        Require(linkScript.IndexOf("$tlsObject, $rust", StringComparison.Ordinal) >= 0, "TLS object is linked before Rust code");
    }
    Console.WriteLine($"Validated and backported the pinned client scripts: {root}");
    return;
}

if (args.Length == 7 && args[0] == "--compact-host-proof")
{
    var arch = args[1];
    var root = Path.GetFullPath(args[6]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new compact-toolchain proof directory.");
    Directory.CreateDirectory(root);
    var environments = CompactToolchain.Create(args[2], args[3], args[4], arch, Path.Combine(root, "linker"));
    var host = new Dictionary<string, string>(environments.Host, StringComparer.OrdinalIgnoreCase)
    {
        ["HORIZON_UWP_GAME_LIB"] = Path.GetFullPath(args[5]), ["HORIZON_UWP_BUILD_DIR"] = Path.Combine(root, "host"),
        ["HORIZON_UWP_XAML_SHELL"] = "1", ["HORIZON_UWP_ACCOUNT"] = "1", ["HORIZON_UWP_XAUDIO"] = "1",
        ["HORIZON_UWP_HTTP"] = "1", ["HORIZON_UWP_NATIVE_D3D11"] = "1", ["HORIZON_UWP_TOUCH_MOD"] = "1",
        ["HORIZON_UWP_DX12"] = "1", ["HORIZON_UWP_DEVICE_VIBRATION"] = "1"
    };
    await LocalBuildRunner.RunProcess(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        Environment.CurrentDirectory, ["/d", "/c", "uwp\\host\\build-game.cmd", arch], host);
    Require(File.Exists(Path.Combine(root, "host", "HorizonUWP.exe")), "compact toolchain built and strictly linked the host");
    Console.WriteLine($"Built the {arch} host with the compact toolchain: {root}");
    return;
}

if (args.Length == 3 && args[0] == "--client-tls-backport-proof")
{
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new client TLS proof directory.");
    try
    {
        using (var zip = System.IO.Compression.ZipFile.OpenRead(args[1]))
            foreach (var name in new[] { "uwp/host/tls-index.c", "uwp/host/strict-bridge-link.ps1" })
            {
                var target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var entry = zip.Entries.SingleOrDefault(item => item.FullName.Replace('\\', '/') == name)
                    ?? throw new Exception("Missing client TLS input.");
                using var source = entry.Open();
                using var file = new FileStream(target, FileMode.CreateNew);
                source.CopyTo(file);
            }
        ClientTlsBackport.Apply(root, "0.1.3");
        ClientTlsBackport.Apply(root, "0.1.3");
        var tlsSource = File.ReadAllText(Path.Combine(root, "uwp", "host", "tls-index.c"));
        var linkScript = File.ReadAllText(Path.Combine(root, "uwp", "host", "strict-bridge-link.ps1"));
        Require(tlsSource.Contains("_tls_used") && tlsSource.Contains("_tls_start"), "backport defines the PE TLS directory and sentinels");
        Require(linkScript.Contains("/INCLUDE:__tls_used") && linkScript.Contains("/INCLUDE:_tls_used"), "backport forces the TLS directory for 32-bit and 64-bit symbols");
        Require(linkScript.Contains("IMAGE_DIRECTORY_ENTRY_TLS") && linkScript.Contains("$tlsObject, $rust"), "backport validates TLS metadata and links the object before Rust");
        Console.WriteLine("Client 0.1.3 TLS backport validation passed.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
    return;
}

if (args.Length == 4 && args[0] == "--client-uwp-source-backport-proof")
{
    var source = Path.GetFullPath(args[1]);
    ClientUwpSourceBackport.Apply(source, args[2], args[3]);
    ClientUwpSourceBackport.Apply(source, args[2], args[3]);
    var probe = File.ReadAllText(Path.Combine(source, "crates", "horizon-system", "src", "lib.rs"));
    if (args[2] == "0.1.4" && args[3].Equals("e797c725ad99ef311db216de7f41a2e6593353db", StringComparison.OrdinalIgnoreCase))
    {
        Require(probe.Contains("#[cfg(all(windows, not(target_vendor = \"uwp\")))]", StringComparison.Ordinal)
            && probe.Contains("#[cfg(all(windows, target_vendor = \"uwp\"))]", StringComparison.Ordinal),
            "client backport separates desktop and UWP architecture probing");
        var desktopProbe = probe.IndexOf("#[cfg(all(windows, not(target_vendor = \"uwp\")))]", StringComparison.Ordinal);
        var moduleLookup = probe.IndexOf("GetModuleHandleA(c\"kernel32.dll\"", StringComparison.Ordinal);
        Require(desktopProbe >= 0 && moduleLookup > desktopProbe, "desktop-only module lookup remains guarded out of UWP builds");
    }
    Console.WriteLine($"Client {args[2]} UWP source backport validation passed.");
    return;
}

if (args.Length == 3 && args[0] == "--private-git-proof")
{
    var root = Path.GetFullPath(args[2]);
    var git = await PrivateGit.EnsureAsync([], root, Path.GetFullPath(args[1]));
    Require(await PrivateGit.IsUsableAsync(git), "private Git executes");
    Require(await PrivateGit.FindAsync([Path.Combine(root, "missing.exe"), git], root + "-unused") == git,
        "a later usable Git is preferred over a private fallback");
    PrivateGit.Activate(git);
    await TestReleaseSource();
    await TestSourceAssembler();
    Console.WriteLine($"Prepared MinGit and applied the release patch: {git}");
    return;
}

if (args.Length == 3 && args[0] == "--private-python-proof")
{
    var root = Path.GetFullPath(args[2]);
    var python = await PrivatePython.EnsureAsync(null, root, Path.GetFullPath(args[1]));
    Require(await PrivatePython.IsUsableAsync(python), "private Python executes and meets the minimum version");
    Require(await PrivatePython.FindAsync(python, root + "-unused") == python, "an existing compatible Python is preferred");
    Require(await PrivatePython.FindCandidatesAsync([Path.Combine(root, "missing.exe"), python], root + "-unused") == python,
        "a later compatible Python is used if the first candidate is unusable");
    Console.WriteLine($"Prepared and reused embedded Python: {python}");
    return;
}

if (args.Length == 4 && args[0] == "--private-sdk-target-proof")
{
    var destination = Path.GetFullPath(args[3]);
    var prepared = await PrivateWindowsSdk.FindOrPrepareAsync(destination + "-missing", destination, Path.GetFullPath(args[2]), [args[1]]);
    Require(prepared == destination, "missing installed SDK falls back to the verified private SDK");
    Require(PrivateWindowsSdk.IsComplete(destination, [args[1]]), "private SDK contains the selected target");
    Require(!PrivateWindowsSdk.IsComplete(destination), "single-target SDK does not claim all targets");
    var reused = await PrivateWindowsSdk.FindOrPrepareAsync(destination, destination + "-unused", destination + "-downloads", [args[1]]);
    Require(reused == destination && !Directory.Exists(destination + "-downloads"), "the installed target SDK avoids downloads");
    Console.WriteLine($"Assembled and reused private SDK for {args[1]}: {destination}");
    return;
}

if (args.Length == 3 && args[0] == "--private-sdk-proof")
{
    var archives = Path.GetFullPath(args[1]);
    var destination = Path.GetFullPath(args[2]);
    await PrivateWindowsSdk.AssembleAsync(archives, destination);
    Require(PrivateWindowsSdk.IsComplete(destination), "private SDK contains four target libraries and packaging tools");
    var reused = await PrivateWindowsSdk.FindOrPrepareAsync(destination, destination + "-unused", destination + "-downloads");
    Require(reused == destination && !Directory.Exists(destination + "-downloads"), "an existing complete SDK avoids downloads");
    Console.WriteLine($"Assembled and verified private Windows SDK: {destination}");
    return;
}

if (args.Length == 4 && args[0] == "--private-sdk-extension-proof")
{
    var archives = Path.GetFullPath(args[1]);
    var destination = Path.GetFullPath(args[2]);
    var architecture = args[3];
    if (!Directory.Exists(destination)) await PrivateWindowsSdk.AssembleAsync(archives, destination, ["x64"]);
    var extended = await PrivateWindowsSdk.FindOrPrepareAsync(destination + "-no-installed-sdk", destination, archives, [architecture]);
    Require(extended == destination, "a builder-managed SDK can add another target architecture");
    Require(PrivateWindowsSdk.IsComplete(destination, ["x64", architecture]), "the extended SDK contains its original and new target libraries");
    Console.WriteLine($"Extended the private SDK with {architecture}: {destination}");
    return;
}

{
    var root = Path.Combine(Path.GetTempPath(), "HorizonSdkDigestTest-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(root, PrivateWindowsSdk.PackageFileName("")), "tampered package");
        var destination = Path.Combine(root, "sdk");
        try { await PrivateWindowsSdk.AssembleAsync(root, destination); throw new Exception("tampered SDK was accepted"); }
        catch (InvalidDataException) { }
        Require(!Directory.Exists(destination), "tampered SDK did not create a usable layout");
        Require(!Directory.EnumerateDirectories(root, ".sdk-staging-*").Any(), "failed SDK extraction cleaned its staging directory");
        var bytes = System.Text.Encoding.UTF8.GetBytes("verified package");
        var artifact = new CatalogArtifact("https://example.test/artifact", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        var cached = Path.Combine(root, "package.zip");
        await File.WriteAllTextAsync(cached, "damaged package");
        using var transport = new CatalogTransport(new CatalogFixtureHandler([], [], bytes));
        await PinnedDownload.EnsureAsync(transport, artifact, cached);
        Require(File.ReadAllBytes(cached).SequenceEqual(bytes), "damaged cache is automatically replaced with verified content");
        Require(Directory.EnumerateFiles(root, "package.zip.invalid-*").Count() == 1, "damaged cache is preserved for inspection");
        await PinnedDownload.EnsureAsync(transport, artifact, cached);
        Require(Directory.EnumerateFiles(root, "package.zip.invalid-*").Count() == 1, "valid cache is reused without another replacement");
    }
    finally { Directory.Delete(root, recursive: true); }
}

if (args.Length == 2 && args[0] == "--install-vs-proof")
{
    if (!Enum.TryParse<TargetProfile>(args[1], out var target) || !Enum.IsDefined(target)) throw new ArgumentException("Invalid target profile.");
    SystemProbe.RefreshProcessPath();
    var probe = new SystemProbe();
    await VisualStudioBootstrap.EnsureAsync(probe, target, requested => VisualStudioBootstrap.InstallAsync(probe, requested), new ConsoleProgress());
    Require(VisualStudioBootstrap.IsReady(probe, target), "Visual Studio UWP compiler and SDK are present");
    Console.WriteLine($"Verified automatic Visual Studio setup for {target}.");
    return;
}

if ((args.Length == 3 || args.Length == 4) && args[0] == "--framework-fallback-proof")
{
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new framework proof workspace.");
    Directory.CreateDirectory(root);
    var profile = Path.Combine(root, "profile");
    Directory.CreateDirectory(profile);
    await WinUiBootstrap.EnsureAsync(profile, root, args[1], new ConsoleProgress());
    var systemSdkRoot = Environment.GetEnvironmentVariable("ProgramFiles(x86)")!;
    var signTool = args.Length == 4 ? Path.GetFullPath(args[3]) : Path.Combine(systemSdkRoot, "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe");
    var packages = await FrameworkDependencies.FindAsync(args[1], Path.Combine(root, "empty-sdk"), profile, signTool, root);
    Require(packages.Count == 2 && packages.All(File.Exists) && packages[0].StartsWith(root, StringComparison.OrdinalIgnoreCase),
        "Microsoft runtime downloaded, extracted and verified without an SDK runtime package");
    var cached = await FrameworkDependencies.FindAsync(args[1], Path.Combine(root, "empty-sdk"), profile, signTool, root);
    Require(cached[0].StartsWith(Path.Combine(profile, ".huwp", "frameworks"), StringComparison.OrdinalIgnoreCase),
        "verified runtime is reused from the private cache");
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
    var release = new PreparedRelease(source, new CatalogPatch(LocalRecipe.PatchVersion, LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient(LocalRecipe.ClientVersion, 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
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
    var reused = await MinecraftAssetPreparation.StageHelperAsync(source, workspace);
    Require(reused == staged, "matching asset helper workspace is reused on a build retry");
    Console.WriteLine($"Asset helper staged: {staged}");
    return;
}

if (args.Length == 1 && args[0] == "--install-clang-proof")
{
    SystemProbe.RefreshProcessPath();
    var clang = await PrivateClang.EnsureAsync(PrivateClang.DownloadRoot, new ConsoleProgress());
    Require(File.Exists(clang) && (clang != PrivateClang.ClangExecutable || PrivateClang.IsComplete(PrivateClang.Root)),
        "ARM Clang and builtin headers are available after setup");
    Console.WriteLine($"Verified ARM Clang setup: {clang}");
    return;
}

if (args.Length == 3 && args[0] == "--clang-archive-proof")
{
    var archive = Path.GetFullPath(args[1]);
    var root = Path.GetFullPath(args[2]);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Choose a new Clang archive proof directory.");
    Directory.CreateDirectory(root);
    await PrivateClang.ExtractAsync(archive, root);
    Require(PrivateClang.IsComplete(root), "the real LLVM archive extracts the compiler and builtin headers");
    var clang = Path.Combine(root, "bin", "clang.exe");
    var source = Path.Combine(root, "probe.c");
    await File.WriteAllTextAsync(source, "#include <stddef.h>\nsize_t clang_probe(size_t n) { return n + 1; }\n");
    foreach (var (target, name, machine) in new[]
    {
        ("aarch64-uwp-windows-msvc", "arm64.obj", (ushort)0xAA64),
        ("thumbv7a-uwp-windows-msvc", "arm32.obj", (ushort)0x01C4)
    })
    {
        await LocalBuildRunner.RunProcess(clang, root, ["--target=" + target, "-c", source, "-o", name]);
        using var objectFile = File.OpenRead(Path.Combine(root, name));
        using var reader = new BinaryReader(objectFile);
        Require(reader.ReadUInt16() == machine, $"compact Clang compiles a {target} COFF object");
    }
    Console.WriteLine("The real compact LLVM archive extracted and compiled ARM32 and ARM64 COFF objects.");
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
    var originalLib = await File.ReadAllTextAsync(Path.Combine(Path.GetFullPath(args[1]), "crates", "horizon-client", "src", "lib.rs"));
    var staged = await NativeModBuild.ApplyAsync(Path.GetFullPath(args[1]), workspace, ["zoom"], cargo, environment,
        new Progress<string>(Console.WriteLine));
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "game.rs")).Contains("official_zoom"), "registry mod applied to the game");
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "lib.rs")) == originalLib, "client crate root remains unchanged");
    Console.WriteLine("Verified Horizon registry Zoom mod applied to the UWP source.");
    return;
}

if (args.Length == 5 && args[0] == "--apply-native-mods")
{
    var source = Path.GetFullPath(args[1]);
    var workspace = Path.GetFullPath(args[2]);
    if (Directory.Exists(workspace) || File.Exists(workspace)) throw new IOException("Choose a new test workspace.");
    var selectedVersions = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => value.Split('=', 2))
        .ToArray();
    if (selectedVersions.Length == 0 || selectedVersions.Any(value => value.Length != 2))
        throw new ArgumentException("Provide comma-separated modId=version entries.");
    var selected = selectedVersions.Select(value => value[0]).ToArray();
    var expectedVersions = selectedVersions.ToDictionary(value => value[0], value => value[1], StringComparer.Ordinal);
    Directory.CreateDirectory(workspace);
    SystemProbe.UsePrivateRustupForProcess();
    SystemProbe.RefreshProcessPath();
    var probe = new SystemProbe();
    var toolchain = VisualCppInstallation.FindCompact(probe, "x64") ?? throw new Exception("The compact host C++ toolchain was not found.");
    var compactSdk = PrivateWindowsSdk.FindReady(probe, ["x64"]) ?? throw new Exception("The compact Windows SDK was not found.");
    var environment = new Dictionary<string, string>(CompactToolchain.Create(toolchain.Toolset, toolchain.Root,
        compactSdk, "x64", Path.Combine(workspace, "test-linker")).Cargo, StringComparer.OrdinalIgnoreCase)
    {
        ["CARGO_TARGET_DIR"] = LocalBuildRunner.SharedCargoTargetDirectory,
        ["CARGO_BUILD_JOBS"] = Math.Max(1, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
    var cargo = probe.FindOnPath("cargo.exe") ?? throw new Exception("Cargo was not found.");
    var originalLib = await File.ReadAllTextAsync(Path.Combine(source, "crates", "horizon-client", "src", "lib.rs"));
    var staged = await NativeModBuild.ApplyAsync(source, workspace, selected, cargo, environment, args[3], expectedVersions,
        new Progress<string>(Console.WriteLine));
    foreach (var id in selected)
        Require(File.Exists(Path.Combine(workspace, "native-mixins", id, "horizon-mixin.toml")), $"{id} was staged");
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "lib.rs")) == originalLib,
        "client crate root remains unchanged after applying the selected mods");
    Require(File.ReadAllText(Path.Combine(staged, "crates", "horizon-client", "src", "game.rs")).Contains("official_zoom"),
        "the selected Zoom mod was applied to the game");
    Console.WriteLine($"Applied and verified {selected.Length} pinned Horizon native mods for {args[3]}.");
    return;
}

if (args.Length == 4 && args[0] == "--check-native-mod-targets")
{
    var resolver = NativeModTargetResolver.Create(Path.GetFullPath(args[1]));
    var result = resolver.NormalizeBundle(Path.GetFullPath(args[2]), args[3]);
    foreach (var remap in result.RemappedTargets) Console.WriteLine($"Remapped: {remap}");
    Console.WriteLine("Every declared native-mod target resolves in the selected Horizon source.");
    return;
}

if (args.Length == 3 && args[0] == "--check-native-mod-preflight")
{
    var result = await NativeModPreflight.ResolveAsync(args[2], Path.GetFullPath(args[1]), new Progress<string>(Console.WriteLine));
    var expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["classic_zoom"] = "2.0.0", ["fps_overlay"] = "2.0.0", ["jei"] = "0.1.0",
        ["keystrokes"] = "2.0.0", ["killc"] = "5.1.0", ["minimap"] = "2.0.0", ["recipes"] = "2.0.0",
        ["serverify"] = "0.1.0", ["zoom"] = "2.0.0"
    };
    foreach (var (id, version) in expected)
    {
        var listing = result.Mods.Single(mod => mod.Id == id);
        Require(listing.CompatibleVersions?.Any(candidate => candidate.Version == version) == true,
            $"{listing.Name} {version} is visible as compatible: {listing.CompatibilityMessage}");
        Console.WriteLine($"Compatible: {listing.Name} {version}");
    }
    var eatingAnimation = result.Mods.Single(mod => mod.Id == "eatinganimation");
    Require(eatingAnimation.CompatibleVersions?.Any(candidate => candidate.Version == "0.1.1") != true,
        "a native-mod struct literal missing fields from the selected Horizon source is excluded");
    Require(eatingAnimation.CompatibleVersions?.Any(candidate => candidate.Version == "0.2.0") == true,
        "a later release with a complete struct literal remains available when its API is compatible");
    Require(eatingAnimation.CompatibilityMessage.Contains("omits Horizon fields", StringComparison.Ordinal),
        "the disabled release explains the missing fields detected from source and bundle contents");
    Console.WriteLine($"Preflight accepted {expected.Count} pinned mod versions and excluded source-incompatible native-mod releases for Horizon {result.HorizonCommit}.");
    return;
}

if (args.Length == 5 && args[0] == "--build-native-mods-smoke")
{
    static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
    }

    var upstreamArchive = Path.GetFullPath(args[1]);
    var patchFile = Path.GetFullPath(args[2]);
    var clientArchive = Path.GetFullPath(args[3]);
    var resources = Path.GetFullPath(args[4]);
    var git = new SystemProbe().FindOnPath("git.exe") ?? throw new Exception("Git is needed to assemble the signed release inputs.");
    var workspace = Path.Combine(Path.GetTempPath(), "horizon-native-mods-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspace);
    var source = Path.Combine(workspace, "source");
    var upstream = new CatalogArtifact("https://example.test/native-mod-smoke/upstream", await HashFileAsync(upstreamArchive));
    var patchArtifact = new CatalogArtifact("https://example.test/native-mod-smoke/patch", await HashFileAsync(patchFile));
    var clientArtifact = new CatalogArtifact("https://example.test/native-mod-smoke/client", await HashFileAsync(clientArchive));
    await SourceAssembler.AssembleAsync(upstreamArchive, upstream.Sha256, patchFile, patchArtifact.Sha256,
        clientArchive, clientArtifact.Sha256, source, git);
    var available = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["classic_zoom"] = "2.0.0", ["fps_overlay"] = "2.0.0", ["jei"] = "0.1.0",
        ["keystrokes"] = "2.0.0", ["killc"] = "5.1.0", ["minimap"] = "2.0.0", ["recipes"] = "2.0.0",
        ["serverify"] = "0.1.0", ["zoom"] = "2.0.0"
    };
    var requestedIds = Environment.GetEnvironmentVariable("HORIZON_NATIVE_MOD_SMOKE_IDS");
    var expected = requestedIds is null
        ? available
        : requestedIds.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : requestedIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToDictionary(id => id,
                    id => available.TryGetValue(id, out var version) ? version : throw new ArgumentException($"Unknown native-mod smoke id: {id}"),
                    StringComparer.Ordinal);
    var prepared = new PreparedRelease(source,
        new CatalogPatch("0.1.2", "e797c725ad99ef311db216de7f41a2e6593353db", 1, upstream, patchArtifact),
        new CatalogClient("0.1.4", 1, clientArtifact), new CompatibilityResult(true, false, false, "Pinned native-mod smoke test"),
        CatalogTrust.BuilderVersion, "x64");
    var nativeSmokeSelection = new BuildSelection(TargetProfile.DesktopX64, false, PackageVersion: "2026.1109.1.0",
        MicrosoftClientId: HorizonIdentity.ClientId, RegistryNativeMods: expected.Keys.ToArray(), RegistryNativeModVersions: expected);
    var result = await LocalBuildRunner.RunPreparedAsync(prepared, resources, nativeSmokeSelection, new SystemProbe(), new Progress<string>(Console.WriteLine));
    Require(result.SignedOutput is { } signed && File.Exists(signed.Client) && File.Exists(signed.Certificate),
        "the x64 package and public certificate were generated with all compatible pinned registry mods");
    Console.WriteLine($"Built the signed x64 APPX with {expected.Count} compatible pinned native mods. Source-incompatible native-mod releases were excluded before application: {result.SignedOutput!.Client}");
    return;
}

if (args.Length == 3 && args[0] == "--verify-local-catalog")
{
    var candidateCatalog = CatalogSignature.Verify(await File.ReadAllBytesAsync(args[1]), await File.ReadAllBytesAsync(args[2]), CatalogTrust.PublicKey());
    Require(candidateCatalog.Check(LocalRecipe.PatchVersion, LocalRecipe.ClientVersion, CatalogTrust.BuilderVersion, "x64").Allowed, "signed candidate catalog is readable");
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
    var prepared = new PreparedRelease(source, new CatalogPatch(LocalRecipe.PatchVersion, LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient(LocalRecipe.ClientVersion, 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
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
    var prepared = new PreparedRelease(source, new CatalogPatch(LocalRecipe.PatchVersion, LocalRecipe.UpstreamNightlyCommit, 1, upstream, patch),
        new CatalogClient(LocalRecipe.ClientVersion, 1, client), new CompatibilityResult(true, false, false, "Local development proof"), CatalogTrust.BuilderVersion, "x64");
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

if (args.Length == 2 && args[0] == "--inspect-current-builder-catalog")
{
    var candidate = CompatibilityCatalog.Parse(await File.ReadAllTextAsync(args[1]));
    foreach (var architecture in new[] { "x86", "x64", "arm", "arm64" })
    {
        var result = candidate.Check("0.1.0", "0.1.1", CatalogTrust.BuilderVersion, architecture);
        Require(!result.Allowed,
            $"builder {CatalogTrust.BuilderVersion} must reject client 0.1.1 until the 0.1.3 activation-metadata host kit is available");
        Console.WriteLine($"{architecture}: builder {CatalogTrust.BuilderVersion} correctly blocks client 0.1.1");
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
var sourceProbeFixture = "#[cfg(windows)]\nfn emulating_on() -> Option<&'static str> { Some(\"desktop\") }\n\n#[cfg(target_os = \"macos\")]\nfn emulating_on() -> Option<&'static str> { Some(\"macos\") }\n";
var uwpProbe = ClientUwpSourceBackport.Transform(sourceProbeFixture);
Require(uwpProbe.Contains("#[cfg(all(windows, not(target_vendor = \"uwp\")))]", StringComparison.Ordinal)
    && uwpProbe.Contains("#[cfg(all(windows, target_vendor = \"uwp\"))]", StringComparison.Ordinal)
    && uwpProbe.Contains("fn emulating_on() -> Option<&'static str> { None }", StringComparison.Ordinal),
    "UWP source backport avoids desktop-only architecture probing");
Require(ClientUwpSourceBackport.Transform(uwpProbe) == uwpProbe, "UWP source backport is idempotent");
var metadataProof = ReleaseMetadata.Create("0.1.0", "0.1.1", new string('A', 40));
Require(metadataProof.SchemaVersion == 1 && metadataProof.UpstreamCommit == new string('a', 40), "release metadata normalizes catalog identity");
Require(ReleaseMetadata.IsVersion("0.1.1") && !ReleaseMetadata.IsVersion("0.1"), "release metadata accepts three-part versions only");
try { ReleaseMetadata.Create("0.1.0", "0.1.1", "bad"); throw new Exception("invalid release commit accepted"); }
catch (ArgumentException) { }
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
    var metadataPath = Path.Combine(root, "release-metadata.json");
    var metadata = ReleaseMetadata.Create("0.1.0", "0.1.1", new string('a', 40));
    await File.WriteAllBytesAsync(metadataPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(metadata));
    var framework = await ResourceFrameworkPacker.PackAsync(resources, Path.Combine(root, "framework-output"), publisher: publisher);
    var output = Path.Combine(root, "client-output");
    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var arg in new[] { "-NoProfile", "-File", Path.GetFullPath("uwp/host/package.ps1"), "-Architecture", "x64",
        "-Executable", Path.GetFullPath(args[1]), "-OutputDirectory", output, "-ResourceFrameworkName", framework.Name, "-Publisher", publisher,
        "-ReleaseMetadataPath", metadataPath })
        start.ArgumentList.Add(arg);
    using (var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("package script did not start"))
    {
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "client packaging succeeded");
    }
    using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(output, "HorizonUWP-x64.appx"));
    using (var metadataEntry = (zip.GetEntry("HorizonRelease.json") ?? throw new Exception("release metadata missing from catalog package")).Open())
    using (var metadataDocument = await System.Text.Json.JsonDocument.ParseAsync(metadataEntry))
    {
        var json = metadataDocument.RootElement;
        Require(json.GetProperty("schema_version").GetInt32() == 1
            && json.GetProperty("patch_version").GetString() == "0.1.0"
            && json.GetProperty("client_version").GetString() == "0.1.1"
            && json.GetProperty("upstream_commit").GetString() == new string('a', 40),
            "selected catalog identity survives into APPX metadata");
    }
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
await TestBuilderUpdateChecker();
await TestBuilderUpdatePreferences();
await TestNativeBundleStager();
TestNativeModTargetResolver();
await TestMinecraftSignIn();
await TestMinecraftAssetPrefetch();
await TestMinecraftAssetHelperCache();
await TestPrivateClangArchive();
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
            var githubArtifact = Path.Combine(folder, "github-release-asset");
            var githubHandler = new GitHubReleaseAssetHandler(content);
            using (var githubTransport = new CatalogTransport(githubHandler))
                await githubTransport.DownloadArtifactAsync(new CatalogArtifact("https://api.github.com/repos/example/project/releases/assets/123", digest), githubArtifact);
            Require(File.ReadAllBytes(githubArtifact).SequenceEqual(content), "GitHub API release asset follows its HTTPS redirect and verifies the digest");
            Require(githubHandler.ApiAccept == "application/octet-stream"
                && githubHandler.ApiVersion == "2022-11-28"
                && githubHandler.UserAgent?.StartsWith("MCHorizonUWPBuilder/", StringComparison.Ordinal) == true
                && githubHandler.RedirectedAssetRequested,
                "GitHub release asset requests use the binary API headers and a Builder user agent");
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

DirectoryInfo? repoRoot = new(AppContext.BaseDirectory);
while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, "catalog", "catalog-v1.json"))) repoRoot = repoRoot.Parent;
Require(repoRoot is not null, "repository catalog is available to builder tests");
var publishedCatalog = CompatibilityCatalog.Parse(await File.ReadAllTextAsync(Path.Combine(repoRoot!.FullName, "catalog", "catalog-v1.json")));
foreach (var architecture in new[] { "x86", "x64", "arm", "arm64" })
{
    Require(publishedCatalog.Check("0.1.1", "0.1.3", CatalogTrust.BuilderVersion, architecture).Allowed,
        $"the current builder is compatible with patch 0.1.1 and client 0.1.3 for {architecture}");
    Require(publishedCatalog.Check("0.1.2", "0.1.4", CatalogTrust.BuilderVersion, architecture).Allowed,
        $"the current builder is compatible with patch 0.1.2 and client 0.1.4 for {architecture}");
    Require(!publishedCatalog.Check("0.1.2", "0.1.3", CatalogTrust.BuilderVersion, architecture).Allowed,
        $"the current builder rejects the incompatible patch 0.1.2 and client 0.1.3 pair for {architecture}");
}

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
Require(LocalBuildRunner.CargoJobCount(32, 8L * 1024 * 1024 * 1024, 4) == 4, "a selected Cargo job limit overrides automatic memory sizing");
Require(LocalBuildRunner.CargoJobCount(4, 64L * 1024 * 1024 * 1024, 16) == 4, "a selected Cargo job limit is capped at available processors");
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
foreach (var target in Enum.GetValues<TargetProfile>())
{
    var pinnedPlan = LocalRecipe.Plan(new BuildSelection(target, false, PrepareSounds: true));
    Require(pinnedPlan.Steps.Where(step => step.Executable == "cargo").All(step => step.Arguments[0] == "+" + LocalRecipe.RustToolchain),
        "all Cargo stages use the recipe's exact toolchain");
    Require(pinnedPlan.Steps.Single(step => step.Name == "Prepare Rust source copy").Arguments.Contains(LocalRecipe.RustToolchain),
        "Rust source comes from the recipe's exact toolchain");
}
var rustReady = false;
var rustInstalls = 0;
Task<RustToolchainFacts> InspectRustFake() => Task.FromResult(rustReady
    ? new RustToolchainFacts("rustc 1.99.1 (test)", true) : new RustToolchainFacts("", false));
Task InstallRustFake() { rustInstalls++; rustReady = true; return Task.CompletedTask; }
await WindowsPackageManager.EnsureRustToolchainAsync(InspectRustFake, InstallRustFake);
await WindowsPackageManager.EnsureRustToolchainAsync(InspectRustFake, InstallRustFake);
Require(rustInstalls == 1, "the pinned Rust toolchain is installed only when needed");
var failedRustCheck = false;
try { await WindowsPackageManager.EnsureRustToolchainAsync(() => Task.FromResult(new RustToolchainFacts("rustc 1.99.1 (test)", false)), () => Task.CompletedTask); }
catch (InvalidOperationException) { failedRustCheck = true; }
Require(failedRustCheck, "missing rust-src is rejected after installation");
var soundBlockers = LocalRecipe.ToolchainBlockers(new BuildSelection(TargetProfile.WindowsMobile, true, PrepareSounds: true), new FakeProbe(), new RustToolchainFacts("rustc 1.99.1 (test)", true));
Require(soundBlockers.Any(blocker => blocker.Contains("Sound preparation requires the x64")), "ARM sound preparation checks host compiler");
var rustupArguments = WindowsPackageManager.Arguments(InstallableTool.Rustup);
Require(rustupArguments.Contains("1.29.3") && rustupArguments.Contains("--disable-interactivity")
    && rustupArguments.Contains("-y --no-modify-path --default-toolchain none --profile minimal --no-update-default-toolchain"),
    "Rustup uses a pinned silent installer without changing the default toolchain or PATH");
Require(WindowsPackageManager.Arguments(InstallableTool.Git)[2] == "Git.Git", "fixed Git package");
Require(WindowsPackageManager.Arguments(InstallableTool.Python)[2] == "Python.Python.3.13", "fixed Python package");
Require(WindowsPackageManager.Arguments(InstallableTool.Git).Contains("--silent") && WindowsPackageManager.Arguments(InstallableTool.Git).Contains("--disable-interactivity"), "Git setup is noninteractive");
Require(WindowsPackageManager.Arguments(InstallableTool.Python).Contains("--silent") && WindowsPackageManager.Arguments(InstallableTool.Python).Contains("--disable-interactivity"), "Python setup is noninteractive");
Require(WindowsPackageManager.Arguments(InstallableTool.Python).Contains("/quiet /norestart InstallAllUsers=0 PrependPath=1 Include_doc=0 Include_test=0 Include_tcltk=0"), "Python setup omits unneeded UI, documentation and tests while adding the interpreter to PATH");
var autoProbe = new FakeProbe();
var autoInstalled = new List<InstallableTool>();
Task InstallFake(InstallableTool tool)
{
    autoInstalled.Add(tool);
    autoProbe.Executables.Add(tool switch { InstallableTool.Git => "git.exe", InstallableTool.Python => "python.exe", InstallableTool.Rustup => "rustup.exe", _ => throw new Exception("Unexpected setup tool") });
    return Task.CompletedTask;
}
var rustupHomesPrepared = 0;
await WindowsPackageManager.EnsureBuildBasicsAsync(autoProbe, InstallFake, () => { }, prepareRustup: () => rustupHomesPrepared++);
await WindowsPackageManager.EnsureBuildBasicsAsync(autoProbe, InstallFake, () => { }, prepareRustup: () => rustupHomesPrepared++);
Require(autoInstalled.SequenceEqual([InstallableTool.Git, InstallableTool.Python, InstallableTool.Rustup]), "missing Git, Python and Rustup installed once and then reused");
Require(rustupHomesPrepared == 1, "new Rustup gets a private home without changing an existing installation");
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
var occupiedCargoProbe = new FakeProbe();
occupiedCargoProbe.Files.Add(Path.Combine(occupiedCargoProbe.Root, "bin", "cargo.exe"));
var occupiedCargoBlocked = false;
try { await WindowsPackageManager.InstallAsync(occupiedCargoProbe, InstallableTool.Rustup); }
catch (IOException) { occupiedCargoBlocked = true; }
Require(occupiedCargoBlocked, "Rustup does not replace an existing unrelated Cargo executable");
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
Require(VisualStudioBootstrap.Components(TargetProfile.WindowsMobile).Contains("Microsoft.VisualStudio.Component.VC.Tools.ARM")
    && VisualStudioBootstrap.Components(TargetProfile.WindowsMobile).Contains("Microsoft.VisualStudio.Component.Windows10SDK.19041"),
    "ARM32 setup includes its compiler and older SDK");
Require(VisualStudioBootstrap.Components(TargetProfile.SurfaceProX).Contains("Microsoft.VisualStudio.Component.UWP.VC.ARM64")
    && VisualStudioBootstrap.Components(TargetProfile.SurfaceProX).Contains("Microsoft.VisualStudio.Component.Windows11SDK.26100"),
    "ARM64 setup includes the target tools and SDK");
var allComponents = VisualStudioBootstrap.ComponentsForAllTargets();
Require(allComponents.Contains("Microsoft.VisualStudio.Component.VC.Tools.ARM")
    && allComponents.Contains("Microsoft.VisualStudio.Component.VC.Tools.ARM64")
    && allComponents.Contains("Microsoft.VisualStudio.Component.Windows10SDK.19041")
    && allComponents.Distinct(StringComparer.Ordinal).Count() == allComponents.Count,
    "the explicit all-targets component plan has no duplicate components");
var x64CompactComponents = VisualStudioBootstrap.CompactComponentsFor("x64");
var armCompactComponents = VisualStudioBootstrap.CompactComponentsFor("arm");
var arm64CompactComponents = VisualStudioBootstrap.CompactComponentsFor("arm64");
Require(x64CompactComponents.SequenceEqual(["Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "Microsoft.VisualStudio.ComponentGroup.UWP.VC.BuildTools"]),
    "x64 setup installs the host linker libraries and UWP support");
Require(armCompactComponents.SequenceEqual(["Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "Microsoft.VisualStudio.ComponentGroup.UWP.VC.BuildTools", "Microsoft.VisualStudio.Component.VC.Tools.ARM"]),
    "ARM32 setup installs the host linker libraries, UWP support and ARM32 target libraries");
Require(arm64CompactComponents.SequenceEqual(["Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "Microsoft.VisualStudio.ComponentGroup.UWP.VC.BuildTools", "Microsoft.VisualStudio.Component.VC.Tools.ARM64", "Microsoft.VisualStudio.Component.UWP.VC.ARM64"]),
    "ARM64 setup installs host and target libraries with UWP support");
var linkProbe = new FakeProbe();
var linkVs = Path.Combine(linkProbe.Root, "Microsoft Visual Studio", "2022", "BuildTools");
var linkToolsetParent = Path.Combine(linkVs, "VC", "Tools", "MSVC");
var linkToolset = Path.Combine(linkToolsetParent, "14.44.35207");
linkProbe.AddDirectory(Path.Combine(linkProbe.Root, "Microsoft Visual Studio"), Path.GetDirectoryName(linkVs)!);
linkProbe.AddDirectory(Path.GetDirectoryName(linkVs)!, linkVs);
linkProbe.AddDirectory(linkToolsetParent, linkToolset);
linkProbe.Files.UnionWith([Path.Combine(linkToolset, "include", "vcruntime.h"),
    Path.Combine(linkToolset, "lib", "x64", "msvcrt.lib"), Path.Combine(linkToolset, "lib", "x64", "vcruntime.lib"),
    Path.Combine(linkToolset, "lib", "x64", "oldnames.lib"), Path.Combine(linkToolset, "lib", "x64", "libcmt.lib"),
    Path.Combine(linkToolset, "lib", "x64", "legacy_stdio_definitions.lib"),
    Path.Combine(linkToolset, "lib", "arm64", "store", "msvcrt.lib"),
    Path.Combine(linkToolset, "lib", "arm64", "store", "vcruntime.lib"),
    Path.Combine(linkToolset, "lib", "arm64", "store", "oldnames.lib")]);
Require(VisualCppInstallation.FindLinkToolset(linkProbe, "arm64")?.Toolset == linkToolset,
    "selected-target link libraries are discoverable without requiring the MSVC compiler");
Require(!VisualStudioBootstrap.IsReadyForAllTargets(unifiedProbe), "one installed target does not count as setup for all targets");
var cleanVsProbe = new FakeProbe();
cleanVsProbe.Executables.Add("winget.exe");
Require(VisualStudioBootstrap.RequiresWinGet(cleanVsProbe), "a new Build Tools installation needs the current WinGet path");
var cleanVsPlan = VisualStudioBootstrap.Plan(cleanVsProbe, TargetProfile.WindowsMobile);
Require(!cleanVsPlan.Elevate && cleanVsPlan.Arguments.Contains(VisualStudioBootstrap.PackageId)
    && cleanVsPlan.Arguments.Contains(VisualStudioBootstrap.TestedVersion)
    && cleanVsPlan.Arguments.Single(arg => arg.Contains("--add")).Contains("Microsoft.VisualStudio.Component.VC.Tools.ARM"),
    "clean PC uses the pinned Microsoft Build Tools package and ARM components");
var setupPath = Path.Combine(unifiedProbe.Root, "Microsoft Visual Studio", "Installer", "setup.exe");
unifiedProbe.Files.Add(setupPath);
unifiedProbe.AddDirectory(Path.GetDirectoryName(vsRoot)!, vsRoot);
Require(!VisualStudioBootstrap.RequiresWinGet(unifiedProbe), "an existing Build Tools installation can be modified without WinGet");
var mixedSdkProbe = new FakeProbe();
var installedSdkFixture = Path.Combine(mixedSdkProbe.Root, "Windows Kits", "10");
static void AddSdkFixture(FakeProbe fixture, string root, params string[] architectures)
{
    var version = PrivateWindowsSdk.Version;
    foreach (var name in new[] { "makeappx.exe", "signtool.exe", "makepri.exe", "fxc.exe" }) fixture.Files.Add(Path.Combine(root, "bin", version, "x64", name));
    foreach (var path in new[] { Path.Combine("um", "Windows.h"), Path.Combine("ucrt", "corecrt.h"), Path.Combine("shared", "winapifamily.h"),
        Path.Combine("cppwinrt", "winrt", "base.h"), Path.Combine("winrt", "windows.ui.xaml.h") }) fixture.Files.Add(Path.Combine(root, "Include", version, path));
    fixture.Files.Add(Path.Combine(root, "UnionMetadata", version, "Windows.winmd"));
    fixture.Files.Add(Path.Combine(root, "References", version, "Windows.Foundation.UniversalApiContract", "10.0.0.0", "Windows.Foundation.UniversalApiContract.winmd"));
    foreach (var arch in architectures)
    {
        fixture.Files.Add(Path.Combine(root, "Lib", version, "um", arch, "WindowsApp.lib"));
        fixture.Files.Add(Path.Combine(root, "Lib", version, "ucrt", arch, "ucrt.lib"));
    }
}
AddSdkFixture(mixedSdkProbe, installedSdkFixture, "x64", "x86");
AddSdkFixture(mixedSdkProbe, PrivateWindowsSdk.PrivateRoot, "x64", "arm", "arm64");
Require(PrivateWindowsSdk.AllReady(mixedSdkProbe), "installed and private SDK targets can be combined");
Require(PrivateWindowsSdk.FindReady(mixedSdkProbe, ["x86", "x64"]) == installedSdkFixture, "installed SDK is preferred for an available target");
Require(PrivateWindowsSdk.FindReady(mixedSdkProbe, ["arm", "x64"]) == PrivateWindowsSdk.PrivateRoot, "private SDK supplies a missing target");
var repairVsPlan = VisualStudioBootstrap.Plan(unifiedProbe, TargetProfile.SurfaceProX);
Require(repairVsPlan.Elevate && repairVsPlan.Executable == setupPath && repairVsPlan.Arguments.Contains(vsRoot)
    && repairVsPlan.Arguments.Contains("modify") && !repairVsPlan.Arguments.Contains("--wait"),
    "existing Build Tools receive only the selected components without the bootstrapper-only wait flag");
var sdkRoot = Path.Combine(unifiedProbe.Root, "Windows Kits", "10");
var sdkVersion = "10.0.26100.0";
var sdkInstalls = 0;
Task InstallFakeSdk(TargetProfile profile)
{
    sdkInstalls++;
    unifiedProbe.Files.Add(Path.Combine(sdkRoot, "bin", sdkVersion, "x64", "fxc.exe"));
    unifiedProbe.Files.Add(Path.Combine(sdkRoot, "bin", sdkVersion, "x64", "makeappx.exe"));
    unifiedProbe.Files.Add(Path.Combine(sdkRoot, "bin", sdkVersion, "x64", "signtool.exe"));
    unifiedProbe.Files.Add(Path.Combine(sdkRoot, "Include", sdkVersion, "um", "Windows.h"));
    return Task.CompletedTask;
}
await VisualStudioBootstrap.EnsureAsync(unifiedProbe, TargetProfile.SurfaceProX, InstallFakeSdk);
await VisualStudioBootstrap.EnsureAsync(unifiedProbe, TargetProfile.SurfaceProX, InstallFakeSdk);
Require(sdkInstalls == 1, "missing Visual Studio SDK tools are installed once and rechecked");
Require(LocalPackageInstaller.CanInstall(TargetProfile.DesktopX86, System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 19045)), "x86 can install on x64 Windows");
Require(!LocalPackageInstaller.CanInstall(TargetProfile.WindowsMobile, System.Runtime.InteropServices.Architecture.X64, new Version(10, 0, 19045)), "ARM32 is not offered on x64 Windows");
Require(!LocalPackageInstaller.CanInstall(TargetProfile.DesktopX64, System.Runtime.InteropServices.Architecture.Arm64, new Version(10, 0, 19045)), "Windows 10 ARM64 does not offer x64 installation");
Require(LocalPackageInstaller.CanInstall(TargetProfile.DesktopX64, System.Runtime.InteropServices.Architecture.Arm64, new Version(10, 0, 22621)), "Windows 11 ARM64 can offer x64 installation");
Require(LocalBuildRunner.SignedOutputDirectoryName("x64") == "x64"
    && LocalBuildRunner.SignedOutputDirectoryName("arm64") == "arm64"
    && LocalBuildRunner.SignedOutputDirectoryName("arm") == "arm32"
    && LocalBuildRunner.SignedOutputDirectoryName("x86") == "x86", "signed outputs use recognizable architecture directory names");
try { _ = LocalBuildRunner.SignedOutputDirectoryName("neutral"); throw new Exception("unsupported signed output architecture accepted"); }
catch (ArgumentOutOfRangeException) { }
Require(PrebuiltToolchain.ClangTargetTriple("x64") == "x86_64-uwp-windows-msvc"
    && PrebuiltToolchain.ClangTargetTriple("x86") == "i686-uwp-windows-msvc"
    && PrebuiltToolchain.ClangTargetTriple("arm64") == "aarch64-uwp-windows-msvc"
    && PrebuiltToolchain.ClangTargetTriple("arm") == "thumbv7a-uwp-windows-msvc", "compact Clang target triples cover all UWP architectures");
Require(PrebuiltToolchain.CoffMachine("x86") == 0x014c
    && PrebuiltToolchain.CoffMachine("x64") == 0x8664
    && PrebuiltToolchain.CoffMachine("arm") == 0x01c4
    && PrebuiltToolchain.CoffMachine("arm64") == 0xaa64, "UWP shim validation checks the target COFF machine");
var desktopReuse = reuse with { Target = TargetProfile.DesktopX64 };
var localWindowsVersion = new Version(10, 0, 22621);
Require(!LocalPackageInstaller.CanInstall(desktopReuse, System.Runtime.InteropServices.Architecture.X64, localWindowsVersion, false),
    "local install is withheld when the exact reused resource framework is not installed");
Require(LocalPackageInstaller.CanInstall(desktopReuse, System.Runtime.InteropServices.Architecture.X64, localWindowsVersion, true),
    "local install is allowed when the exact reused resource framework is installed");
Require(LocalPackageInstaller.CanInstall(new BuildSelection(TargetProfile.DesktopX64, false, UseResourceFramework: true),
        System.Runtime.InteropServices.Architecture.X64, localWindowsVersion, false),
    "a newly generated resource framework does not require an existing framework on this PC");
Require(LocalPackageInstaller.CanInstall(new BuildSelection(TargetProfile.DesktopX64, false),
        System.Runtime.InteropServices.Architecture.X64, localWindowsVersion, false),
    "bundled resources do not require a separate Horizon framework on this PC");
try { await LocalPackageInstaller.IsResourceFrameworkInstalledAsync("Horizon.Asset.26-3.bad;name", ResourceFrameworkPacker.Publisher); throw new Exception("unsafe package name reached the PowerShell query"); }
catch (ArgumentException) { }
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
try
{
    var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    await LocalBuildRunner.RunProcess(cmd, Path.GetTempPath(), ["/d", "/c", "echo package-diagnostic-proof 1>&2 & exit /b 7"], captureFailureOutput: true);
    throw new Exception("failed packaging tools unexpectedly succeeded");
}
catch (InvalidOperationException error)
{
    Require(error.Message.Contains("package-diagnostic-proof", StringComparison.Ordinal), "failed packaging tools include their useful diagnostic output");
}
Console.WriteLine("Setup checker tests passed.");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static void TestNativeModTargetResolver()
{
    var root = Path.Combine(Path.GetTempPath(), "HorizonNativeTargetResolverTest-" + Guid.NewGuid().ToString("N"));
    var clientRoot = Path.Combine(root, "crates", "horizon-client");
    var libraryRoot = Path.Combine(root, "crates", "horizon-client-lib");
    Directory.CreateDirectory(Path.Combine(clientRoot, "src"));
    Directory.CreateDirectory(libraryRoot);
    File.WriteAllText(Path.Combine(clientRoot, "Cargo.toml"), "[package]\nname = \"horizon-client\"\n[lib]\npath = \"src/lib.rs\"\n");
    File.WriteAllText(Path.Combine(clientRoot, "src", "lib.rs"), "pub use horizon_client_lib::*;\n");
    File.WriteAllText(Path.Combine(libraryRoot, "Cargo.toml"), "[package]\nname = \"horizon-client-lib\"\n[lib]\npath = \"../horizon-client/src/client_lib.rs\"\n");
    File.WriteAllText(Path.Combine(clientRoot, "src", "client_lib.rs"), "pub mod item_render;\n");
    File.WriteAllText(Path.Combine(clientRoot, "src", "item_render.rs"), "pub struct ItemRenderer;\nimpl ItemRenderer { pub fn resolve(&self) {} }\n");
    try
    {
        var resolver = NativeModTargetResolver.Create(root);
        var normalized = resolver.NormalizeManifest("[[injection]]\ntarget = \"horizon_client::item_render::ItemRenderer::resolve\"\n");
        Require(normalized.Manifest.Contains("horizon_client_lib::item_render::ItemRenderer::resolve", StringComparison.Ordinal),
            "re-exported crate target follows its defining library crate");
        Require(normalized.RemappedTargets.Count == 1, "crate target remap is reported");
        try
        {
            resolver.NormalizeManifest("[[injection]]\ntarget = \"horizon_client::item_render::ItemRenderer::missing\"\n");
            throw new Exception("missing native-mod target was accepted");
        }
        catch (InvalidDataException) { }
    }
    finally { Directory.Delete(root, recursive: true); }
}
static async Task TestMinecraftSignIn()
{
    var root = Path.Combine(Path.GetTempPath(), "horizon-minecraft-session-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var store = new ProtectedRefreshTokenStore(Path.Combine(root, "session.dpapi"));
        using (var signIn = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: true, fallback: false), store))
        {
            var challenge = await signIn.BeginAsync();
            Require(challenge.UserCode == "ABCD-EFGH" && challenge.VerificationUri.Host == "www.microsoft.com", "device sign-in instructions returned");
            Require(!challenge.ToString().Contains("device-secret", StringComparison.Ordinal), "device secret is not displayed");
            Require(await signIn.CompleteOwnershipCheckAsync(challenge), "Microsoft, Xbox and Minecraft ownership verified");
        }
        Require(store.Read() == "refresh-secret" && store.HasToken, "only the verified account's refresh token is saved");
        var ciphertext = await File.ReadAllBytesAsync(Path.Combine(root, "session.dpapi"));
        Require(!System.Text.Encoding.UTF8.GetString(ciphertext).Contains("refresh-secret", StringComparison.Ordinal), "saved refresh token is encrypted at rest");
        using (var savedSignIn = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: true, fallback: false), store))
            Require(await savedSignIn.CompleteSavedOwnershipCheckAsync(), "saved sign-in refreshes and verifies ownership without a new device code");
        Require(store.Read() == "rotated-refresh-secret", "rotated refresh token replaces the old token");

        using (var nonOwner = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: false, fallback: true), store))
        {
            var challenge = await nonOwner.BeginAsync();
            Require(!await nonOwner.CompleteOwnershipCheckAsync(challenge), "an account without Minecraft is not treated as an owner");
        }
        Require(store.Read() == "rotated-refresh-secret", "an unowned account does not overwrite a verified saved session");

        store.Write("expired-secret");
        using (var expiredSignIn = new MinecraftSignIn(new MinecraftAuthFixtureHandler(owns: true, fallback: false, refreshExpired: true), store))
        {
            try { await expiredSignIn.CompleteSavedOwnershipCheckAsync(); throw new Exception("expired saved session was accepted"); }
            catch (InvalidOperationException error) when (error.Message.Contains("expired or was revoked", StringComparison.Ordinal))
            { Require(!store.HasToken, "revoked refresh token is removed"); }
        }
        store.Clear();
        Require(!store.HasToken, "sign out clears the saved Microsoft session");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
static async Task TestMinecraftAssetPrefetch()
{
    var root = Path.Combine(Path.GetTempPath(), "horizon-minecraft-assets-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        static string Sha1(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();
        var clientBytes = System.Text.Encoding.UTF8.GetBytes("pinned Minecraft client jar");
        var assetBytes = System.Text.Encoding.UTF8.GetBytes("pinned Minecraft asset");
        var clientHash = Sha1(clientBytes);
        var assetHash = Sha1(assetBytes);
        var assetIndex = System.Text.Encoding.UTF8.GetBytes($"{{\"objects\":{{\"logo\":{{\"hash\":\"{assetHash}\",\"size\":{assetBytes.Length}}}}}}}");
        var assetIndexHash = Sha1(assetIndex);
        var metadata = System.Text.Encoding.UTF8.GetBytes($"{{\"assetIndex\":{{\"id\":\"34\",\"sha1\":\"{assetIndexHash}\",\"size\":{assetIndex.Length},\"url\":\"https://piston-meta.mojang.com/v1/packages/index/34.json\"}},\"downloads\":{{\"client\":{{\"sha1\":\"{clientHash}\",\"size\":{clientBytes.Length},\"url\":\"https://piston-data.mojang.com/client.jar\"}}}}}}");
        var manifestHash = Sha1(metadata);
        var metadataUrl = $"https://piston-meta.mojang.com/v1/packages/{manifestHash}/26.3.json";
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [metadataUrl] = metadata,
            ["https://piston-meta.mojang.com/v1/packages/index/34.json"] = assetIndex,
            ["https://piston-data.mojang.com/client.jar"] = clientBytes,
            [$"https://resources.download.minecraft.net/{assetHash[..2]}/{assetHash}"] = assetBytes
        };
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "research-lock.json"),
            $"{{\"minecraft\":{{\"version\":\"26.3\",\"manifest_sha1\":\"{manifestHash}\",\"client_sha1\":\"{clientHash}\",\"accepted_asset_index_sha1\":[\"{new string('0', 40)}\"]}}}}");

        var cache = Path.Combine(root, "cache");
        var downloaded = await MinecraftAssetPrefetcher.PrefetchAsync(source, true, null, testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: cache);
        Require(downloaded.NetworkFilesFetched == 2 && downloaded.BytesFetched == clientBytes.Length + assetBytes.Length,
            "authenticated prefetch verifies and downloads the client and asset objects from the index in pinned version metadata");
        Require(File.ReadAllBytes(Path.Combine(downloaded.MinecraftDirectory, "versions", "26.3", "26.3.jar")).SequenceEqual(clientBytes)
            && File.ReadAllBytes(Path.Combine(downloaded.MinecraftDirectory, "assets", "objects", assetHash[..2], assetHash)).SequenceEqual(assetBytes),
            "prefetched Minecraft files use the launcher directory layout");
        var reused = await MinecraftAssetPrefetcher.PrefetchAsync(source, true, null, testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: cache);
        Require(reused.NetworkFilesFetched == 0 && reused.BytesFetched == 0, "a later build reuses verified cached files");

        var local = Path.Combine(root, "local");
        var localJar = Path.Combine(local, "versions", "26.3", "26.3.jar");
        var localAsset = Path.Combine(local, "assets", "objects", assetHash[..2], assetHash);
        Directory.CreateDirectory(Path.GetDirectoryName(localJar)!);
        Directory.CreateDirectory(Path.GetDirectoryName(localAsset)!);
        await File.WriteAllBytesAsync(localJar, clientBytes);
        await File.WriteAllBytesAsync(localAsset, assetBytes);
        var localReuse = await MinecraftAssetPrefetcher.PrefetchAsync(source, true, local,
            testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: Path.Combine(root, "local-cache"));
        Require(localReuse.LocalFilesReused == 2 && localReuse.NetworkFilesFetched == 0,
            "the builder copies verified files from a local Minecraft 26.3 installation");

        var lowSpaceCache = Path.Combine(root, "low-space-cache");
        long checkedAvailable = -1;
        long checkedRequired = -1;
        try
        {
            await MinecraftAssetPrefetcher.PrefetchAsync(source, true, null,
                testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: lowSpaceCache,
                confirmInsufficientSpace: (available, required) =>
                {
                    checkedAvailable = available;
                    checkedRequired = required;
                    return Task.FromResult(false);
                }, testAvailableSpaceBytes: 0);
            throw new Exception("low-space asset preparation was not canceled");
        }
        catch (OperationCanceledException)
        {
            Require(!Directory.Exists(lowSpaceCache), "low-space cancellation happens before writing assets into the cache");
        }
        Require(checkedAvailable == 0 && checkedRequired == metadata.Length + assetIndex.Length + clientBytes.Length + assetBytes.Length,
            "low-space warning receives the exact missing metadata, index, client and asset byte count");

        var bypassed = await MinecraftAssetPrefetcher.PrefetchAsync(source, true, null,
            testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: Path.Combine(root, "bypassed-cache"),
            confirmInsufficientSpace: (_, _) => Task.FromResult(true), testAvailableSpaceBytes: 0);
        Require(bypassed.NetworkFilesFetched == 2 && bypassed.BytesFetched == clientBytes.Length + assetBytes.Length,
            "choosing to continue bypasses the low-space warning and completes the verified asset download");

        var unauthorizedCache = Path.Combine(root, "unauthorized-cache");
        try
        {
            await MinecraftAssetPrefetcher.PrefetchAsync(source, false, local, testHandler: new AssetPrefetchFixtureHandler(responses), testCacheRoot: unauthorizedCache);
            throw new Exception("Minecraft files were prefetched before ownership verification");
        }
        catch (InvalidOperationException) { Require(!Directory.Exists(unauthorizedCache), "ownership is checked before local copy or network access"); }
    }
    finally { Directory.Delete(root, recursive: true); }
}
static async Task TestMinecraftAssetHelperCache()
{
    var root = Path.Combine(Path.GetTempPath(), "mchorizon-asset-helper-cache-test-" + Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var workspace = Path.Combine(root, "workspace");
    Directory.CreateDirectory(Path.Combine(source, "launcher", "src"));
    Directory.CreateDirectory(workspace);
    try
    {
        var mojang = Path.Combine(source, "launcher", "src", "mojang.rs");
        var sources = Path.Combine(source, "launcher", "src", "sources.rs");
        await File.WriteAllTextAsync(mojang, "pinned mojang downloader");
        await File.WriteAllTextAsync(sources, "pinned source downloader");

        var first = await MinecraftAssetPreparation.StageHelperAsync(source, workspace);
        var firstWriteTime = File.GetLastWriteTimeUtc(Path.Combine(first, "src", "main.rs"));
        var repeated = await MinecraftAssetPreparation.StageHelperAsync(source, workspace);
        Require(repeated == first && File.GetLastWriteTimeUtc(Path.Combine(repeated, "src", "main.rs")) == firstWriteTime,
            "an exact asset-helper workspace is reused without rewriting its files");

        await File.WriteAllTextAsync(mojang, "changed pinned downloader");
        var changed = await MinecraftAssetPreparation.StageHelperAsync(source, workspace);
        Require(changed != first && File.ReadAllText(Path.Combine(changed, "src", "mojang.rs")) == "changed pinned downloader",
            "changed authenticated helper inputs receive a separate content-keyed workspace");
        Require(File.ReadAllText(Path.Combine(first, "src", "mojang.rs")) == "pinned mojang downloader",
            "creating a new helper workspace preserves the prior release workspace");

        var staleWorkspace = Path.Combine(root, "stale-workspace");
        var staleHelper = Path.Combine(staleWorkspace, "asset-tool");
        Directory.CreateDirectory(staleHelper);
        await File.WriteAllTextAsync(Path.Combine(staleHelper, "partial.tmp"), "interrupted build");
        var rebuilt = await MinecraftAssetPreparation.StageHelperAsync(source, staleWorkspace);
        Require(rebuilt != staleHelper && File.ReadAllText(Path.Combine(staleHelper, "partial.tmp")) == "interrupted build"
            && File.Exists(Path.Combine(rebuilt, "Cargo.lock")),
            "an incomplete helper workspace is left intact while a matching isolated workspace is prepared");
    }
    finally { Directory.Delete(root, recursive: true); }
}
static async Task TestPrivateClangArchive()
{
    var root = Path.Combine(Path.GetTempPath(), "mchorizon-clang-archive-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var archive = Path.Combine(root, "clang.tar.zst");
        var extracted = Path.Combine(root, "extracted");
        Directory.CreateDirectory(extracted);
        await using (var output = File.Create(archive))
        using (var zstd = new ZstdSharp.CompressionStream(output, 1))
        using (var tar = new System.Formats.Tar.TarWriter(zstd, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
        {
            var prefix = PrivateClang.ArchiveRoot;
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, prefix + "/"));
            Add(prefix + "/bin/clang.exe", "clang-test");
            Add(prefix + "/bin/llvm-lib.exe", "llvm-lib-test");
            Add(prefix + "/lib/clang/23/include/stddef.h", "header-test");
            Add(prefix + "/bin/clang++.exe", "unneeded");

            void Add(string name, string content)
            {
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name)
                { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)) };
                tar.WriteEntry(entry);
            }
        }
        await PrivateClang.ExtractAsync(archive, extracted);
        Require(PrivateClang.IsComplete(extracted), "private Clang checks its compiler, archiver and builtin headers");
        Require(!File.Exists(Path.Combine(extracted, "bin", "clang++.exe")), "private Clang extracts only the required C compiler tools and builtin headers");
        Require(await File.ReadAllTextAsync(Path.Combine(extracted, "bin", "clang.exe")) == "clang-test"
            && await File.ReadAllTextAsync(Path.Combine(extracted, "bin", "llvm-lib.exe")) == "llvm-lib-test"
            && await File.ReadAllTextAsync(Path.Combine(extracted, "lib", "clang", "23", "include", "stddef.h")) == "header-test",
            "private Clang extracts the required files from a Zstandard tar archive");
    }
    finally { Directory.Delete(root, recursive: true); }
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
        var armPrepared = prepared.ForArchitecture("arm", new CompatibilityResult(true, false, false, "Signed catalog permits ARM32"));
        Require(armPrepared.SourceDirectory == prepared.SourceDirectory && armPrepared.Architecture == "arm"
            && armPrepared.Compatibility.Allowed, "a prepared source is reusable with separately checked target compatibility");
        var isolatedSource = Path.Combine(root, "isolated-source");
        LocalBuildRunner.CopyPreparedSource(prepared.SourceDirectory, isolatedSource);
        await File.WriteAllTextAsync(Path.Combine(isolatedSource, "main.txt"), "changed\n");
        Require((await File.ReadAllTextAsync(Path.Combine(prepared.SourceDirectory, "main.txt"))).Replace("\r\n", "\n") == "world\n",
            "a target's private build source cannot mutate the shared prepared release");
        var recipe = new PreparedRelease(prepared.SourceDirectory, prepared.Patch with { Version = LocalRecipe.PatchVersion, UpstreamCommit = LocalRecipe.UpstreamNightlyCommit },
            prepared.Client with { Version = LocalRecipe.ClientVersion }, prepared.Compatibility, CatalogTrust.BuilderVersion, "x64");
        LocalBuildRunner.ValidatePreparedRelease(recipe, new BuildSelection(TargetProfile.DesktopX64, true));
        var updatedRecipe = new PreparedRelease(recipe.SourceDirectory,
            recipe.Patch with { Version = "0.1.2", UpstreamCommit = new string('b', 40) }, recipe.Client,
            recipe.Compatibility, CatalogTrust.BuilderVersion, "x64");
        LocalBuildRunner.ValidatePreparedRelease(updatedRecipe, new BuildSelection(TargetProfile.DesktopX64, true));
        var updatedPlan = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, true), updatedRecipe.Patch);
        Require(updatedPlan.SourceCommit == new string('b', 40)
            && updatedPlan.Notes.Any(note => note.Contains(new string('b', 40), StringComparison.Ordinal)),
            "the recipe follows the upstream commit selected by the signed patch catalog");
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

static async Task TestBuilderUpdateChecker()
{
    const string response = """
        [
          {"tag_name":"builder-v0.1.2-preview.1","draft":false,"prerelease":true},
          {"tag_name":"builder-v0.1.9","draft":true,"prerelease":false},
          {"tag_name":"client-v0.9.0","draft":false,"prerelease":false},
          {"tag_name":"builder-v0.1.8","draft":false,"prerelease":false,"body":"Reliable GitHub release asset downloads."},
          {"tag_name":"builder-v0.1.7","draft":false,"prerelease":false,"body":"Multi-architecture builds and Cargo caching."},
          {"tag_name":"builder-v0.1.6","draft":false,"prerelease":false,"body":"Build speed settings and progress details."}
        ]
        """;
    using var handler = new BuilderUpdateFixtureHandler(response);
    using var client = new HttpClient(handler);
    var update = await BuilderUpdateChecker.CheckAsync(client, "0.1.7");
    Require(handler.RequestedEndpoint == BuilderUpdateChecker.ReleasesEndpoint, "builder checks the MCHorizonUWP GitHub release list");
    Require(handler.UserAgent?.Contains("0.1.7", StringComparison.Ordinal) == true, "builder update requests identify the installed version");
    Require(update is { Version: "0.1.8", Changelog: "Reliable GitHub release asset downloads." }
        && update.ReleaseUri == new Uri("https://github.com/megabytesme/MCHorizonUWP/releases/tag/builder-v0.1.8"),
        "builder update checker selects the newest stable builder release only");

    using var sameVersionHandler = new BuilderUpdateFixtureHandler(response);
    using var sameVersionClient = new HttpClient(sameVersionHandler);
    Require(await BuilderUpdateChecker.CheckAsync(sameVersionClient, "0.1.8") is null,
        "builder update checker reports no update when the current version is latest");
}

static async Task TestBuilderUpdatePreferences()
{
    var root = Path.Combine(Path.GetTempPath(), "HorizonBuilderUpdateSettings-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "settings.json");
    try
    {
        Require(BuilderUpdatePreferences.ReadEnabled(path), "builder update checks default to enabled");
        BuilderUpdatePreferences.WriteEnabled(false, path);
        Require(!BuilderUpdatePreferences.ReadEnabled(path), "builder update checks persist as disabled");
        BuilderUpdatePreferences.WriteEnabled(true, path);
        Require(BuilderUpdatePreferences.ReadEnabled(path), "builder update checks can be re-enabled");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
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

sealed class ConsoleDependencyProgress : IProgress<DependencyProgress>
{
    public void Report(DependencyProgress value) => Console.WriteLine($"{value.Id}: {value.Detail}");
}
sealed class MinecraftAuthFixtureHandler(bool owns, bool fallback, bool refreshExpired = false) : HttpMessageHandler
{
    private int polls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        string json;
        var status = System.Net.HttpStatusCode.OK;
        if (uri.Host == "login.microsoftonline.com" && uri.AbsolutePath.EndsWith("/devicecode", StringComparison.Ordinal))
            json = "{\"user_code\":\"ABCD-EFGH\",\"device_code\":\"device-secret\",\"verification_uri\":\"https://www.microsoft.com/link\",\"interval\":1,\"expires_in\":60}";
        else if (uri.Host == "login.microsoftonline.com" && uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
        {
            var grant = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (grant.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                if (refreshExpired) { status = System.Net.HttpStatusCode.BadRequest; json = "{\"error\":\"invalid_grant\"}"; }
                else json = "{\"access_token\":\"microsoft-token\",\"refresh_token\":\"rotated-refresh-secret\"}";
            }
            else
            {
                polls++;
                if (polls == 1) { status = System.Net.HttpStatusCode.BadRequest; json = "{\"error\":\"authorization_pending\"}"; }
                else json = "{\"access_token\":\"microsoft-token\",\"refresh_token\":\"refresh-secret\"}";
            }
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
        return new HttpResponseMessage(status) { Content = new StringContent(json) };
    }
}
sealed class AssetPrefetchFixtureHandler(Dictionary<string, byte[]> inputs) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(inputs.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
}
sealed class ReleaseFixtureHandler(Dictionary<string, byte[]> inputs) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(inputs.TryGetValue(request.RequestUri!.AbsolutePath, out var bytes)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
}
sealed class BuilderUpdateFixtureHandler(string response) : HttpMessageHandler
{
    public string? RequestedEndpoint { get; private set; }
    public string? UserAgent { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedEndpoint = request.RequestUri?.AbsoluteUri;
        UserAgent = request.Headers.UserAgent.ToString();
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
        });
    }
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

sealed class GitHubReleaseAssetHandler(byte[] artifact) : HttpMessageHandler
{
    public string? ApiAccept { get; private set; }
    public string? ApiVersion { get; private set; }
    public string? UserAgent { get; private set; }
    public bool RedirectedAssetRequested { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            ApiAccept = request.Headers.Accept.FirstOrDefault()?.MediaType;
            ApiVersion = request.Headers.TryGetValues("X-GitHub-Api-Version", out var values) ? values.SingleOrDefault() : null;
            UserAgent = request.Headers.UserAgent.ToString();
            var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://release-assets.example.test/client.zip");
            return Task.FromResult(redirect);
        }

        RedirectedAssetRequested = request.RequestUri.Host.Equals("release-assets.example.test", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(RedirectedAssetRequested
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(artifact) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
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

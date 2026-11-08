using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record LocalBuildResult(string Workspace, string UnsignedPackage, string? SoundWarning,
    string? ResourceFrameworkPackage = null, SignedPackageOutput? SignedOutput = null);

/// <summary>Runs only the pinned local recipe in a fresh, builder-owned workspace.</summary>
public static class LocalBuildRunner
{
    public static int CargoJobCount(int processors, long memoryBytes, int requestedJobs = 0)
    {
        const long gib = 1024L * 1024 * 1024;
        var memoryJobs = Math.Max(1, (memoryBytes - Math.Min(memoryBytes, 2 * gib)) / (2 * gib));
        var automatic = (int)Math.Min(Math.Max(1, processors), memoryJobs);
        return requestedJobs <= 0 ? automatic : Math.Min(Math.Max(1, processors), Math.Max(1, requestedJobs));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static long AvailableBuildMemory()
    {
        if (!OperatingSystem.IsWindows()) return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (long)Math.Min(Math.Min(status.TotalPhysical, status.AvailablePageFile), (ulong)long.MaxValue);
    }
    internal static int CurrentCargoJobCount(int requestedJobs = 0) => CargoJobCount(Environment.ProcessorCount, AvailableBuildMemory(), requestedJobs);
    internal static string SharedCargoTargetDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Cache", "CargoTarget");

    public static async Task<LocalBuildResult> RunAsync(string source, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null)
        => await RunCoreAsync(source, resources, selection, probe, progress, minecraftDirectory, null, null);

    /// <summary>Build the exact split-source recipe and sign its output. Other catalog versions need their own recipe.</summary>
    public static async Task<LocalBuildResult> RunPreparedAsync(PreparedRelease release, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null, Func<int>? requestedCargoJobs = null)
    {
        ValidatePreparedRelease(release, selection);
        return await RunCoreAsync(release.SourceDirectory, resources, selection, probe, progress, minecraftDirectory, release, requestedCargoJobs);
    }

    internal static void ValidatePreparedRelease(PreparedRelease release, BuildSelection selection)
    {
        ArgumentNullException.ThrowIfNull(release);
        var arch = selection.Target switch { TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
            TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => "" };
        if (!release.Compatibility.Allowed || release.Architecture != arch || release.BuilderVersion != CatalogTrust.BuilderVersion
            || release.Patch.Version != LocalRecipe.PatchVersion || release.Client.Version != LocalRecipe.ClientVersion
            || !release.Patch.UpstreamCommit.Equals(LocalRecipe.UpstreamNightlyCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The prepared release does not match this build recipe and target.");
    }

    private static async Task<LocalBuildResult> RunCoreAsync(string source, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress, string? minecraftDirectory, PreparedRelease? release, Func<int>? requestedCargoJobs)
    {
        int CaptureCargoJobs(string step)
        {
            var requested = requestedCargoJobs?.Invoke() ?? 0;
            var jobs = CurrentCargoJobCount(requested);
            progress?.Report($"Starting Cargo compile for {step} with {jobs} parallel jobs{(requested <= 0 ? " (automatic, memory-aware)" : " (selected limit)")}. The build-speed setting is now locked for this run.");
            return jobs;
        }

        var plan = LocalRecipe.Plan(selection);
        if (release is null) await LocalRecipe.ValidateSourceAsync(source, probe);
        else if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The prepared release source is missing.");
        var existingFramework = selection.ValidatedExistingResourceFramework;
        LocalRecipe.ValidateResourceSelection(selection, resources, minecraftDirectory);
        var workspaceBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huwp", "b");
        if (existingFramework is null)
        {
            if (ResourcesContainWorkspace(resources!, workspaceBase))
                throw new InvalidOperationException("Choose a resource directory that does not contain the builder workspace.");
            if (release is not null && ResourcesContainWorkspace(resources!, Path.GetDirectoryName(Path.GetFullPath(source))!))
                throw new InvalidOperationException("Choose a resource directory that does not contain the release workspace.");
            ValidateResourceTree(resources!);
        }
        var targetArchitecture = selection.Target switch { TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
            TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => throw new ArgumentOutOfRangeException(nameof(selection)) };
        var compactSdk = release is null ? null : PrivateWindowsSdk.FindReady(probe, [targetArchitecture, "x64"]);
        var msvcToolset = release is null ? null : VisualCppInstallation.FindLinkToolset(probe, targetArchitecture)?.Toolset;
        var signTool = release is null ? Path.Combine(probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe")
            : Path.Combine(compactSdk ?? throw new InvalidOperationException("The compact Windows SDK is missing."), "bin", PrivateWindowsSdk.Version, "x64", "signtool.exe");
        if (release is not null && !probe.FileExists(signTool))
            throw new FileNotFoundException("The signing tool for this recipe is missing.", signTool);
        string? angleBin = null;
        {
            var triplet = selection.Target switch { TargetProfile.WindowsMobile => "arm-uwp-local", TargetProfile.SurfaceProX => "arm64-uwp-local", TargetProfile.DesktopX86 => "x86-uwp-local", _ => "x64-uwp-local" };
            var vcpkgRoot = selection.Target switch { TargetProfile.WindowsMobile => "vcpkg-arm32", TargetProfile.SurfaceProX => "vcpkg", TargetProfile.DesktopX86 => "vcpkg-x86", _ => "vcpkg-x64" };
            var candidate = Path.Combine(Path.GetFullPath(source), ".local", vcpkgRoot, "installed", triplet, "bin");
            if (new[] { "libEGL.dll", "libGLESv2.dll" }.All(name => File.Exists(Path.Combine(candidate, name))))
                angleBin = candidate;
        }
        var unifiedToolchain = release is not null;
        string? toolchainRoot = null;
        var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
            splitRelease: release is not null, unifiedToolchain: unifiedToolchain, compactToolchain: release is not null);
        if (blockers.Count != 0) throw new InvalidOperationException(string.Join(" ", blockers));
        var hostExecutable = release is null ? null : RequirePrebuiltHost(source, targetArchitecture);
        progress?.Report(release is null ? "Creating an isolated snapshot of the pinned source revision." : "Using the authenticated release source workspace.");
        var root = release is null ? await CreateSnapshotAsync(source, probe)
            : Directory.GetParent(Path.GetFullPath(source))?.FullName ?? throw new InvalidOperationException("Invalid release workspace.");
        var snapshot = release is null ? Path.Combine(root, "source") : Path.GetFullPath(source);
        if (release is not null && release.Client.Version is "0.1.0" or "0.1.1")
            ClientToolchainBackport.Apply(snapshot, release.Client.Version);
        var cargo = probe.FindOnPath("cargo.exe")!;
        var python = probe.FindOnPath("python.exe")!;
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var cmd = Path.Combine(system, "cmd.exe");
        var powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        var jobs = CurrentCargoJobCount();

        CompactEnvironment? compact = null;
        if (release is not null)
        {
            var clang = ClangFinder.Find(probe) ?? throw new InvalidOperationException("The compact LLVM C compiler is missing. Check setup again.");
            var rustLld = await LocalRecipe.FindRustLinkerAsync(probe);
            compact = PrebuiltToolchain.Create(snapshot, compactSdk!, targetArchitecture, clang, rustLld, msvcToolset!,
                Path.Combine(root, "prebuilt-linker"), probe);
        }
        IReadOnlyList<string>? dependencies = null;
        if (release is not null)
        {
            await WinUiBootstrap.EnsureAsync(WinUiBootstrap.PrivateProfile, root, targetArchitecture, progress);
            progress?.Report("Checking the Microsoft frameworks needed to install on the target.");
            dependencies = await FrameworkDependencies.FindAsync(targetArchitecture,
                probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
                WinUiBootstrap.PrivateProfile, signTool, root);
        }

        if (selection.RegistryNativeMods is { Count: > 0 } selectedMods)
        {
            if (release is null) throw new InvalidOperationException("Native registry mods require an authenticated source release.");
            var hostEnvironment = new Dictionary<string, string>(compact!.Cargo, StringComparer.OrdinalIgnoreCase);
            hostEnvironment["CARGO_TARGET_DIR"] = SharedCargoTargetDirectory;
            jobs = CaptureCargoJobs("the native-mod tool");
            hostEnvironment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot = await NativeModBuild.ApplyAsync(snapshot, root, selectedMods, cargo, hostEnvironment, progress);
        }

        var rustSource = Path.Combine(snapshot, ".local", "builder-rust-std");
        var rustLibrary = Path.Combine(rustSource, "library");
        var rustPatches = new[] { "stdio", "file-open", "module", "runtime" }
            .Select(name => Path.Combine(rustSource, "horizon-" + name + "-uwp.patch"));
        if (Directory.Exists(rustSource))
        {
            if (!Directory.Exists(rustLibrary) || rustPatches.Any(path => !File.Exists(path)))
                throw new InvalidDataException("The existing private Rust source copy is incomplete. Preserve it and choose a new prepared release workspace.");
            progress?.Report("Reusing the prepared private Rust standard-library source.");
        }
        else
        {
            progress?.Report("Preparing a private copy of the Rust standard library.");
            await RunProcess(powershell, snapshot, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/rust-std/prepare-source.ps1", "-Destination", ".local/builder-rust-std", "-Toolchain", LocalRecipe.RustToolchain]);
        }

        var arm = selection.Target == TargetProfile.WindowsMobile;
        if (arm)
        {
            progress?.Report("Preparing the ARM32 offline dependency overrides.");
            await RunProcess(python, snapshot, ["uwp/arm32-deps/prepare.py", "--offline-arm32"],
                new Dictionary<string, string> { ["RUSTUP_TOOLCHAIN"] = LocalRecipe.RustToolchain });
        }

        progress?.Report(release is null ? "Preparing the Visual C++ environment." : "Preparing the compact Rust and Clang environment.");
        var vcEnvironment = compact is null ? await VisualCppEnvironment(cmd, selection.Target, probe, splitRelease: false, unifiedRoot: null)
            : new Dictionary<string, string>(compact.Cargo, StringComparer.OrdinalIgnoreCase);
        if (release is not null) vcEnvironment["CARGO_TARGET_DIR"] = SharedCargoTargetDirectory;
        if (release is not null)
            vcEnvironment["HORIZON_UWP_RUST_LLD"] = await LocalRecipe.FindRustLinkerAsync(probe);
        // Never inherit an unrelated client ID from the parent environment.
        vcEnvironment["HORIZON_MSA_CLIENT_ID"] = selection.ValidatedMicrosoftClientId ?? "";
        vcEnvironment["HORIZON_VERSION"] = LocalRecipe.UpstreamReleaseVersion;
        vcEnvironment["HORIZON_NIGHTLY"] = LocalRecipe.UpstreamNightly;
        vcEnvironment["HORIZON_UPSTREAM_COMMIT"] = LocalRecipe.UpstreamNightlyCommit;
        vcEnvironment["HORIZON_UWP_PORT_REF"] = release is null ? LocalRecipe.SourceCommit : "client-v" + release.Client.Version;
        vcEnvironment["HORIZON_UWP_XAML_SHELL"] = "1";
        vcEnvironment["RUSTC_BOOTSTRAP"] = "1";
        vcEnvironment["__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS"] = "nightly";
        vcEnvironment["__CARGO_TESTS_ONLY_SRC_ROOT"] = Path.Combine(snapshot, ".local", "builder-rust-std", "library");
        vcEnvironment["CARGO_BUILD_JOBS"] = CurrentCargoJobCount().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (selection.Target == TargetProfile.SurfaceProX)
        {
            vcEnvironment["CC_aarch64_uwp_windows_msvc"] = ClangFinder.Find(probe) ?? throw new InvalidOperationException("Clang is missing.");
        }
        if (arm)
        {
            vcEnvironment["CC_thumbv7a_uwp_windows_msvc"] = ClangFinder.Find(probe) ?? throw new InvalidOperationException("Clang is missing.");
            vcEnvironment["CFLAGS_thumbv7a_uwp_windows_msvc"] = "-DWINAPI_FAMILY=WINAPI_FAMILY_APP";
        }
        progress?.Report("Fetching missing Cargo dependencies from configured registries.");
        try
        {
            await RunProcess(cargo, snapshot, plan.Steps.Single(step => step.Name == "Fetch Cargo dependencies").Arguments, vcEnvironment, progress: progress);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("Cargo could not fetch dependencies. Check the internet connection and configured Cargo registries. " + error.Message, error);
        }

        var packageResources = existingFramework is not null ? null : selection.PrepareSounds
            ? await PrepareSoundResourcesAsync(snapshot, resources!, minecraftDirectory!, probe, progress, toolchainRoot, compact?.Cargo, requestedCargoJobs)
            : Path.GetFullPath(resources!);

        progress?.Report("Building the game bridge. This may take several minutes.");
        try
        {
            jobs = CaptureCargoJobs("the Horizon game bridge");
            vcEnvironment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bridgeArguments = plan.Steps.Single(step => step.Name == "Build game bridge").Arguments
                .Select(argument => argument == ".local/builder-target" ? SharedCargoTargetDirectory : argument).ToArray();
            await RunProcess(cargo, snapshot, bridgeArguments, vcEnvironment, progress: progress);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("The offline Cargo build failed. Check that the required toolchain and dependencies are available. " + error.Message, error);
        }

        var arch = selection.Target switch
        {
            TargetProfile.DesktopX64 => "x64",
            TargetProfile.DesktopX86 => "x86",
            TargetProfile.SurfaceProX => "arm64",
            TargetProfile.WindowsMobile => "arm",
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        var cachedGameLibrary = Path.Combine(SharedCargoTargetDirectory, selection.RustTarget, "release", "horizon_uwp_game_bridge.lib");
        if (!File.Exists(cachedGameLibrary)) throw new FileNotFoundException("Cargo did not produce the Horizon game bridge library.", cachedGameLibrary);
        var workspaceGameLibrary = Path.Combine(snapshot, ".local", "builder-target", selection.RustTarget, "release", "horizon_uwp_game_bridge.lib");
        Directory.CreateDirectory(Path.GetDirectoryName(workspaceGameLibrary)!);
        File.Copy(cachedGameLibrary, workspaceGameLibrary, overwrite: true);
        vcEnvironment["HORIZON_UWP_XAUDIO"] = "1";
        vcEnvironment["HORIZON_UWP_HTTP"] = "1";
        vcEnvironment["HORIZON_UWP_ACCOUNT"] = "1";
        vcEnvironment["HORIZON_UWP_XAML_SHELL"] = "1";
        vcEnvironment["HORIZON_UWP_NATIVE_D3D11"] = "1";
        vcEnvironment["HORIZON_UWP_TOUCH_MOD"] = selection.IncludesNativeTouchMod ? "1" : "";
        vcEnvironment["HORIZON_UWP_DX12"] = "1";
        vcEnvironment["HORIZON_UWP_ANGLE"] = angleBin is null ? "" : "1";
        // The UWP client probes available renderers and chooses on the target device.
        vcEnvironment["HORIZON_UWP_DEFAULT_RENDERER"] = "";
        vcEnvironment["HORIZON_UWP_DEVICE_VIBRATION"] = selection.NativeDeviceVibration ? "1" : "";
        vcEnvironment["HORIZON_UWP_GAME_LIB"] = workspaceGameLibrary;
        var hostOutput = Path.Combine(root, "native-host", "game-" + arch);
        vcEnvironment["HORIZON_UWP_BUILD_DIR"] = hostOutput;
        var hostBuildEnvironment = new Dictionary<string, string>(vcEnvironment, StringComparer.OrdinalIgnoreCase);
        if (compact is not null) foreach (var (key, value) in compact.Host) hostBuildEnvironment[key] = value;
        if (release is not null) hostBuildEnvironment["USERPROFILE"] = WinUiBootstrap.PrivateProfile;
        string executable;
        string? gameBridge = null;
        if (release is null)
        {
            progress?.Report("Strict-linking the UWP host.");
            // cmd.exe is required for the checked-in .cmd file. Its command is fixed;
            // selected paths are passed in the working directory or environment only.
            await RunProcess(cmd, snapshot, ["/d", "/c", "uwp\\host\\build-game.cmd", arch], hostBuildEnvironment);
            executable = Path.Combine(hostOutput, "HorizonUWP.exe");
        }
        else
        {
            gameBridge = Path.Combine(hostOutput, "horizon_uwp_game_bridge.dll");
            progress?.Report("Linking the selected Horizon game bridge with Rust's bundled linker.");
            await RunProcess(powershell, snapshot, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/host/strict-bridge-link.ps1",
                "-Architecture", arch,
                "-RustLibrary", workspaceGameLibrary,
                "-HostKit", Path.Combine(snapshot, "uwp", "host", "prebuilt", arch),
                "-MsvcToolsetRoot", msvcToolset!,
                "-SdkRoot", compactSdk!,
                "-RustLld", vcEnvironment["HORIZON_UWP_RUST_LLD"],
                "-SentinelObject", Path.Combine(snapshot, "uwp", "host", "prebuilt", arch, "dead-import-sentinels.obj"),
                "-Output", gameBridge], hostBuildEnvironment);
            executable = hostExecutable!;
        }
        var packageDir = Path.Combine(root, "package");
        var manifestPath = Path.Combine(snapshot, "uwp", "host", "AppxManifest.xml");
        var manifest = XDocument.Load(manifestPath);
        var identity = release is null ? null : PackageSigning.GetOrCreateIdentity();
        LocalRecipe.ConfigureManifest(manifest, selection, identity);
        manifest.Save(manifestPath);
        ResourceFramework? framework = null;
        if (selection.UseResourceFramework)
        {
            progress?.Report("Packaging a reusable resource framework from a private snapshot.");
            framework = await ResourceFrameworkPacker.PackAsync(packageResources!, Path.Combine(root, "resource-framework"), progress, identity?.Publisher, compactSdk);
        }
        string? releaseNotes = null;
        try { releaseNotes = await NightlyRelease.FetchPinnedNotesAsync(LocalRecipe.UpstreamNightlyDate, LocalRecipe.UpstreamNightlyCommit); }
        catch (HttpRequestException) { /* Use the checked-in notes for this exact source pin below. */ }
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            var pinnedNotes = Path.Combine(snapshot, "uwp", "host", "Assets",
                $"horizon-release-notes-{LocalRecipe.UpstreamNightlyDate}-{LocalRecipe.UpstreamNightlyShortCommit}.txt");
            if (!File.Exists(pinnedNotes))
                throw new FileNotFoundException("The release notes for the pinned Horizon nightly are unavailable.", pinnedNotes);
            var fallbackNotes = await File.ReadAllTextAsync(pinnedNotes);
            if (fallbackNotes.Length > 1024 * 1024 || !fallbackNotes.Contains(LocalRecipe.UpstreamNightlyCommit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The checked-in Horizon release notes do not match the pinned nightly.");
            releaseNotes = fallbackNotes;
        }
        var releaseNotesPath = Path.Combine(root, "horizon-release-notes.txt");
        await File.WriteAllTextAsync(releaseNotesPath, releaseNotes, new System.Text.UTF8Encoding(false));
        progress?.Report("Packaging the unsigned client.");
        var packageArguments = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/host/package.ps1", "-Architecture", arch,
            "-Executable", executable, "-OutputDirectory", packageDir,
            "-IdentityName", identity is null ? "HorizonUWPPrototype" : "MCHorizonUWP", "-WinUIFramework", "2.7" };
        if (gameBridge is not null) { packageArguments.Add("-GameBridge"); packageArguments.Add(gameBridge); }
        packageArguments.AddRange(LocalRecipe.ResourceArguments(selection, packageResources, framework?.Name));
        if (angleBin is not null) { packageArguments.Add("-AngleBin"); packageArguments.Add(angleBin); packageArguments.Add("-RequireAngle"); }
        packageArguments.Add("-ReleaseNotes"); packageArguments.Add(releaseNotesPath);
        if (release is not null)
        {
            var metadataPath = Path.Combine(root, "HorizonRelease.json");
            await File.WriteAllBytesAsync(metadataPath, ReleaseMetadata.Serialize(release.Patch, release.Client));
            packageArguments.Add("-ReleaseMetadataPath");
            packageArguments.Add(metadataPath);
        }
        await RunProcess(powershell, snapshot, packageArguments, compact is null ? null : hostBuildEnvironment,
            captureFailureOutput: compact is not null);
        var package = Path.Combine(packageDir, $"HorizonUWP-{arch}.appx");
        if (!File.Exists(package)) throw new InvalidOperationException("The package script did not produce the expected APPX.");
        SignedPackageOutput? signed = null;
        if (identity is not null)
        {
            progress?.Report("Signing the package and exporting its public certificate.");
            signed = await PackageOutput.CreateAsync(package, framework?.Package, identity, signTool, Path.Combine(root, "signed-output"), dependencies);
        }
        return new(root, package, packageResources is null ? null : LocalRecipe.SoundWarning(packageResources), framework?.Package, signed);
    }

    private static string RequirePrebuiltHost(string source, string architecture)
    {
        var hostRoot = Path.Combine(Path.GetFullPath(source), "uwp", "host", "prebuilt", architecture);
        var executable = Path.Combine(hostRoot, "HorizonUWP.exe");
        foreach (var path in new[] { executable, Path.Combine(hostRoot, "HorizonUWPHost.lib"),
                     Path.Combine(hostRoot, "dead-import-sentinels.obj") })
            if (!File.Exists(path)) throw new FileNotFoundException($"The authenticated client release is missing its prebuilt {architecture} host kit.", path);
        var expected = architecture switch { "x86" => (ushort)0x014c, "x64" => (ushort)0x8664,
            "arm" => (ushort)0x01c4, "arm64" => (ushort)0xaa64, _ => (ushort)0 };
        using var stream = File.OpenRead(executable);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("The prebuilt host is not a PE executable.");
        stream.Position = 0x3c;
        var peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset + 6 > stream.Length) throw new InvalidDataException("The prebuilt host has an invalid PE header.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != expected)
            throw new InvalidDataException($"The prebuilt host does not match the selected {architecture} architecture.");
        return executable;
    }

    public static async Task<string> CreateSnapshotAsync(string source, IProbe probe)
    {
        await LocalRecipe.ValidateSourceAsync(source, probe);
        // The ARM32 generated bindings contain very long paths. A short
        // user-owned root also avoids packaged-host LocalAppData redirection.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".huwp", "b", Guid.NewGuid().ToString("N")[..16]);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The new workspace already exists.");
        var snapshot = Path.Combine(root, "source");
        var archive = Path.Combine(root, "source.tar");
        Directory.CreateDirectory(snapshot);
        var git = probe.FindOnPath("git.exe") ?? throw new InvalidOperationException("Git is required to snapshot the source.");
        var environment = new Dictionary<string, string> { ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = "NUL", ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_OPTIONAL_LOCKS"] = "0" };
        await RunProcess(git, source, ["-c", "core.fsmonitor=false", "archive", "--format=tar", "--output", archive, LocalRecipe.SourceCommit], environment);
        ExtractSnapshot(archive, snapshot);
        return root;
    }

    internal static async Task<Dictionary<string, string>> VisualCppEnvironment(string cmd, TargetProfile profile, IProbe probe, bool splitRelease, string? unifiedRoot = null)
    {
        var arm = profile == TargetProfile.WindowsMobile;
        var arm64 = profile == TargetProfile.SurfaceProX;
        var x86 = profile == TargetProfile.DesktopX86;
        var root = arm ? probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)" : probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files";
        var vcvars = unifiedRoot is not null ? Path.Combine(unifiedRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat") : arm
            ? Path.Combine(root, "Microsoft Visual Studio", splitRelease ? "2022" : "2017", splitRelease ? "BuildTools" : "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat")
            : Path.Combine(root, "Microsoft Visual Studio", "18", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat");
        IReadOnlyList<string> args = arm
            ? ["/d", "/c", "call", vcvars, "x64_arm", "store", unifiedRoot is not null ? "10.0.19041.0" : splitRelease ? "10.0.17763.0" : "10.0.15063.0", "&&", "set"]
            : arm64
                ? ["/d", "/c", "call", vcvars, "x64_arm64", "store", "10.0.26100.0", "&&", "set"]
                : x86
                    ? ["/d", "/c", "call", vcvars, "x64_x86", "store", "10.0.26100.0", "&&", "set"]
                : ["/d", "/c", "call", vcvars, "x64", "&&", "set"];
        var output = await RunProcess(cmd, Path.GetDirectoryName(vcvars)!, args, captureOutput: true);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator > 0) result[line[..separator].Trim()] = line[(separator + 1)..].TrimEnd('\r');
        }
        if (!result.ContainsKey("PATH")) throw new InvalidOperationException("Visual C++ did not provide a build environment.");
        return result;
    }

    internal static async Task<string> RunProcess(string executable, string directory, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment = null, bool captureOutput = false, IProgress<string>? progress = null,
        bool captureFailureOutput = false)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetFullPath(directory), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start a required build tool.");
        var output = ReadProcessStreamAsync(process.StandardOutput, captureOutput, progress, captureFailureOutput);
        var error = ReadProcessStreamAsync(process.StandardError, captureOutput: false, progress: progress, captureFailureOutput: captureFailureOutput);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error); // Tool output is streamed only through the safe progress filter; credentials are never persisted.
        if (process.ExitCode != 0)
        {
            var diagnostics = captureFailureOutput ? FormatFailureOutput(await output, await error) : "";
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit code {process.ExitCode}); working directory: {directory}. The workspace was retained.{diagnostics}");
        }
        return captureOutput ? await output : "";
    }

    private static async Task<string> ReadProcessStreamAsync(StreamReader reader, bool captureOutput, IProgress<string>? progress, bool captureFailureOutput = false)
    {
        var captured = captureOutput ? new StringBuilder() : null;
        Queue<string>? failureTail = captureFailureOutput ? new Queue<string>() : null;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (captured is not null) captured.AppendLine(line);
            if (failureTail is not null)
            {
                failureTail.Enqueue(line);
                while (failureTail.Count > 40) failureTail.Dequeue();
            }
            if (progress is not null && IsSafeCargoProgressLine(line)) progress.Report("Cargo: " + line.Trim());
        }
        if (captured is not null) return captured.ToString();
        if (failureTail is null) return "";
        var tail = string.Join(Environment.NewLine, failureTail);
        return tail.Length <= 8_000 ? tail : tail[^8_000..];
    }

    private static string FormatFailureOutput(string standardOutput, string standardError)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(standardOutput)) sections.Add("Standard output:\n" + standardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(standardError)) sections.Add("Standard error:\n" + standardError.Trim());
        return sections.Count == 0 ? "" : "\n\nTool details:\n" + string.Join("\n", sections);
    }

    private static bool IsSafeCargoProgressLine(string line)
    {
        var value = line.TrimStart();
        return value.StartsWith("Compiling ", StringComparison.Ordinal)
            || value.StartsWith("Checking ", StringComparison.Ordinal)
            || value.StartsWith("Building ", StringComparison.Ordinal)
            || value.StartsWith("Downloading ", StringComparison.Ordinal)
            || value.StartsWith("Updating ", StringComparison.Ordinal)
            || value.StartsWith("Locking ", StringComparison.Ordinal)
            || value.StartsWith("Installing ", StringComparison.Ordinal)
            || value.StartsWith("Finished ", StringComparison.Ordinal);
    }

    private static void ExtractSnapshot(string archive, string destination)
    {
        using var input = File.OpenRead(archive);
        using var tar = new TarReader(input);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes) continue;
            if (entry.EntryType == TarEntryType.SymbolicLink && entry.Name == "CLAUDE.md" && entry.LinkName == "AGENTS.md") continue;
            if (entry.EntryType != TarEntryType.Directory && entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile)
                throw new InvalidDataException($"The pinned source archive contains an unsupported entry ({entry.EntryType}, {entry.Name}).");
            var target = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source archive entry leaves its workspace.");
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var file = new FileStream(target, FileMode.CreateNew);
            entry.DataStream?.CopyTo(file);
        }
    }

    /// Prepares sounds only in a new resource tree under the selected build snapshot.
    internal static async Task<string> PrepareSoundResourcesAsync(string snapshot, string resources, string minecraftDirectory,
        IProbe probe, IProgress<string>? progress = null, string? unifiedRoot = null, IReadOnlyDictionary<string, string>? compactEnvironment = null,
        Func<int>? requestedCargoJobs = null)
    {
        LocalRecipe.ValidateResources(resources);
        LocalRecipe.ValidateSoundPreparation(resources, minecraftDirectory);
        var root = Path.GetFullPath(snapshot);
        var destination = Path.Combine(root, ".local", "horizon-client", LocalRecipe.MinecraftVersion);
        progress?.Report("Copying the selected resources into the build workspace.");
        await Task.Run(() => StageResources(resources, destination));
        var cargo = probe.FindOnPath("cargo.exe") ?? throw new InvalidOperationException("Cargo is required to prepare sounds.");
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var hostEnvironment = compactEnvironment is null
            ? await VisualCppEnvironment(cmd, TargetProfile.DesktopX64, probe, splitRelease: false, unifiedRoot: unifiedRoot)
            : new Dictionary<string, string>(compactEnvironment, StringComparer.OrdinalIgnoreCase);
        var requested = requestedCargoJobs?.Invoke() ?? 0;
        var jobs = CurrentCargoJobCount(requested);
        hostEnvironment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        hostEnvironment["CARGO_TARGET_DIR"] = SharedCargoTargetDirectory;
        progress?.Report($"Starting Cargo compile for Horizon's resource preparer with {jobs} parallel jobs{(requested <= 0 ? " (automatic, memory-aware)" : " (selected limit)")}. The build-speed setting is now locked for this run.");
        var plan = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false, PrepareSounds: true));
        progress?.Report("Building Horizon's resource preparation tool.");
        var prepareArguments = plan.Steps.Single(step => step.Name == "Build resource preparer").Arguments
            .Select(argument => argument == ".local/builder-prepare-target" ? SharedCargoTargetDirectory : argument).ToArray();
        await RunProcess(cargo, root, prepareArguments, hostEnvironment, progress: progress);
        var preparer = Path.Combine(SharedCargoTargetDirectory, "x86_64-pc-windows-msvc", "debug", "horizon-prepare.exe");
        progress?.Report("Preparing verified sound assets from the selected Minecraft installation.");
        try
        {
            await RunProcess(preparer, root, ["sounds", "--root", root, "--minecraft-dir", Path.GetFullPath(minecraftDirectory)]);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("Sound preparation failed. Select a Minecraft installation where version 26.3 has been played and its assets are complete. " + error.Message, error);
        }
        if (LocalRecipe.SoundWarning(destination) is not null)
            throw new InvalidDataException("Sound preparation did not produce the expected files.");
        return destination;
    }

    /// Copies a selected tree to a new workspace directory without following links or overwriting files.
    internal static void StageResources(string source, string destination)
    {
        var input = Path.GetFullPath(source);
        var output = Path.GetFullPath(destination);
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Resource directory is missing.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Resource staging already exists.");
        if (ResourcesContainWorkspace(input, output)) throw new InvalidDataException("Resource staging cannot be inside the selected resources.");
        ValidateResourceTree(input);
        var pending = new Stack<(string Input, string Output)>();
        pending.Push((input, output));
        while (pending.TryPop(out var current))
        {
            if ((File.GetAttributes(current.Input) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Prepared resources contain a link.");
            Directory.CreateDirectory(current.Output);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Input))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Prepared resources contain a link.");
                var target = Path.Combine(current.Output, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) pending.Push((entry, target));
                else File.Copy(entry, target, overwrite: false);
            }
        }
    }

    private static void ValidateResourceTree(string path)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(path));
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Prepared resources contain a link; use a self-contained resource directory.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Prepared resources contain a link; use a self-contained resource directory.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    public static bool ResourcesContainWorkspace(string parent, string child)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}

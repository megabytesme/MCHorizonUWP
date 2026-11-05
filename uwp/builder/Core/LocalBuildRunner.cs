using System.Diagnostics;
using System.Formats.Tar;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record LocalBuildResult(string Workspace, string UnsignedPackage, string? SoundWarning,
    string? ResourceFrameworkPackage = null, SignedPackageOutput? SignedOutput = null);

/// <summary>Runs only the pinned local recipe in a fresh, builder-owned workspace.</summary>
public static class LocalBuildRunner
{
    public const long MinimumFreeBytes = 10L * 1024 * 1024 * 1024;
    public static int CargoJobCount(int processors, long memoryBytes)
    {
        return Math.Max(1, processors);
    }

    public static async Task<LocalBuildResult> RunAsync(string source, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null)
        => await RunCoreAsync(source, resources, selection, probe, progress, minecraftDirectory, null);

    /// <summary>Build the exact split-source recipe and sign its output. Other catalog versions need their own recipe.</summary>
    public static async Task<LocalBuildResult> RunPreparedAsync(PreparedRelease release, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null)
    {
        ValidatePreparedRelease(release, selection);
        return await RunCoreAsync(release.SourceDirectory, resources, selection, probe, progress, minecraftDirectory, release);
    }

    internal static void ValidatePreparedRelease(PreparedRelease release, BuildSelection selection)
    {
        ArgumentNullException.ThrowIfNull(release);
        var arch = selection.Target switch { TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
            TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => "" };
        if (!release.Compatibility.Allowed || release.Architecture != arch || release.BuilderVersion != "0.1.0"
            || release.Patch.Version != "0.1.0" || release.Client.Version != "0.1.0"
            || !release.Patch.UpstreamCommit.Equals(LocalRecipe.UpstreamNightlyCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The prepared release does not match this build recipe and target.");
    }

    private static async Task<LocalBuildResult> RunCoreAsync(string source, string? resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress, string? minecraftDirectory, PreparedRelease? release)
    {
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
        var signTool = Path.Combine(probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe");
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
        var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe), splitRelease: release is not null);
        if (blockers.Count != 0) throw new InvalidOperationException(string.Join(" ", blockers));
        var profileRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            ?? throw new InvalidOperationException("Could not determine the builder workspace drive.");
        if (new DriveInfo(profileRoot).AvailableFreeSpace < MinimumFreeBytes)
            throw new IOException("At least 10 GiB of free space is required on the builder workspace drive before starting a build.");

        progress?.Report(release is null ? "Creating an isolated snapshot of the pinned source revision." : "Using the authenticated release source workspace.");
        var root = release is null ? await CreateSnapshotAsync(source, probe)
            : Directory.GetParent(Path.GetFullPath(source))?.FullName ?? throw new InvalidOperationException("Invalid release workspace.");
        var snapshot = release is null ? Path.Combine(root, "source") : Path.GetFullPath(source);
        var cargo = probe.FindOnPath("cargo.exe")!;
        var python = probe.FindOnPath("python.exe")!;
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var cmd = Path.Combine(system, "cmd.exe");
        var powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");

        progress?.Report("Preparing a private copy of the Rust standard library.");
        await RunProcess(powershell, snapshot, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/rust-std/prepare-source.ps1", "-Destination", ".local/builder-rust-std", "-Toolchain", "stable"]);

        var arm = selection.Target == TargetProfile.WindowsMobile;
        if (arm)
        {
            progress?.Report("Preparing the ARM32 offline dependency overrides.");
            await RunProcess(python, snapshot, ["uwp/arm32-deps/prepare.py", "--offline-arm32"]);
        }

        progress?.Report("Preparing the Visual C++ environment.");
        var vcEnvironment = await VisualCppEnvironment(cmd, selection.Target, probe, splitRelease: release is not null);
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
        var jobs = Math.Max(1, Environment.ProcessorCount);
        vcEnvironment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        progress?.Report($"Using all {jobs} available processor cores for Cargo.");
        if (selection.Target == TargetProfile.SurfaceProX)
        {
            vcEnvironment["CC_aarch64_uwp_windows_msvc"] = Path.Combine(
                probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files", "LLVM", "bin", "clang.exe");
        }
        if (arm)
        {
            vcEnvironment["CC_thumbv7a_uwp_windows_msvc"] = Path.Combine(probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files", "LLVM", "bin", "clang.exe");
            vcEnvironment["CFLAGS_thumbv7a_uwp_windows_msvc"] = "-DWINAPI_FAMILY=WINAPI_FAMILY_APP";
        }
        progress?.Report("Fetching missing Cargo dependencies from configured registries.");
        try
        {
            await RunProcess(cargo, snapshot, plan.Steps.Single(step => step.Name == "Fetch Cargo dependencies").Arguments, vcEnvironment);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("Cargo could not fetch dependencies. Check the internet connection and configured Cargo registries. " + error.Message, error);
        }

        var packageResources = existingFramework is not null ? null : selection.PrepareSounds
            ? await PrepareSoundResourcesAsync(snapshot, resources!, minecraftDirectory!, probe, progress)
            : Path.GetFullPath(resources!);

        progress?.Report("Building the game bridge. This may take several minutes.");
        try
        {
            await RunProcess(cargo, snapshot, plan.Steps.Single(step => step.Name == "Build game bridge").Arguments, vcEnvironment);
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
        vcEnvironment["HORIZON_UWP_GAME_LIB"] = Path.Combine(snapshot, ".local", "builder-target", selection.RustTarget, "release", "horizon_uwp_game_bridge.lib");
        var hostOutput = Path.Combine(root, "native-host", "game-" + arch);
        vcEnvironment["HORIZON_UWP_BUILD_DIR"] = hostOutput;
        progress?.Report("Strict-linking the UWP host.");
        // cmd.exe is required for the checked-in .cmd file. Its command is fixed;
        // selected paths are passed in the working directory or environment only.
        await RunProcess(cmd, snapshot, ["/d", "/c", "uwp\\host\\build-game.cmd", arch], vcEnvironment);

        var executable = Path.Combine(hostOutput, "HorizonUWP.exe");
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
            framework = await ResourceFrameworkPacker.PackAsync(packageResources!, Path.Combine(root, "resource-framework"), progress, identity?.Publisher);
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
        packageArguments.AddRange(LocalRecipe.ResourceArguments(selection, packageResources, framework?.Name));
        if (angleBin is not null) { packageArguments.Add("-AngleBin"); packageArguments.Add(angleBin); packageArguments.Add("-RequireAngle"); }
        packageArguments.Add("-ReleaseNotes"); packageArguments.Add(releaseNotesPath);
        await RunProcess(powershell, snapshot, packageArguments);
        var package = Path.Combine(packageDir, $"HorizonUWP-{arch}.appx");
        if (!File.Exists(package)) throw new InvalidOperationException("The package script did not produce the expected APPX.");
        SignedPackageOutput? signed = null;
        if (identity is not null)
        {
            progress?.Report("Signing the package and exporting its public certificate.");
            signed = await PackageOutput.CreateAsync(package, framework?.Package, identity, signTool, Path.Combine(root, "signed-output"));
        }
        return new(root, package, packageResources is null ? null : LocalRecipe.SoundWarning(packageResources), framework?.Package, signed);
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

    private static async Task<Dictionary<string, string>> VisualCppEnvironment(string cmd, TargetProfile profile, IProbe probe, bool splitRelease)
    {
        var arm = profile == TargetProfile.WindowsMobile;
        var arm64 = profile == TargetProfile.SurfaceProX;
        var x86 = profile == TargetProfile.DesktopX86;
        var root = arm ? probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)" : probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files";
        var vcvars = arm
            ? Path.Combine(root, "Microsoft Visual Studio", splitRelease ? "2022" : "2017", splitRelease ? "BuildTools" : "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat")
            : Path.Combine(root, "Microsoft Visual Studio", "18", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat");
        IReadOnlyList<string> args = arm
            ? ["/d", "/c", "call", vcvars, "x64_arm", "store", splitRelease ? "10.0.17763.0" : "10.0.15063.0", "&&", "set"]
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

    private static async Task<string> RunProcess(string executable, string directory, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment = null, bool captureOutput = false)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetFullPath(directory), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start a required build tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error); // Never persist tool output, environment variables or credentials.
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit code {process.ExitCode}); working directory: {directory}. The workspace was retained.");
        return captureOutput ? await output : "";
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
        IProbe probe, IProgress<string>? progress = null)
    {
        LocalRecipe.ValidateResources(resources);
        LocalRecipe.ValidateSoundPreparation(resources, minecraftDirectory);
        var root = Path.GetFullPath(snapshot);
        var destination = Path.Combine(root, ".local", "horizon-client", LocalRecipe.MinecraftVersion);
        progress?.Report("Copying the selected resources into the build workspace.");
        await Task.Run(() => StageResources(resources, destination));
        var cargo = probe.FindOnPath("cargo.exe") ?? throw new InvalidOperationException("Cargo is required to prepare sounds.");
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var hostEnvironment = await VisualCppEnvironment(cmd, TargetProfile.DesktopX64, probe, splitRelease: false);
        hostEnvironment["CARGO_BUILD_JOBS"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var plan = LocalRecipe.Plan(new BuildSelection(TargetProfile.DesktopX64, false, PrepareSounds: true));
        progress?.Report("Building Horizon's resource preparation tool.");
        await RunProcess(cargo, root, plan.Steps.Single(step => step.Name == "Build resource preparer").Arguments, hostEnvironment);
        var preparer = Path.Combine(root, ".local", "builder-prepare-target", "x86_64-pc-windows-msvc", "debug", "horizon-prepare.exe");
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

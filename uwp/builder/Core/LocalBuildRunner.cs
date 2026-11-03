using System.Diagnostics;
using System.Formats.Tar;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record LocalBuildResult(string Workspace, string UnsignedPackage, string? SoundWarning, string? ResourceFrameworkPackage = null);

/// <summary>Runs only the pinned local recipe in a fresh, builder-owned workspace.</summary>
public static class LocalBuildRunner
{
    public const long MinimumFreeBytes = 10L * 1024 * 1024 * 1024;
    // Large Rust crates have exhausted memory when every logical CPU was used.
    // Keep as much parallelism as the machine's memory budget can sustain.
    public static int CargoJobCount(int processors, long memoryBytes) =>
        (int)Math.Clamp(memoryBytes / (4L * 1024 * 1024 * 1024), 1, Math.Max(1, processors));

    public static async Task<LocalBuildResult> RunAsync(string source, string resources, BuildSelection selection,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null)
    {
        var plan = LocalRecipe.Plan(selection);
        await LocalRecipe.ValidateSourceAsync(source, probe);
        LocalRecipe.ValidateResources(resources);
        if (selection.PrepareSounds) LocalRecipe.ValidateSoundPreparation(resources, minecraftDirectory);
        var workspaceBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huwp", "b");
        if (ResourcesContainWorkspace(resources, workspaceBase))
            throw new InvalidOperationException("Choose a resource directory that does not contain the builder workspace.");
        ValidateResourceTree(resources);
        var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe));
        if (blockers.Count != 0) throw new InvalidOperationException(string.Join(" ", blockers));
        var profileRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            ?? throw new InvalidOperationException("Could not determine the builder workspace drive.");
        if (new DriveInfo(profileRoot).AvailableFreeSpace < MinimumFreeBytes)
            throw new IOException("At least 10 GiB of free space is required on the builder workspace drive before starting a build.");

        progress?.Report("Creating an isolated snapshot of the pinned source revision.");
        var root = await CreateSnapshotAsync(source, probe);
        var snapshot = Path.Combine(root, "source");
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
        var vcEnvironment = await VisualCppEnvironment(cmd, selection.Target, probe);
        // Never inherit an unrelated client ID from the parent environment.
        vcEnvironment["HORIZON_MSA_CLIENT_ID"] = selection.ValidatedMicrosoftClientId ?? "";
        vcEnvironment["RUSTC_BOOTSTRAP"] = "1";
        vcEnvironment["__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS"] = "nightly";
        vcEnvironment["__CARGO_TESTS_ONLY_SRC_ROOT"] = Path.Combine(snapshot, ".local", "builder-rust-std", "library");
        var jobs = CargoJobCount(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        vcEnvironment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        progress?.Report($"Using {jobs} parallel Cargo jobs within the available memory budget.");
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

        var packageResources = selection.PrepareSounds
            ? await PrepareSoundResourcesAsync(snapshot, resources, minecraftDirectory!, probe, progress)
            : Path.GetFullPath(resources);

        progress?.Report("Building the game bridge. This may take several minutes.");
        try
        {
            await RunProcess(cargo, snapshot, plan.Steps.Single(step => step.Name == "Build game bridge").Arguments, vcEnvironment);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("The offline Cargo build failed. Check that the required toolchain and dependencies are available. " + error.Message, error);
        }

        var arch = arm ? "arm" : "x64";
        vcEnvironment["HORIZON_UWP_XAUDIO"] = "1";
        vcEnvironment["HORIZON_UWP_HTTP"] = "1";
        vcEnvironment["HORIZON_UWP_GAMEPAD"] = "1";
        vcEnvironment["HORIZON_UWP_ACCOUNT"] = "1";
        vcEnvironment["HORIZON_UWP_DEVICE_VIBRATION"] = selection.NativeDeviceVibration ? "1" : "";
        vcEnvironment["HORIZON_UWP_GAME_LIB"] = Path.Combine(snapshot, ".local", "builder-target", selection.RustTarget, "release", "horizon_uwp_game_bridge.lib");
        progress?.Report("Strict-linking the UWP host.");
        // cmd.exe is required for the checked-in .cmd file. Its command is fixed;
        // selected paths are passed in the working directory or environment only.
        await RunProcess(cmd, snapshot, ["/d", "/c", "uwp\\host\\build-game.cmd", arch], vcEnvironment);

        var executable = Path.Combine(snapshot, "uwp", "host", "build", "game-" + arch, "HorizonUWP.exe");
        var packageDir = Path.Combine(root, "package");
        var manifestPath = Path.Combine(snapshot, "uwp", "host", "AppxManifest.xml");
        var manifest = XDocument.Load(manifestPath);
        LocalRecipe.ConfigureManifest(manifest, selection);
        manifest.Save(manifestPath);
        ResourceFramework? framework = null;
        if (selection.UseResourceFramework)
        {
            progress?.Report("Packaging a reusable resource framework from a private snapshot.");
            framework = await ResourceFrameworkPacker.PackAsync(packageResources, Path.Combine(root, "resource-framework"), progress);
        }
        progress?.Report("Packaging the unsigned client.");
        await RunProcess(powershell, snapshot, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/host/package.ps1", "-Architecture", arch,
            "-Executable", executable, "-OutputDirectory", packageDir,
            framework is null ? "-Resources" : "-ResourceFrameworkName", framework?.Name ?? packageResources]);
        var package = Path.Combine(packageDir, $"HorizonUWP-{arch}.appx");
        if (!File.Exists(package)) throw new InvalidOperationException("The package script did not produce the expected APPX.");
        return new(root, package, LocalRecipe.SoundWarning(packageResources), framework?.Package);
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

    private static async Task<Dictionary<string, string>> VisualCppEnvironment(string cmd, TargetProfile profile, IProbe probe)
    {
        var arm = profile == TargetProfile.WindowsMobile;
        var root = arm ? probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)" : probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files";
        var vcvars = arm
            ? Path.Combine(root, "Microsoft Visual Studio", "2017", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat")
            : Path.Combine(root, "Microsoft Visual Studio", "18", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat");
        IReadOnlyList<string> args = arm
            ? ["/d", "/c", "call", vcvars, "x64_arm", "store", "10.0.15063.0", "&&", "set"]
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
        var hostEnvironment = await VisualCppEnvironment(cmd, TargetProfile.DesktopX64, probe);
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

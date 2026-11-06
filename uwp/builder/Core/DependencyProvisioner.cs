namespace Horizon.Builder.Core;

public sealed record DependencyProgress(string Id, string Detail, bool Running, bool Complete, double? Percent = null);

/// <summary>Checks and prepares shared tools plus the selected target's build requirements.</summary>
public static class DependencyProvisioner
{
    private static void ValidateArchitecture(string architecture)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64"))
            throw new ArgumentException("Unsupported target architecture.", nameof(architecture));
    }

    public static async Task<IReadOnlyList<string>> MissingAsync(IProbe probe, string architecture)
    {
        ValidateArchitecture(architecture);
        SystemProbe.RefreshProcessPath();
        var missing = new List<string>();
        var git = await FindGitAsync(probe);
        var gitMissing = git is null;
        if (gitMissing) missing.Add("Git");
        else PrivateGit.Activate(git!);
        var python = await FindPythonAsync(probe);
        if (python is null) missing.Add("Python 3.10 or newer");
        else PrivatePython.Activate(python);
        var rustupMissing = probe.FindOnPath("rustup.exe") is null;
        if (rustupMissing) missing.Add("Rustup");
        var rust = await LocalRecipe.InspectRustAsync(probe);
        if (!rust.SourceAvailable || !rust.Version.StartsWith("rustc " + LocalRecipe.RustVersion + " ", StringComparison.Ordinal))
            missing.Add($"Rust {LocalRecipe.RustVersion} and source");
        if (PrivateWindowsSdk.FindReady(probe, [architecture, "x64"]) is null)
            missing.Add($"Windows SDK 19041 for {architecture}");
        if (VisualCppInstallation.FindLinkToolset(probe, architecture) is null)
            missing.Add($"Microsoft C++ UWP link libraries for {architecture}");
        if (ClangFinder.Find(probe) is null) missing.Add("LLVM C compiler");
        var profile = WinUiBootstrap.PrivateProfile;
        var winUi = Path.Combine(profile, ".nuget", "packages", "microsoft.ui.xaml", "2.7.3");
        if (!WinUiBootstrap.IsComplete(winUi, architecture)) missing.Add("Microsoft UI framework");
        var programFiles = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        if (!FrameworkDependencies.IsAvailable(architecture, programFiles, profile)) missing.Add("Microsoft VC runtime framework");
        return missing.AsReadOnly();
    }

    public static async Task PrepareAsync(IProgress<DependencyProgress> progress, string architecture)
    {
        ValidateArchitecture(architecture);
        var probe = new SystemProbe();
        void Update(string id, string detail, bool running = false, bool complete = false, double? percent = null) => progress.Report(new(id, detail, running, complete, percent));
        SystemProbe.RefreshProcessPath();
        foreach (var (id, executable) in new[] { ("git", "git.exe"), ("python", "python.exe"), ("rustup", "rustup.exe") })
            Update(id, probe.FindOnPath(executable) is null ? "Waiting to install" : "Available", complete: probe.FindOnPath(executable) is not null);
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Downloads");
        var git = await FindGitAsync(probe);
        if (git is null)
        {
            Update("git", "Preparing a private Git runtime…", running: true);
            git = await PrivateGit.EnsureAsync([], PrivateGit.Root, downloads);
        }
        PrivateGit.Activate(git);
        Update("git", "Ready", complete: true);
        var installedPython = probe.FindOnPath("python.exe");
        var python = await FindPythonAsync(probe);
        if (python is null)
        {
            Update("python", "Preparing a private Python runtime…", running: true);
            python = await PrivatePython.EnsureAsync(installedPython, PrivatePython.Root, downloads);
        }
        PrivatePython.Activate(python);
        Update("python", "Ready", complete: true);
        if (probe.FindOnPath("rustup.exe") is null)
        {
            Update("rustup", "Preparing a private Rustup installation…", running: true);
            await PrivateRustup.InstallAsync(downloads);
        }
        Update("rustup", "Ready", complete: true);
        SystemProbe.RefreshProcessPath();
        foreach (var executable in new[] { "git.exe", "python.exe", "rustup.exe" })
            if (probe.FindOnPath(executable) is null) throw new InvalidOperationException($"{executable} was prepared but could not be found.");
        Update("rust", "Preparing the pinned compiler and Rust source…", running: true);
        await WindowsPackageManager.EnsureRustToolchainAsync(() => LocalRecipe.InspectRustAsync(probe), () => WindowsPackageManager.InstallRustToolchainAsync(probe));
        Update("rust", "Ready", complete: true);
        var profile = WinUiBootstrap.PrivateProfile;
        var signInWorkspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(signInWorkspace);

        Update("msvc", $"Checking the Microsoft link libraries for {architecture}…", running: true);
        var msvcTask = PrivateMsvcToolset.EnsureAsync(probe, architecture,
            new InlineProgress<string>(detail => Update("msvc", detail, running: true)));
        Update("sdk", $"Preparing the Windows SDK for {architecture}…", running: true);
        var system = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var installedSdk = Path.Combine(system, "Windows Kits", "10");
        var sdkProgress = new InlineProgress<string>(detail => Update("sdk", detail, running: true));
        var sdkTask = PrivateWindowsSdk.FindOrPrepareAsync(installedSdk, PrivateWindowsSdk.PrivateRoot,
            PrivateWindowsSdk.DownloadRoot, [architecture], progress: sdkProgress);
        var uiTask = WinUiBootstrap.EnsureAsync(profile, signInWorkspace, architecture,
            new InlineProgress<string>(detail => Update("ui", detail, running: true)));
        Task? llvmTask = null;
        if (ClangFinder.Find(probe) is null)
        {
            Update("llvm", "Preparing the compact C compiler…", running: true);
            llvmTask = PrivateClang.EnsureAsync(downloads,
                new InlineProgress<string>(detail => Update("llvm", detail, running: true)));
        }
        var sharedSetup = new List<Task> { msvcTask, sdkTask, uiTask };
        if (llvmTask is not null) sharedSetup.Add(llvmTask);
        await Task.WhenAll(sharedSetup);
        var sdkRoot = await sdkTask;
        Update("msvc", $"Ready for {architecture}", complete: true);
        Update("sdk", $"Ready for {architecture}", complete: true);
        Update("llvm", "Ready", complete: true);
        var signTool = Path.Combine(sdkRoot, "bin", PrivateWindowsSdk.Version, "x64", "signtool.exe");
        Update("ui", $"Ready for {architecture}", complete: true);
        Update("vclibs", $"Checking the {architecture} Microsoft VC runtime…", running: true);
        await FrameworkDependencies.FindAsync(architecture, system, profile, signTool, signInWorkspace);
        Update("vclibs", $"Ready for {architecture}", complete: true);
    }

    private static Task<string?> FindPythonAsync(IProbe probe)
    {
        var candidates = probe is SystemProbe system ? system.FindAllOnPath("python.exe")
            : probe.FindOnPath("python.exe") is { } path ? [path] : Array.Empty<string>();
        return PrivatePython.FindCandidatesAsync(candidates, PrivatePython.Root);
    }

    private static Task<string?> FindGitAsync(IProbe probe)
    {
        var candidates = probe is SystemProbe system ? system.FindAllOnPath("git.exe")
            : probe.FindOnPath("git.exe") is { } path ? [path] : Array.Empty<string>();
        return PrivateGit.FindAsync(candidates, PrivateGit.Root);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

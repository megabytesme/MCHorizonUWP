namespace Horizon.Builder.Core;

public sealed record DependencyProgress(string Id, string Detail, bool Running, bool Complete, double? Percent = null);

/// <summary>Checks and prepares the shared build requirements for every supported target.</summary>
public static class DependencyProvisioner
{
    private static readonly string[] Architectures = ["x86", "x64", "arm", "arm64"];

    public static async Task<IReadOnlyList<string>> MissingAsync(IProbe probe)
    {
        SystemProbe.RefreshProcessPath();
        var missing = new List<string>();
        if (probe.FindOnPath("winget.exe") is null) missing.Add("Windows Package Manager (WinGet)");
        if (probe.FindOnPath("git.exe") is null) missing.Add("Git");
        if (probe.FindOnPath("python.exe") is null) missing.Add("Python");
        if (probe.FindOnPath("rustup.exe") is null) missing.Add("Rustup");
        var rust = await LocalRecipe.InspectRustAsync(probe);
        if (!rust.SourceAvailable || !rust.Version.StartsWith("rustc " + LocalRecipe.RustVersion + " ", StringComparison.Ordinal))
            missing.Add($"Rust {LocalRecipe.RustVersion} and source");
        if (!VisualStudioBootstrap.IsReadyForAllTargets(probe)) missing.Add("Visual C++ UWP build tools and Windows SDKs");
        var llvm = Path.Combine(probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files", "LLVM", "bin", "clang.exe");
        if (!probe.FileExists(llvm)) missing.Add("LLVM for ARM targets");
        var profile = WinUiBootstrap.PrivateProfile;
        var winUi = Path.Combine(profile, ".nuget", "packages", "microsoft.ui.xaml", "2.7.3");
        if (Architectures.Any(arch => !WinUiBootstrap.IsComplete(winUi, arch))) missing.Add("Microsoft UI framework");
        var programFiles = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        if (Architectures.Any(arch => !FrameworkDependencies.IsAvailable(arch, programFiles, profile))) missing.Add("Microsoft VC runtime frameworks");
        return missing.AsReadOnly();
    }

    public static async Task PrepareAsync(IProgress<DependencyProgress> progress)
    {
        var probe = new SystemProbe();
        void Update(string id, string detail, bool running = false, bool complete = false, double? percent = null) => progress.Report(new(id, detail, running, complete, percent));
        SystemProbe.RefreshProcessPath();
        foreach (var (id, executable) in new[] { ("git", "git.exe"), ("python", "python.exe"), ("rustup", "rustup.exe") })
            Update(id, probe.FindOnPath(executable) is null ? "Waiting to install" : "Available", complete: probe.FindOnPath(executable) is not null);
        await WindowsPackageManager.EnsureBuildBasicsAsync(probe, async tool =>
        {
            var id = tool.ToString().ToLowerInvariant();
            Update(id, "Installing with WinGet…", running: true);
            await WindowsPackageManager.InstallAsync(probe, tool);
            SystemProbe.RefreshProcessPath();
            var executable = tool switch { InstallableTool.Git => "git.exe", InstallableTool.Python => "python.exe", InstallableTool.Rustup => "rustup.exe", _ => "" };
            if (probe.FindOnPath(executable) is null) throw new InvalidOperationException($"{tool} was installed but could not be found.");
            Update(id, "Installed", complete: true);
        }, SystemProbe.RefreshProcessPath, prepareRustup: SystemProbe.UsePrivateRustupForProcess);
        Update("rust", "Preparing the pinned compiler and Rust source…", running: true);
        await WindowsPackageManager.EnsureRustToolchainAsync(() => LocalRecipe.InspectRustAsync(probe), () => WindowsPackageManager.InstallRustToolchainAsync(probe));
        Update("rust", "Ready", complete: true);
        Update("vs", "Preparing C++ tools and Windows SDKs for all targets…", running: true);
        await VisualStudioBootstrap.EnsureAllTargetsAsync(probe, () => VisualStudioBootstrap.InstallAllTargetsAsync(probe));
        Update("vs", "Ready for all four architectures", complete: true);
        Update("llvm", "Preparing LLVM for ARM targets…", running: true);
        await WindowsPackageManager.EnsureClangAsync(probe, TargetProfile.SurfaceProX, tool => WindowsPackageManager.InstallAsync(probe, tool));
        Update("llvm", "Ready", complete: true);
        var profile = WinUiBootstrap.PrivateProfile;
        var system = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var signTool = Path.Combine(system, "Windows Kits", "10", "bin", "10.0.26100.0", "x64", "signtool.exe");
        var workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        for (var i = 0; i < Architectures.Length; i++)
        {
            var arch = Architectures[i];
            Update("ui", $"Checking the {arch} Microsoft UI framework ({i} of 4)…", running: true, percent: i * 25);
            await WinUiBootstrap.EnsureAsync(profile, workspace, arch);
        }
        Update("ui", "Ready for all four architectures", complete: true);
        for (var i = 0; i < Architectures.Length; i++)
        {
            var arch = Architectures[i];
            Update("vclibs", $"Checking the {arch} Microsoft VC runtime ({i} of 4)…", running: true, percent: i * 25);
            await FrameworkDependencies.FindAsync(arch, system, profile, signTool, workspace);
        }
        Update("vclibs", "Ready for all four architectures", complete: true);
    }
}

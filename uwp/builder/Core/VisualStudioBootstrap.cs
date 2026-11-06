using System.ComponentModel;
using System.Diagnostics;

namespace Horizon.Builder.Core;

internal sealed record VisualStudioInstallPlan(string Executable, IReadOnlyList<string> Arguments, bool Elevate);

/// <summary>Installs the supported Visual Studio 2022 UWP build tools when absent.</summary>
public static class VisualStudioBootstrap
{
    public const string PackageId = "Microsoft.VisualStudio.2022.BuildTools";
    public const string TestedVersion = "17.14.44";
    private static readonly CatalogArtifact CompactBootstrap = new(
        "https://download.visualstudio.microsoft.com/download/pr/6bd63d7f-1818-4e03-a552-9feb11496c19/5dc747350fb0589f908915c7b5fa52ec22d22b37ae2e5f4964d83519667498b2/vs_BuildTools.exe",
        "5dc747350fb0589f908915c7b5fa52ec22d22b37ae2e5f4964d83519667498b2");

    internal static IReadOnlyList<string> CompactComponents =>
    ["Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "Microsoft.VisualStudio.Component.VC.Tools.ARM", "Microsoft.VisualStudio.Component.VC.Tools.ARM64"];

    internal static IReadOnlyList<string> CompactComponentsFor(string architecture)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64"))
            throw new ArgumentException("Unsupported target architecture.", nameof(architecture));
        var components = new List<string> { "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "Microsoft.VisualStudio.ComponentGroup.UWP.VC.BuildTools" };
        if (architecture == "arm") components.Add("Microsoft.VisualStudio.Component.VC.Tools.ARM");
        if (architecture == "arm64") components.AddRange(["Microsoft.VisualStudio.Component.VC.Tools.ARM64", "Microsoft.VisualStudio.Component.UWP.VC.ARM64"]);
        return components.AsReadOnly();
    }

    internal static bool IsCompactReady(IProbe probe) => new[] { "x86", "x64", "arm", "arm64" }
        .All(arch => VisualCppInstallation.FindCompact(probe, arch) is not null);

    internal static bool IsCompactReady(IProbe probe, string architecture) =>
        VisualCppInstallation.FindLinkToolset(probe, architecture) is not null;

    internal static async Task EnsureCompactAsync(IProbe probe, string architecture, string downloads, bool includeClang, IProgress<string>? progress = null)
    {
        var componentsForTarget = CompactComponentsFor(architecture);
        if (IsCompactReady(probe, architecture) && (!includeClang || ClangFinder.Find(probe) is not null)) return;
        var programFiles = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var installed = VisualCppInstallation.FindLinkToolset(probe, architecture)?.Root;
        var root = installed ?? Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "BuildTools");
        var existing = installed is not null || !RequiresWinGet(probe);
        var requested = includeClang ? componentsForTarget.Concat(["Microsoft.VisualStudio.Component.VC.Llvm.Clang"]) : componentsForTarget;
        var components = requested.SelectMany(component => new[] { "--add", component }).ToArray();
        string executable;
        IReadOnlyList<string> arguments;
        if (existing)
        {
            executable = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "setup.exe");
            if (!probe.FileExists(executable)) throw new FileNotFoundException("The existing Build Tools installer is missing.", executable);
            // setup.exe is the installer, not the bootstrapper; it does not accept --wait.
            arguments = new[] { "modify", "--installPath", root, "--quiet", "--norestart", "--nocache" }.Concat(components).ToArray();
        }
        else
        {
            Directory.CreateDirectory(downloads);
            executable = Path.Combine(downloads, "vs_BuildTools-17.14.44.exe");
            progress?.Report("Downloading Microsoft's C++ build tools bootstrapper.");
            using (var transport = new CatalogTransport()) await PinnedDownload.EnsureAsync(transport, CompactBootstrap, executable);
            arguments = new[] { "--installPath", root, "--quiet", "--wait", "--norestart", "--nocache" }.Concat(components).ToArray();
        }
        progress?.Report("Installing the selected-architecture Microsoft link libraries; Windows may ask for administrator approval.");
        await InstallPlanAsync(new(executable, arguments, true));
        if (!IsCompactReady(probe, architecture) || (includeClang && ClangFinder.Find(probe) is null))
            throw new InvalidOperationException($"Microsoft setup finished, but the {architecture} link library components are missing.");
    }

    public static IReadOnlyList<string> Components(TargetProfile target)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        var result = new List<string> { "Microsoft.VisualStudio.Workload.VCTools",
            "Microsoft.VisualStudio.ComponentGroup.UWP.VC.BuildTools",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "Microsoft.VisualStudio.Component.Windows11SDK.26100" };
        if (target != TargetProfile.SurfaceProX) result.Add("Microsoft.VisualStudio.Component.Windows10SDK.19041");
        if (target == TargetProfile.WindowsMobile) result.Add("Microsoft.VisualStudio.Component.VC.Tools.ARM");
        if (target == TargetProfile.SurfaceProX) result.AddRange(["Microsoft.VisualStudio.Component.VC.Tools.ARM64", "Microsoft.VisualStudio.Component.UWP.VC.ARM64"]);
        return result.AsReadOnly();
    }

    public static bool IsReady(IProbe probe, TargetProfile target) =>
        VisualCppInstallation.FindForTarget(probe, target) is not null
        && new SetupChecker(probe).Scan(target, splitRelease: true, unifiedToolchain: true)
            .Any(check => check.Name == "Windows SDK and packaging tools" && check.Status == CheckStatus.Found);

    public static bool IsReadyForAllTargets(IProbe probe) => Enum.GetValues<TargetProfile>().All(target => IsReady(probe, target));

    public static IReadOnlyList<string> ComponentsForAllTargets() => Enum.GetValues<TargetProfile>()
        .SelectMany(Components).Distinct(StringComparer.Ordinal).ToArray();

    internal static bool RequiresWinGet(IProbe probe)
    {
        var programFiles = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var parent = Path.Combine(programFiles, "Microsoft Visual Studio", "2022");
        var root = Path.Combine(parent, "BuildTools");
        return !probe.Directories(parent).Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
    }

    public static async Task EnsureAllTargetsAsync(IProbe probe, Func<Task> install, IProgress<string>? progress = null)
    {
        if (IsReadyForAllTargets(probe)) return;
        progress?.Report("Installing the Microsoft C++ UWP build tools and SDKs for all four architectures. Windows may ask for administrator approval.");
        await install();
        if (!IsReadyForAllTargets(probe))
            throw new InvalidOperationException("Visual Studio setup finished, but the UWP compiler or SDK tools for one or more targets are missing. Restart Windows and try again.");
    }

    public static async Task EnsureAsync(IProbe probe, TargetProfile target, Func<TargetProfile, Task> install,
        IProgress<string>? progress = null)
    {
        if (IsReady(probe, target)) return;
        progress?.Report("Installing the Microsoft C++ UWP build tools and Windows SDKs. Windows may ask for administrator approval; this can take a while.");
        await install(target);
        if (!IsReady(probe, target))
            throw new InvalidOperationException("The Visual Studio installer finished, but the required UWP compiler or Windows SDK tools could not be found. Restart Windows and check setup.");
    }

    internal static VisualStudioInstallPlan Plan(IProbe probe, TargetProfile target) => Plan(probe, Components(target));

    internal static VisualStudioInstallPlan Plan(IProbe probe, IReadOnlyList<string> components)
    {
        var programFiles = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var parent = Path.Combine(programFiles, "Microsoft Visual Studio", "2022");
        var root = Path.Combine(parent, "BuildTools");
        var existing = !RequiresWinGet(probe);
        var componentArgs = components.SelectMany(component => new[] { "--add", component }).ToArray();
        if (existing)
        {
            var setup = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "setup.exe");
            if (!probe.FileExists(setup)) throw new FileNotFoundException("The existing Visual Studio Build Tools installation needs repair, but its installer is missing.", setup);
            // setup.exe is the installer, not the bootstrapper; it does not accept --wait.
            return new(setup, new[] { "modify", "--installPath", root, "--quiet", "--norestart", "--nocache" }.Concat(componentArgs).ToArray(), true);
        }
        var winget = probe.FindOnPath("winget.exe") ?? throw new InvalidOperationException("Windows Package Manager was not found.");
        var options = string.Join(' ', new[] { "--quiet", "--wait", "--norestart", "--nocache" }.Concat(componentArgs));
        return new(winget, new[] { "install", "--id", PackageId, "--exact", "--source", "winget", "--version", TestedVersion,
            "--override", options, "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements", "--no-upgrade" }, false);
    }

    public static Task InstallAsync(IProbe probe, TargetProfile target) => InstallPlanAsync(Plan(probe, target));

    public static Task InstallAllTargetsAsync(IProbe probe) => InstallPlanAsync(Plan(probe, ComponentsForAllTargets()));

    private static async Task InstallPlanAsync(VisualStudioInstallPlan plan)
    {
        var start = new ProcessStartInfo(plan.Executable) { UseShellExecute = plan.Elevate, CreateNoWindow = false };
        if (plan.Elevate) start.Verb = "runas";
        foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Visual Studio installer.");
            await process.WaitForExitAsync();
            if (process.ExitCode == 3010) throw new InvalidOperationException("Visual Studio setup requires a Windows restart. Restart the PC and run the builder again.");
            if (process.ExitCode != 0) throw new InvalidOperationException($"Visual Studio setup did not complete (exit code {process.ExitCode}).");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Windows administrator approval is needed to install the C++ build tools.", error);
        }
    }
}

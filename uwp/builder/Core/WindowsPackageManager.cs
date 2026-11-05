using System.Diagnostics;

namespace Horizon.Builder.Core;

public enum InstallableTool { Rustup, Git, Python, Llvm }

/// <summary>Fixed package-manager operations initiated by the user.</summary>
public static class WindowsPackageManager
{
    public static async Task EnsureBuildBasicsAsync(IProbe probe, Func<InstallableTool, Task> install,
        Action refreshPath, IProgress<string>? progress = null)
    {
        refreshPath();
        foreach (var (tool, executable) in new[] { (InstallableTool.Git, "git.exe"), (InstallableTool.Python, "python.exe") })
        {
            if (probe.FindOnPath(executable) is not null) continue;
            progress?.Report($"Installing {tool} with Windows Package Manager…");
            await install(tool);
            refreshPath();
            if (probe.FindOnPath(executable) is null)
                throw new InvalidOperationException($"{tool} was installed but could not be found. Restart the builder and try again.");
        }
    }

    public static async Task EnsureClangAsync(IProbe probe, TargetProfile target, Func<InstallableTool, Task> install,
        IProgress<string>? progress = null)
    {
        if (target is not (TargetProfile.WindowsMobile or TargetProfile.SurfaceProX)) return;
        var clang = Path.Combine(probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files", "LLVM", "bin", "clang.exe");
        if (probe.FileExists(clang)) return;
        progress?.Report("Installing LLVM for the ARM target with Windows Package Manager…");
        await install(InstallableTool.Llvm);
        if (!probe.FileExists(clang)) throw new InvalidOperationException("LLVM was installed but clang.exe was not found in its standard location.");
    }

    public static IReadOnlyList<string> Arguments(InstallableTool tool)
    {
        var package = tool switch
        {
            InstallableTool.Rustup => "Rustlang.Rustup",
            InstallableTool.Git => "Git.Git",
            InstallableTool.Python => "Python.Python.3.13",
            InstallableTool.Llvm => "LLVM.LLVM",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        var args = new List<string> { "install", "--id", package, "--exact", "--source", "winget" };
        if (tool == InstallableTool.Llvm) args.AddRange(["--version", "23.1.4"]);
        if (tool == InstallableTool.Rustup) args.Add("--interactive");
        else args.AddRange(["--silent", "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements"]);
        args.Add("--no-upgrade");
        return args.AsReadOnly();
    }

    public static async Task InstallAsync(IProbe probe, InstallableTool tool)
    {
        var arguments = Arguments(tool);
        var executable = probe.FindOnPath("winget.exe")
            ?? throw new InvalidOperationException("Windows Package Manager was not found. Open the publisher instructions or install App Installer first.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows Package Manager.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows Package Manager did not complete (exit code {process.ExitCode}).");
    }
}

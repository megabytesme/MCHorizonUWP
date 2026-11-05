using System.Diagnostics;

namespace Horizon.Builder.Core;

public enum InstallableTool { Rustup, Git, Python }

/// <summary>Fixed package-manager operations initiated by the user.</summary>
public static class WindowsPackageManager
{
    public static IReadOnlyList<string> Arguments(InstallableTool tool)
    {
        var package = tool switch
        {
            InstallableTool.Rustup => "Rustlang.Rustup",
            InstallableTool.Git => "Git.Git",
            InstallableTool.Python => "Python.Python.3.13",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        var args = new List<string> { "install", "--id", package, "--exact", "--source", "winget" };
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

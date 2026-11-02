using System.Diagnostics;

namespace Horizon.Builder.Core;

/// <summary>Fixed, interactive package-manager operations initiated by the user.</summary>
public static class WindowsPackageManager
{
    public static IReadOnlyList<string> RustupArguments { get; } = Array.AsReadOnly(new[]
    {
        "install", "--id", "Rustlang.Rustup", "--exact", "--source", "winget", "--interactive", "--no-upgrade",
    });

    public static async Task InstallRustupAsync(IProbe probe)
    {
        var executable = probe.FindOnPath("winget.exe")
            ?? throw new InvalidOperationException("Windows Package Manager was not found. Open the official Rust instructions above or install App Installer first.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = false };
        foreach (var argument in RustupArguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows Package Manager.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows Package Manager did not complete (exit code {process.ExitCode}).");
    }
}

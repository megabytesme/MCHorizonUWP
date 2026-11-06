using System.Diagnostics;

namespace Horizon.Builder.Core;

/// <summary>Restores Rust's pinned bootstrapper directly into the builder-owned tool profile.</summary>
internal static class PrivateRustup
{
    private const string FileName = "rustup-init-1.29.3-x86_64.exe";
    private static readonly CatalogArtifact Bootstrap = new(
        "https://static.rust-lang.org/rustup/archive/1.29.3/x86_64-pc-windows-msvc/rustup-init.exe",
        "38027d51d69a5d05493627a0634dbea52d6cd793c8f072d2373d3f4163eec7db");

    internal static async Task InstallAsync(string downloads)
    {
        Directory.CreateDirectory(downloads);
        var executable = Path.Combine(downloads, FileName);
        using (var transport = new CatalogTransport()) await PinnedDownload.EnsureAsync(transport, Bootstrap, executable);
        SystemProbe.UsePrivateRustupForProcess();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-y", "--no-modify-path", "--default-toolchain", "none", "--profile", "minimal" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Rustup bootstrapper.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Rustup setup did not complete (exit code {process.ExitCode}).");
        SystemProbe.RefreshProcessPath();
        if (new SystemProbe().FindOnPath("rustup.exe") is null) throw new InvalidOperationException("Rustup finished but could not be found in its private profile.");
    }
}

using System.Diagnostics;
using System.IO.Compression;

namespace Horizon.Builder.Core;

/// <summary>Finds a suitable Python or restores Python.org's private embedded runtime.</summary>
internal static class PrivatePython
{
    private const string FileName = "python-3.13.17-embed-amd64.zip";
    private static readonly CatalogArtifact Package = new("https://www.python.org/ftp/python/3.13.17/" + FileName,
        "9e35319bffedab8b36ad44d8f377cb4f1733bcec257a0b6d04d94f01486ddb38");
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Python", "3.13.17-x64");

    internal static async Task<bool> IsUsableAsync(string? executable)
    {
        if (executable is null || !File.Exists(executable)) return false;
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-I");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import argparse, json, os, pathlib, re, shutil, subprocess, sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)");
            using var process = Process.Start(start);
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return false; }
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { return false; }
    }

    internal static async Task<string?> FindAsync(string? installed, string root)
        => await FindCandidatesAsync(installed is null ? [] : [installed], root);

    internal static async Task<string?> FindCandidatesAsync(IEnumerable<string> installed, string root)
    {
        foreach (var candidate in installed)
            if (await IsUsableAsync(candidate)) return Path.GetFullPath(candidate);
        var local = Path.Combine(root, "python.exe");
        return await IsUsableAsync(local) ? Path.GetFullPath(local) : null;
    }

    internal static void Activate(string executable)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var parts = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        parts.RemoveAll(part => string.Equals(part.Trim().Trim('"'), directory, StringComparison.OrdinalIgnoreCase));
        parts.Insert(0, directory);
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, parts));
    }

    internal static async Task<string> EnsureAsync(string? installed, string root, string downloads)
    {
        var existing = await FindAsync(installed, root);
        if (existing is not null) return existing;
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The private Python installation is incomplete; it was left unchanged.");
        Directory.CreateDirectory(downloads);
        var archive = Path.Combine(downloads, FileName);
        using (var transport = new CatalogTransport()) await PinnedDownload.EnsureAsync(transport, Package, archive);
        var parent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".python-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName;
                    if (name.Contains('\\') || name.Contains('/') || name.Contains(':') || name is "" or "." or ".."
                        || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000))
                        throw new InvalidDataException("Unsafe embedded Python package entry.");
                    if (entry.Length < 0 || entry.Length > 128L * 1024 * 1024 - total) throw new InvalidDataException("Embedded Python package is too large.");
                    total += entry.Length;
                    using var source = entry.Open();
                    using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew);
                    source.CopyTo(output);
                }
            }
            if (!await IsUsableAsync(Path.Combine(staging, "python.exe"))) throw new InvalidDataException("The embedded Python package could not run.");
            Directory.Move(staging, root);
            return Path.Combine(root, "python.exe");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
}

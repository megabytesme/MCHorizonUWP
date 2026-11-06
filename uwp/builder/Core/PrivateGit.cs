using System.Diagnostics;
using System.IO.Compression;

namespace Horizon.Builder.Core;

/// <summary>Reuses Git for Windows or restores a pinned private MinGit distribution.</summary>
internal static class PrivateGit
{
    private const string FileName = "MinGit-2.56.0.4-busybox-64-bit.zip";
    private static readonly CatalogArtifact Package = new(
        "https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.4/" + FileName,
        "c4f27d9356ef471dd9fea1f89e5c176cfff7c85b29912a00f3cc7fb59197a39f");
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCHorizonUWP", "Git", "2.56.0.4-x64");

    internal static async Task<bool> IsUsableAsync(string? executable)
    {
        if (executable is null || !File.Exists(executable)) return false;
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return false; }
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { return false; }
    }

    internal static async Task<string?> FindAsync(IEnumerable<string> installed, string root)
    {
        foreach (var candidate in installed)
            if (await IsUsableAsync(candidate)) return Path.GetFullPath(candidate);
        var local = Path.Combine(root, "cmd", "git.exe");
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

    internal static async Task<string> EnsureAsync(IEnumerable<string> installed, string root, string downloads)
    {
        var existing = await FindAsync(installed, root);
        if (existing is not null) return existing;
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The private Git installation is incomplete; it was left unchanged.");
        Directory.CreateDirectory(downloads);
        var archive = Path.Combine(downloads, FileName);
        using (var transport = new CatalogTransport()) await PinnedDownload.EnsureAsync(transport, Package, archive);
        var parent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".git-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 10_000) throw new InvalidDataException("MinGit has too many archive entries.");
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName;
                    var parts = name.TrimEnd('/').Split('/');
                    if (name.StartsWith('/') || name.Contains('\\') || parts.Any(part => part is "" or "." or ".." || part.Contains(':'))
                        || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000 or 0x4000))
                        throw new InvalidDataException("Unsafe MinGit package entry.");
                    if (entry.Length < 0 || entry.Length > 256L * 1024 * 1024 - total) throw new InvalidDataException("MinGit package is too large.");
                    total += entry.Length;
                    var path = Path.GetFullPath(Path.Combine([staging, .. parts]));
                    if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MinGit entry leaves its destination.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var source = entry.Open();
                    using var output = new FileStream(path, FileMode.CreateNew);
                    source.CopyTo(output);
                }
            }
            if (!await IsUsableAsync(Path.Combine(staging, "cmd", "git.exe")) || !File.Exists(Path.Combine(staging, "LICENSE.txt")))
                throw new InvalidDataException("The MinGit package is incomplete.");
            Directory.Move(staging, root);
            return Path.Combine(root, "cmd", "git.exe");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
}

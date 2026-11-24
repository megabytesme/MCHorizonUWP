using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Assembles already authenticated and downloaded release inputs in a new directory.</summary>
public static class SourceAssembler
{
    private const long MaxExpandedBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxEntries = 100_000;

    public static async Task AssembleAsync(string upstreamZip, string expectedUpstreamHash, string patch,
        string expectedPatchHash, string clientZip, string expectedClientHash, string output, string git,
        string? upstreamCommit = null, string? clientVersion = null)
    {
        await VerifyAsync(upstreamZip, expectedUpstreamHash);
        await VerifyAsync(patch, expectedPatchHash);
        await VerifyAsync(clientZip, expectedClientHash);
        var root = Path.GetFullPath(output);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The source workspace already exists.");
        Directory.CreateDirectory(root);
        Extract(upstreamZip, root, upstream: true);
        await ApplyPatchAsync(git, root, patch, check: true);
        await ApplyPatchAsync(git, root, patch, check: false);
        var bundle = Path.Combine(root, "uwp", "wgpu-dx11");
        var bridgeRecovery = Path.Combine(bundle, "bridge-feature-after-client.patch");
        var nativeRecovery = Path.Combine(bundle, "dx12-uwp-hdr-recovery.patch");
        var hdrRecoveryRecipe = File.Exists(bridgeRecovery) || File.Exists(nativeRecovery);
        Extract(clientZip, root, upstream: false);
        if (hdrRecoveryRecipe)
        {
            if (!string.Equals(upstreamCommit, NightlyUwp.November20Commit, StringComparison.OrdinalIgnoreCase)
                || !NightlyUwp.UsesHdrWgpu(clientVersion))
                throw new InvalidDataException("The HDR recovery patches require the November 20 HDR source and client recipe.");
            await VerifyAsync(bridgeRecovery, NightlyUwp.HdrBridgeFeaturePatchHash);
            await VerifyAsync(nativeRecovery, NightlyUwp.HdrWgpuRecoveryPatchHash);
            // The client ZIP owns the bridge manifest. Apply the renderer feature after extraction.
            var manifest = Path.Combine(root, "uwp", "game-bridge", "Cargo.toml");
            await VerifyAsync(manifest, NightlyUwp.HdrBridgeManifestHash);
            await ApplyPatchAsync(git, root, bridgeRecovery, check: true);
            await ApplyPatchAsync(git, root, bridgeRecovery, check: false);
            await VerifyAsync(manifest, NightlyUwp.HdrBridgeConfiguredManifestHash);
        }
        if (NightlyUwp.Matches(upstreamCommit))
        {
            var fixedWgpu = NightlyUwp.UsesFixedWgpu(clientVersion);
            await StageNightlyWgpuAsync(root, Path.Combine(bundle, "wgpu.zip"),
                NightlyUwp.UsesHdrWgpu(clientVersion) ? Path.Combine(bundle, "dx12-uwp-hdr-surface.patch") :
                    fixedWgpu ? null : Path.Combine(bundle, "reuse-upload-buffer-shadow.patch"), git,
                fixedWgpu ? NightlyUwp.FixedWgpuArchiveHash : NightlyUwp.WgpuArchiveHash,
                NightlyUwp.UsesHdrWgpu(clientVersion) ? NightlyUwp.HdrWgpuPatchHash : NightlyUwp.WgpuPatchHash);
            if (hdrRecoveryRecipe)
            {
                var destination = Path.Combine(root, "third_party", "wgpu-dx11");
                await ApplyPatchAsync(git, destination, nativeRecovery, check: true);
                await ApplyPatchAsync(git, destination, nativeRecovery, check: false);
            }
        }
    }

    internal static async Task StageNightlyWgpuAsync(string source, string archive, string? patch, string git,
        string expectedArchiveHash = NightlyUwp.WgpuArchiveHash, string expectedPatchHash = NightlyUwp.WgpuPatchHash)
    {
        await VerifyAsync(archive, expectedArchiveHash);
        if (patch is not null) await VerifyAsync(patch, expectedPatchHash);
        var destination = Path.Combine(Path.GetFullPath(source), "third_party", "wgpu-dx11");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The Nightly wgpu source destination already exists.");
        Directory.CreateDirectory(destination);
        Extract(archive, destination, upstream: true);
        if (patch is not null)
        {
            await ApplyPatchAsync(git, destination, patch, check: true);
            await ApplyPatchAsync(git, destination, patch, check: false);
        }
    }

    private static async Task VerifyAsync(string path, string expected)
    {
        if (expected is null || expected.Length != 64 || !expected.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The expected input digest is invalid.");
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A downloaded input does not match its expected digest.");
    }

    private static void Extract(string archive, string output, bool upstream)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("The archive contains too many entries.");
        var root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        string? prefix = null;
        foreach (var entry in zip.Entries)
        {
            var raw = entry.FullName;
            if (raw.Contains('\\') || raw.StartsWith('/') || raw.Contains(':')) throw new InvalidDataException("Invalid archive path.");
            var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(part => part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
                throw new InvalidDataException("Invalid archive path.");
            if (parts.Length == 0) continue;
            if (upstream)
            {
                prefix ??= parts[0];
                if (parts[0] != prefix) throw new InvalidDataException("The upstream archive has multiple roots.");
                parts = parts[1..];
            }
            else if (parts[0] == "notices") continue;
            else if (parts[0] != "uwp") throw new InvalidDataException("The client archive contains a file outside the UWP directory.");
            if (parts.Length == 0) continue;
            var relative = Path.Combine(parts);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !names.Add(target))
                throw new InvalidDataException("The archive has an escaping or duplicate path.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
            {
                // This upstream documentation symlink is not needed for a build.
                if (upstream && relative == "CLAUDE.md" && entry.Length == 9)
                {
                    using var reader = new StreamReader(entry.Open());
                    if (reader.ReadToEnd() == "AGENTS.md") continue;
                }
                throw new InvalidDataException("The archive contains an unsupported symbolic link.");
            }
            if (unixType != 0 && unixType != 0x8000 && unixType != 0x4000)
                throw new InvalidDataException("The archive contains an unsupported file type.");
            if (raw.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            if (entry.Length > MaxExpandedBytes - total) throw new InvalidDataException("The archive expands beyond the allowed size.");
            total += entry.Length;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var mode = FileMode.CreateNew;
            if (!upstream && (File.Exists(target) || Directory.Exists(target)))
            {
                var attributes = File.GetAttributes(target);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new InvalidDataException("The client archive cannot replace a directory or link in the source tree.");
                mode = FileMode.Create;
            }
            using var input = entry.Open();
            using var file = new FileStream(target, mode, FileAccess.Write, FileShare.None);
            input.CopyTo(file);
        }
    }

    private static async Task ApplyPatchAsync(string git, string output, string patch, bool check)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(git)) { WorkingDirectory = output, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-c", "core.fsmonitor=false", "-c", "core.autocrlf=true", "apply", "--binary", "--whitespace=nowarn" }) start.ArgumentList.Add(arg);
        if (check) start.ArgumentList.Add("--check");
        start.ArgumentList.Add("--"); start.ArgumentList.Add(Path.GetFullPath(patch));
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // Apply paths relative to the extracted source, including when it lives inside a Git worktree.
        start.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(output)!;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidDataException($"The patch could not be {(check ? "validated" : "applied")} (Git exit code {process.ExitCode}).");
    }
}

using System.Formats.Tar;
using System.Security.Cryptography;
using ZstdSharp;

namespace Horizon.Builder.Core;

/// <summary>Restores only Clang and its builtin headers from LLVM's pinned Windows archive.</summary>
internal static class PrivateClang
{
    private const string Version = "23.1.5";
    internal const string ArchiveRoot = "clang+llvm-23.1.5-x86_64-pc-windows-msvc";
    private const string FileName = ArchiveRoot + ".tar.zst";
    private const string ArchiveSha256 = "b2be33385ddc86e83511417a060de927ae8b09d1f3017cc3cdce5c73cd5029d7";
    // LLVM's published frame advertises a 1 GiB Zstandard window. Keep the decoder capped
    // to that exact bound rather than accepting arbitrary window sizes from an archive.
    private const int MaxZstdWindowLog = 30;
    private const long MaxExpandedBytes = 224L * 1024 * 1024;
    private static readonly CatalogArtifact Archive = new(
        "https://github.com/llvm/llvm-project/releases/download/llvmorg-23.1.5/clang%2Bllvm-23.1.5-x86_64-pc-windows-msvc.tar.zst",
        ArchiveSha256);

    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "LLVM", Version);
    internal static string ClangExecutable => Path.Combine(Root, "bin", "clang.exe");
    internal static string DownloadRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Downloads", "LLVM");

    internal static bool IsComplete(string root, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return exists(Path.Combine(root, "bin", "clang.exe"))
            && exists(Path.Combine(root, "bin", "llvm-lib.exe"))
            && exists(Path.Combine(root, "lib", "clang", "23", "include", "stddef.h"));
    }

    internal static async Task<string> EnsureAsync(string downloads, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ClangFinder.Find(new SystemProbe()) is { } available) return available;
        var destination = Path.GetFullPath(Root);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The private Clang folder is incomplete; it was left unchanged.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, ".clang-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var archivePath = Path.Combine(downloads, FileName);
            Directory.CreateDirectory(downloads);
            progress?.Report("Downloading the compact, pinned Clang package…");
            using (var transport = new CatalogTransport()) await PinnedDownload.EnsureAsync(transport, Archive, archivePath, cancellationToken);
            if (new FileInfo(archivePath).Length > 300L * 1024 * 1024)
                throw new InvalidDataException("The pinned Clang archive exceeds its size limit.");
            progress?.Report("Extracting Clang and its builtin headers…");
            await ExtractAsync(archivePath, staging, cancellationToken);
            if (!IsComplete(staging)) throw new InvalidDataException("The compact Clang package is incomplete.");
            await File.WriteAllTextAsync(Path.Combine(staging, "mchorizon-clang-version.txt"), Version + "\n", cancellationToken);
            Directory.Move(staging, destination);
            return ClangExecutable;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var decompressor = new Decompressor();
        decompressor.SetParameter(ZstdSharp.Unsafe.ZSTD_dParameter.ZSTD_d_windowLogMax, MaxZstdWindowLog);
        using var zstd = new DecompressionStream(file, decompressor);
        using var reader = new TarReader(zstd);
        var rootPrefix = ArchiveRoot + "/";
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var entries = 0;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > 20_000) throw new InvalidDataException("The Clang archive contains too many entries.");
            var name = entry.Name.TrimEnd('/');
            if (name == ArchiveRoot) continue;
            if (!name.StartsWith(rootPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("The Clang archive has an unexpected root directory.");
            var relative = name[rootPrefix.Length..];
            if (relative.Length == 0 || relative.Contains('\\') || relative.StartsWith('/') || relative.Contains(':')
                || relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("The Clang archive contains an unsafe path.");
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile))
                continue;
            var isCompiler = relative is "bin/clang.exe" or "bin/llvm-lib.exe";
            var isResourceHeader = relative.StartsWith("lib/clang/23/include/", StringComparison.Ordinal);
            if (!isCompiler && !isResourceHeader) continue;
            if (entry.DataStream is null) throw new InvalidDataException("A Clang archive entry has no data.");
            if (entry.Length < 0 || entry.Length > MaxExpandedBytes - expanded)
                throw new InvalidDataException("The selected Clang files exceed the size limit.");
            expanded += entry.Length;
            var target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase) || !files.Add(target))
                throw new InvalidDataException("A Clang archive entry escapes or duplicates its destination.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
        if (!File.Exists(Path.Combine(destination, "bin", "clang.exe")))
            throw new InvalidDataException("The Clang executable was not found in the pinned archive.");
    }
}

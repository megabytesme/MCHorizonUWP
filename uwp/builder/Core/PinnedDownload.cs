using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Reuse verified downloads; preserve a damaged cache entry and download it again.</summary>
internal static class PinnedDownload
{
    internal static async Task EnsureWorkspaceCopyAsync(CatalogTransport transport, CatalogArtifact artifact, string destination,
        CancellationToken cancellationToken = default, string? cacheRoot = null)
    {
        if (artifact?.Sha256 is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid catalog artifact digest.");
        cacheRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCHorizonUWP", "Cache", "ReleaseArtifacts");
        Directory.CreateDirectory(cacheRoot);
        var cache = Path.Combine(cacheRoot, digest.ToLowerInvariant() + ".bin");
        // The persistent lock file serializes separate Builder processes as well as tasks.
        await using var lease = await AcquireLeaseAsync(cache + ".lock", cancellationToken);
        {
            await EnsureAsync(transport, artifact, cache, cancellationToken);
            var created = false;
            try
            {
                await using var input = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                created = true;
                await input.CopyToAsync(output, cancellationToken);
            }
            catch
            {
                if (created) File.Delete(destination);
                throw;
            }
        }
    }

    internal static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(100, cancellationToken); }
        }
    }

    internal static async Task EnsureAsync(CatalogTransport transport, CatalogArtifact artifact, string destination,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destination))
        {
            string actual;
            await using (var input = File.OpenRead(destination)) actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            if (actual.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)) return;
            var quarantine = destination + ".invalid-" + Guid.NewGuid().ToString("N");
            File.Move(destination, quarantine);
        }
        await transport.DownloadArtifactAsync(artifact, destination, cancellationToken);
    }
}

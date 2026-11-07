using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace Horizon.Builder.Core;

/// <summary>Reuse verified downloads; preserve a damaged cache entry and download it again.</summary>
internal static class PinnedDownload
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    internal static async Task EnsureAsync(CatalogTransport transport, CatalogArtifact artifact, string destination,
        CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(destination), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { await EnsureLockedAsync(transport, artifact, destination, cancellationToken); }
        finally { gate.Release(); }
    }

    internal static async Task EnsureWorkspaceCopyAsync(CatalogTransport transport, CatalogArtifact artifact, string destination,
        CancellationToken cancellationToken = default)
    {
        if (artifact.Sha256 is null || artifact.Sha256.Length != 64 || !artifact.Sha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid catalog artifact digest.");
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCHorizonUWP", "Cache", "ReleaseArtifacts", artifact.Sha256.ToLowerInvariant() + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        await EnsureAsync(transport, artifact, cache, cancellationToken);
        File.Copy(cache, destination, overwrite: false);
    }

    private static async Task EnsureLockedAsync(CatalogTransport transport, CatalogArtifact artifact, string destination,
        CancellationToken cancellationToken)
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

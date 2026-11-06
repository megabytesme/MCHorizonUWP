using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Reuse verified downloads; preserve a damaged cache entry and download it again.</summary>
internal static class PinnedDownload
{
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

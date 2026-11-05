namespace Horizon.Builder.Core;

public sealed class PreparedRelease
{
    internal PreparedRelease(string sourceDirectory, CatalogPatch patch, CatalogClient client, CompatibilityResult compatibility,
        string builderVersion, string architecture)
        => (SourceDirectory, Patch, Client, Compatibility, BuilderVersion, Architecture) =
            (sourceDirectory, patch, client, compatibility, builderVersion, architecture);

    public string SourceDirectory { get; }
    public CatalogPatch Patch { get; }
    public CatalogClient Client { get; }
    public CompatibilityResult Compatibility { get; }
    public string BuilderVersion { get; }
    public string Architecture { get; }
}

/// <summary>Authenticates, selects and assembles a release in a new workspace.</summary>
public static class ReleaseSource
{
    public static async Task<PreparedRelease> PrepareAsync(CatalogTransport transport, Uri catalogUri, Uri signatureUri,
        byte[] trustedPublicKey, string patchVersion, string clientVersion, string builderVersion, string architecture,
        string workspace, string git, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var catalog = await transport.GetCatalogAsync(catalogUri, signatureUri, trustedPublicKey, cancellationToken);
        var compatibility = catalog.Check(patchVersion, clientVersion, builderVersion, architecture);
        if (!compatibility.Allowed) throw new InvalidOperationException(compatibility.Reason);
        var patch = catalog.Patches.Single(item => item.Version == patchVersion);
        var client = catalog.Clients.Single(item => item.Version == clientVersion);
        var root = Path.GetFullPath(workspace);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The release workspace already exists.");
        Directory.CreateDirectory(root);
        var upstreamFile = Path.Combine(root, "upstream.zip");
        var patchFile = Path.Combine(root, "patch.diff");
        var clientFile = Path.Combine(root, "client.zip");
        await transport.DownloadArtifactAsync(patch.Upstream, upstreamFile, cancellationToken);
        await transport.DownloadArtifactAsync(patch.Artifact, patchFile, cancellationToken);
        await transport.DownloadArtifactAsync(client.Artifact, clientFile, cancellationToken);
        var source = Path.Combine(root, "source");
        await SourceAssembler.AssembleAsync(upstreamFile, patch.Upstream.Sha256, patchFile, patch.Artifact.Sha256,
            clientFile, client.Artifact.Sha256, source, git);
        return new(source, patch, client, compatibility, builderVersion, architecture);
    }
}

namespace Horizon.Builder.Core;

/// <summary>The public key and fixed HTTPS location for the maintainer-signed release catalog.</summary>
public static class CatalogTrust
{
    public const string BuilderVersion = "1.0.1";
    public static readonly Uri CatalogUri = new("https://raw.githubusercontent.com/megabytesme/MCHorizonUWP/main/catalog/catalog-v1.json");
    public static readonly Uri SignatureUri = new("https://raw.githubusercontent.com/megabytesme/MCHorizonUWP/main/catalog/catalog-v1.sig");

    public static byte[] PublicKey()
    {
        using var input = typeof(CatalogTrust).Assembly.GetManifestResourceStream("Horizon.Builder.Core.CatalogPublicKey.der")
            ?? throw new InvalidOperationException("The release catalog verification key is missing.");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    public static Task<CompatibilityCatalog> FetchAsync(CatalogTransport transport, CancellationToken cancellationToken = default)
        => transport.GetCatalogAsync(CatalogUri, SignatureUri, PublicKey(), cancellationToken);
}

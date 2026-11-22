namespace Horizon.Builder.Core;

/// <summary>The public key and fixed HTTPS location for the maintainer-signed release catalog.</summary>
public static class CatalogTrust
{
    public const string BuilderVersion = "1.0.6";
    private const string RetainedRecipeVersion = "1.0.3";
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

    public static CompatibilityResult CheckRecipe(CompatibilityCatalog catalog, string patchVersion, string clientVersion,
        string builderVersion, string architecture)
    {
        var current = catalog.Check(patchVersion, clientVersion, builderVersion, architecture);
        if (current.Allowed || builderVersion != BuilderVersion) return current;
        var patch = catalog.Patches.SingleOrDefault(item => item.Version == patchVersion);
        var client = catalog.Clients.SingleOrDefault(item => item.Version == clientVersion);
        if (patch is null || client is null || !UsesRetainedRecipe(patch, client)) return current;
        var retained = catalog.Check(patchVersion, clientVersion, RetainedRecipeVersion, architecture);
        return retained.Allowed
            ? new(true, false, false, "The signed catalog allows this release with the retained Builder 1.0.3 recipe.")
            : current;
    }

    // Only the two previously released input sets use the retained recipe.
    // The signed catalog still controls eligibility for each architecture.
    private static bool UsesRetainedRecipe(CatalogPatch patch, CatalogClient client)
    {
        static bool EqualsHex(string actual, string expected) => actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        return (patch.Version, client.Version) switch
        {
            ("0.1.1", "0.1.3") =>
                EqualsHex(patch.UpstreamCommit, "f42ef42ef6ceccb3c6a20bea2481d87949a51c81") &&
                EqualsHex(patch.Upstream.Sha256, "97205ce67c81d1bbfb754aed11185d779dbfa114cdb9898c0ad0b8adf9851467") &&
                EqualsHex(patch.Artifact.Sha256, "76b7dcee0ddad1bdf5d9a7da2461f7c528e24fa6a25d48773d27e30e81e26fa8") &&
                EqualsHex(client.Artifact.Sha256, "1424f087aae9527573737fbb3325f9728040ed03d3b6301a37107ac44ede656f"),
            ("0.1.2", "0.1.4") =>
                EqualsHex(patch.UpstreamCommit, "e797c725ad99ef311db216de7f41a2e6593353db") &&
                EqualsHex(patch.Upstream.Sha256, "1b63454061bce2f5e790342ae65c5553dcdeaef9e3b36fb1d0921a9a773b46a9") &&
                EqualsHex(patch.Artifact.Sha256, "ca6a5f41955b0598daaf8f48ded916d9cb5990b9923ecdf47c232cead1d50f31") &&
                EqualsHex(client.Artifact.Sha256, "6bcdf11502135c5bb8f3c78f027d903e273abeb68584cd52f23fc6ff760b82a3"),
            _ => false,
        };
    }
}

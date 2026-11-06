using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Release identifiers stamped into each catalog-built APPX for its own update checks.</summary>
public sealed record ReleaseMetadata(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("patch_version")] string PatchVersion,
    [property: JsonPropertyName("client_version")] string ClientVersion,
    [property: JsonPropertyName("upstream_commit")] string UpstreamCommit)
{
    private static readonly Regex VersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

    public static ReleaseMetadata Create(CatalogPatch patch, CatalogClient client)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(client);
        return Create(patch.Version, client.Version, patch.UpstreamCommit);
    }

    public static ReleaseMetadata Create(string patchVersion, string clientVersion, string upstreamCommit)
    {
        if (!IsVersion(patchVersion) || !IsVersion(clientVersion))
            throw new ArgumentException("Release metadata versions must have three numeric components.");
        if (upstreamCommit is null || upstreamCommit.Length != 40 || !upstreamCommit.All(Uri.IsHexDigit))
            throw new ArgumentException("Release metadata needs a 40-character upstream commit.", nameof(upstreamCommit));
        return new ReleaseMetadata(1, patchVersion, clientVersion, upstreamCommit.ToLowerInvariant());
    }

    public static byte[] Serialize(CatalogPatch patch, CatalogClient client)
        => JsonSerializer.SerializeToUtf8Bytes(Create(patch, client));

    public static bool IsVersion(string? value)
        => value is not null && VersionPattern.IsMatch(value)
            && Version.TryParse(value, out _);
}

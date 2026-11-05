using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.Builder.Core;

// Catalog parsing and selection only. Callers must authenticate the catalog
// and verify each artifact before using it; this class does not fetch code.
public sealed record CatalogArtifact(string Url, string Sha256);
public sealed record CatalogPatch(string Version, string UpstreamCommit, int Bridge, CatalogArtifact Upstream, CatalogArtifact Artifact);
public sealed record CatalogClient(string Version, int Bridge, CatalogArtifact Artifact);
public sealed record CatalogRange(string MinInclusive, string MaxExclusive);
public sealed record CatalogRule(CatalogRange Patch, CatalogRange Client, CatalogRange Builder, string[] Architectures);
public sealed record CatalogEvidence(string Patch, string Client, string Builder, string Architecture, bool BuildPassed, bool DevicePassed);
public sealed record CompatibilityResult(bool Allowed, bool BuildVerified, bool DeviceVerified, string Reason);

public sealed record CompatibilityCatalog(int SchemaVersion, CatalogPatch[] Patches, CatalogClient[] Clients,
    CatalogRule[] Rules, CatalogEvidence[] Evidence)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CompatibilityCatalog Parse(string json)
    {
        if (json.Length > 2_000_000) throw new InvalidDataException("The catalog is too large.");
        CompatibilityCatalog catalog;
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            catalog = JsonSerializer.Deserialize<CompatibilityCatalog>(json, JsonOptions) ?? throw new InvalidDataException("The catalog is empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("The catalog is invalid.", error); }
        catalog.Validate();
        return catalog;
    }

    public CompatibilityResult Check(string patchVersion, string clientVersion, string builderVersion, string architecture)
    {
        var patch = Patches.SingleOrDefault(p => p.Version == patchVersion);
        var client = Clients.SingleOrDefault(c => c.Version == clientVersion);
        if (patch is null || client is null) return new(false, false, false, "The selected release is not in the catalog.");
        if (patch.Bridge != client.Bridge) return new(false, false, false, "The UWP bridge versions differ.");
        if (!Rules.Any(rule => Contains(rule.Patch, patchVersion) && Contains(rule.Client, clientVersion)
            && Contains(rule.Builder, builderVersion) && rule.Architectures.Contains(architecture, StringComparer.Ordinal)))
            return new(false, false, false, "This builder, patch, client and architecture combination is not declared compatible.");
        var evidence = Evidence.SingleOrDefault(e => e.Patch == patchVersion && e.Client == clientVersion
            && e.Builder == builderVersion && e.Architecture == architecture);
        return new(true, evidence?.BuildPassed == true, evidence?.DevicePassed == true,
            evidence?.BuildPassed == true ? "The catalog records a successful build for this exact combination." : "This combination has not been verified by a recorded build.");
    }

    private void Validate()
    {
        if (SchemaVersion != 1 || Patches is null || Clients is null || Rules is null || Evidence is null)
            throw new InvalidDataException("Unsupported or incomplete catalog.");
        if (Patches.Any(x => x is null) || Clients.Any(x => x is null) || Rules.Any(x => x is null) || Evidence.Any(x => x is null))
            throw new InvalidDataException("The catalog contains an empty entry.");
        if (Patches.Select(p => p.Version).Distinct(StringComparer.Ordinal).Count() != Patches.Length
            || Clients.Select(c => c.Version).Distinct(StringComparer.Ordinal).Count() != Clients.Length)
            throw new InvalidDataException("Duplicate release versions are not permitted.");
        foreach (var patch in Patches)
        {
            _ = ParseVersion(patch.Version);
            if (patch.Bridge <= 0 || !IsHex(patch.UpstreamCommit, 40)) throw new InvalidDataException("Invalid patch identity.");
            ValidateArtifact(patch.Upstream);
            ValidateArtifact(patch.Artifact);
        }
        foreach (var client in Clients)
        {
            _ = ParseVersion(client.Version);
            if (client.Bridge <= 0) throw new InvalidDataException("Invalid client bridge version.");
            ValidateArtifact(client.Artifact);
        }
        foreach (var rule in Rules)
        {
            ValidateRange(rule.Patch); ValidateRange(rule.Client); ValidateRange(rule.Builder);
            if (rule.Architectures is null || rule.Architectures.Length == 0
                || rule.Architectures.Any(a => !IsArchitecture(a))
                || rule.Architectures.Distinct(StringComparer.Ordinal).Count() != rule.Architectures.Length)
                throw new InvalidDataException("Invalid target architectures.");
        }
        var uniqueEvidence = new HashSet<(string, string, string, string)>();
        foreach (var item in Evidence)
        {
            _ = ParseVersion(item.Patch); _ = ParseVersion(item.Client); _ = ParseVersion(item.Builder);
            if (!IsArchitecture(item.Architecture) || item.DevicePassed && !item.BuildPassed
                || !uniqueEvidence.Add((item.Patch, item.Client, item.Builder, item.Architecture)))
                throw new InvalidDataException("Invalid or duplicate test evidence.");
            if (!Patches.Any(p => p.Version == item.Patch) || !Clients.Any(c => c.Version == item.Client))
                throw new InvalidDataException("Test evidence refers to an unknown release.");
            var patch = Patches.Single(p => p.Version == item.Patch);
            var client = Clients.Single(c => c.Version == item.Client);
            if (patch.Bridge != client.Bridge || !Rules.Any(rule => Contains(rule.Patch, item.Patch)
                && Contains(rule.Client, item.Client) && Contains(rule.Builder, item.Builder)
                && rule.Architectures.Contains(item.Architecture, StringComparer.Ordinal)))
                throw new InvalidDataException("Test evidence refers to an incompatible combination.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("The catalog contains a duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void ValidateArtifact(CatalogArtifact artifact)
    {
        if (artifact is null || !Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !IsHex(artifact.Sha256, 64))
            throw new InvalidDataException("Invalid release artifact.");
    }

    private static bool IsHex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
    private static bool IsArchitecture(string? value) => value is "x86" or "x64" or "arm" or "arm64";
    private static void ValidateRange(CatalogRange range)
    {
        if (range is null || ParseVersion(range.MinInclusive) >= ParseVersion(range.MaxExclusive))
            throw new InvalidDataException("Invalid compatibility range.");
    }
    private static bool Contains(CatalogRange range, string value)
    {
        var version = ParseVersion(value);
        return version >= ParseVersion(range.MinInclusive) && version < ParseVersion(range.MaxExclusive);
    }
    // This first schema accepts only three-part stable numeric versions.
    private static Version ParseVersion(string? value)
    {
        var parts = value?.Split('.');
        if (parts is null || parts.Length != 3 || parts.Any(part => part.Length == 0 || part.Length > 1 && part[0] == '0'
            || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new InvalidDataException("Versions must have three numeric components without leading zeros.");
        return new Version(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}

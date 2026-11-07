using System.Net.Http.Headers;
using System.Text.Json;

namespace Horizon.Builder.Core;

public sealed record BuilderUpdate(string Version, Uri ReleaseUri);

/// <summary>Checks public, stable builder releases without downloading or installing them.</summary>
public static class BuilderUpdateChecker
{
    public const string ReleasesEndpoint = "https://api.github.com/repos/megabytesme/MCHorizonUWP/releases?per_page=100";
    private const int ResponseLimit = 512 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();

    public static Task<BuilderUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        => CheckAsync(SharedClient, CatalogTrust.BuilderVersion, cancellationToken);

    public static async Task<BuilderUpdate?> CheckAsync(
        HttpClient client,
        string installedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!ReleaseMetadata.IsVersion(installedVersion)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("MCHorizonUWP-Builder/" + installedVersion);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > ResponseLimit)
            throw new InvalidDataException("The GitHub release response exceeded the size limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > ResponseLimit)
                throw new InvalidDataException("The GitHub release response exceeded the size limit.");
            buffer.Write(chunk, 0, count);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub returned an invalid release list.");

        BuilderUpdate? newest = null;
        Version? newestVersion = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object
                || !IsFalse(release, "draft")
                || !IsFalse(release, "prerelease")
                || !release.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String)
                continue;

            var tag = tagElement.GetString();
            const string prefix = "builder-v";
            if (tag is null || !tag.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var versionText = tag[prefix.Length..];
            if (!ReleaseMetadata.IsVersion(versionText)) continue;

            var version = Version.Parse(versionText);
            if (newestVersion is not null && version <= newestVersion) continue;
            newestVersion = version;
            newest = new BuilderUpdate(versionText,
                new Uri("https://github.com/megabytesme/MCHorizonUWP/releases/tag/" + Uri.EscapeDataString(tag)));
        }

        return newestVersion is not null && newestVersion > Version.Parse(installedVersion) ? newest : null;
    }

    private static bool IsFalse(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.False;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MCHorizonUWP-Builder/" + CatalogTrust.BuilderVersion);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

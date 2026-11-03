using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>A published upstream checkpoint, not a validated UWP port.</summary>
public sealed record NightlyRelease(string SourceCommit, DateTimeOffset PublishedAt)
{
    private const string ReleaseUrl = "https://api.github.com/repos/horizon-rust/horizon/releases/tags/nightly";

    public static NightlyRelease Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("tag_name").GetString() != "nightly" || !root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("GitHub did not return Horizon's nightly release.");
        var commit = root.GetProperty("target_commitish").GetString();
        var published = root.GetProperty("published_at").GetString();
        if (commit is null || commit.Length != 40 || commit.Any(c => !Uri.IsHexDigit(c)) ||
            !DateTimeOffset.TryParseExact(published, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            throw new InvalidDataException("The nightly release metadata is incomplete.");
        return new(commit.ToLowerInvariant(), time);
    }

    public static async Task<NightlyRelease> FetchAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HorizonUWPBuilder", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(ReleaseUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_000_000)
            throw new InvalidDataException("The nightly release response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes)) != 0)
        {
            if (buffer.Length + count > 1_000_000) throw new InvalidDataException("The nightly release response is too large.");
            buffer.Write(bytes, 0, count);
        }
        return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}

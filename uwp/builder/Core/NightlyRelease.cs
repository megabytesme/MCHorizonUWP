using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>A published upstream checkpoint, not a validated UWP port.</summary>
public sealed record NightlyRelease(string SourceCommit, DateTimeOffset PublishedAt, string ReleaseNotes)
{
    private const string ReleaseUrl = "https://api.github.com/repos/horizon-rust/horizon/releases/tags/nightly";

    public static NightlyRelease Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        if ((tag != "nightly" && !(tag?.StartsWith("nightly-", StringComparison.Ordinal) ?? false)) ||
            !root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("GitHub did not return Horizon's nightly release.");
        var commit = root.GetProperty("target_commitish").GetString();
        var published = root.GetProperty("published_at").GetString();
        if (commit is null || commit.Length != 40 || commit.Any(c => !Uri.IsHexDigit(c)) ||
            !DateTimeOffset.TryParseExact(published, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            throw new InvalidDataException("The nightly release metadata is incomplete.");
        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            ? body.GetString() ?? "" : "";
        return new(commit.ToLowerInvariant(), time, notes);
    }

    public static async Task<NightlyRelease> FetchAsync()
    {
        return await FetchAsync(ReleaseUrl);
    }

    /// <summary>Fetch notes for the archived release matching the build's pinned source.</summary>
    public static async Task<string?> FetchPinnedNotesAsync(string date, string sourceCommit)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new ArgumentException("The pinned nightly date is invalid.", nameof(date));
        var current = await FetchAsync(ReleaseUrl);
        if (current.SourceCommit == sourceCommit && current.ReleaseNotes.Length != 0)
            return current.ReleaseNotes.Length <= 1024 * 1024 ? current.ReleaseNotes : null;
        NightlyRelease? archived;
        try { archived = await FetchAsync($"https://api.github.com/repos/horizon-rust/horizon/releases/tags/nightly-{date}"); }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
        if (archived.SourceCommit != sourceCommit || archived.ReleaseNotes.Length == 0) return null;
        return archived.ReleaseNotes.Length <= 1024 * 1024 ? archived.ReleaseNotes : null;
    }

    private static async Task<NightlyRelease> FetchAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HorizonUWPBuilder", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (response.Content.Headers.ContentLength is > 1_000_000)
            throw new InvalidDataException("The nightly release response is too large.");
        response.EnsureSuccessStatusCode();
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

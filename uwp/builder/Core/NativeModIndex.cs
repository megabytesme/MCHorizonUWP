using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>A registry version entry. Raw JSON is retained so the pinned launcher can re-check the official signature and manifest.</summary>
public sealed record NativeModCandidate(string Id, string Name, string Version, string Game, string Api,
    string Url, long Size, string Sha256, string[] BrokenCommits, string RawProject, string RawVersion);

public sealed record NativeModListing(string Id, string Name, string Summary, string Version, string[] Authors,
    IReadOnlyList<NativeModCandidate> Versions, IReadOnlyList<NativeModCandidate>? CompatibleVersions = null,
    string CompatibilityMessage = "Compatibility has not been checked.")
{
    public bool IsCompatible => CompatibleVersions is { Count: > 0 };
}

public sealed record NativeModCatalog(byte[] OriginalIndex, IReadOnlyList<NativeModListing> Mods);

public static class NativeModIndex
{
    public const string RegistryUrl = "https://horizon-rust.github.io/horizon-mods/index.json";
    private const int MaxBytes = 16 * 1024 * 1024;

    public static async Task<IReadOnlyList<NativeModListing>> FetchAsync(CancellationToken cancellationToken = default)
        => (await FetchCatalogAsync(cancellationToken)).Mods;

    public static async Task<NativeModCatalog> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        using var response = await http.GetAsync(RegistryUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxBytes) throw new InvalidDataException("The mod index is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (bytes.Length + count > MaxBytes) throw new InvalidDataException("The mod index is too large.");
            bytes.Write(buffer, 0, count);
        }
        var original = bytes.ToArray();
        return new(original, ParseCore(original));
    }

    public static IReadOnlyList<NativeModListing> Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The mod index is too large.");
        return ParseCore(bytes);
    }

    private static IReadOnlyList<NativeModListing> ParseCore(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unsupported mod index schema.");
            var revoked = root.TryGetProperty("revoked", out var revokedJson) ? revokedJson.EnumerateArray().Select(item =>
                (Id: item.GetProperty("id").GetString(), Hash: item.TryGetProperty("sha256", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null)).ToArray()
                : [];
            var listings = new List<NativeModListing>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in root.GetProperty("projects").EnumerateArray())
            {
                if (project.GetProperty("kind").GetString() != "native-mod") continue;
                var id = project.GetProperty("id").GetString() ?? "";
                var name = project.GetProperty("name").GetString() ?? "";
                var summary = project.GetProperty("summary").GetString() ?? "";
                if (!Regex.IsMatch(id, "^[a-z0-9_]{1,64}$") || !ids.Add(id) || name.Length is < 1 or > 256 || summary.Length > 2048)
                    throw new InvalidDataException("The native mod index has an invalid project.");
                var authors = project.TryGetProperty("authors", out var authorsJson)
                    ? authorsJson.EnumerateArray().Select(a => a.GetString() ?? "").ToArray() : [];
                var candidates = new List<NativeModCandidate>();
                var versionsSeen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var version in project.GetProperty("versions").EnumerateArray())
                {
                    var game = version.GetProperty("game").GetString() ?? "";
                    if (game != LocalRecipe.MinecraftVersion) continue;
                    var file = version.GetProperty("file");
                    var fileName = file.GetProperty("name").GetString() ?? "";
                    var hash = file.GetProperty("sha256").GetString() ?? "";
                    var label = version.GetProperty("version").GetString() ?? "";
                    var url = file.TryGetProperty("url", out var urlJson) ? urlJson.GetString() ?? "" : "";
                    var size = file.TryGetProperty("size", out var sizeJson) && sizeJson.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                    var api = version.TryGetProperty("api", out var apiJson) ? apiJson.GetString() ?? "" : "";
                    if (!fileName.EndsWith(".hnmod", StringComparison.Ordinal) || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)
                        || label.Length is < 1 or > 128 || !versionsSeen.Add(label))
                        throw new InvalidDataException("The native mod index has an invalid version.");
                    if (url.Length != 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme != Uri.UriSchemeHttps))
                        throw new InvalidDataException("The native mod index has an invalid download URL.");
                    if (size < 0 || size > 64L * 1024 * 1024) throw new InvalidDataException("The native mod index lists an invalid bundle size.");
                    if (revoked.Any(r => r.Id == id && (r.Hash is null || r.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)))) continue;
                    var broken = version.TryGetProperty("broken_on", out var brokenJson) && brokenJson.ValueKind == JsonValueKind.Array
                        ? brokenJson.EnumerateArray().Select(item => item.TryGetProperty("horizon", out var commit) ? commit.GetString() ?? "" : "").ToArray()
                        : [];
                    candidates.Add(new(id, name, label, game, api, url, size, hash, broken, project.GetRawText(), version.GetRawText()));
                }
                if (candidates.Count != 0)
                    listings.Add(new(id, name, summary, candidates[0].Version, authors, candidates));
            }
            return listings.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("The native mod index is invalid.", error);
        }
    }

    internal static byte[] CreatePinnedIndex(NativeModCatalog catalog,
        IReadOnlyList<(NativeModCandidate Candidate, string LocalFile)> versions)
    {
        var root = JsonNode.Parse(catalog.OriginalIndex)?.AsObject()
            ?? throw new InvalidDataException("The native mod index is invalid.");
        var projects = new JsonArray();
        foreach (var (candidate, localFile) in versions)
        {
            var project = JsonNode.Parse(candidate.RawProject)?.AsObject()
                ?? throw new InvalidDataException("The native mod project is invalid.");
            var version = JsonNode.Parse(candidate.RawVersion)?.AsObject()
                ?? throw new InvalidDataException("The native mod version is invalid.");
            var file = version["file"]?.AsObject() ?? throw new InvalidDataException("The native mod file entry is invalid.");
            file["url"] = LocalFileUrl(localFile);
            project["versions"] = new JsonArray(version);
            projects.Add(project);
        }
        root["projects"] = projects;
        return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static string LocalFileUrl(string path)
    {
        var absolute = Path.GetFullPath(path).Replace('\\', '/');
        if (absolute.Length < 3 || absolute[1] != ':' || absolute[2] != '/')
            throw new InvalidDataException("The verified native mod must be staged on a local drive.");
        return "file:///" + absolute;
    }
}

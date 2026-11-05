using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Read-only native-mod browsing; installing requires Horizon's complete bundle verification and mixin pipeline.</summary>
public sealed record NativeModListing(string Id, string Name, string Summary, string Version, string[] Authors);

public static class NativeModIndex
{
    public const string RegistryUrl = "https://horizon-rust.github.io/horizon-mods/index.json";
    private const int MaxBytes = 16 * 1024 * 1024;

    public static async Task<IReadOnlyList<NativeModListing>> FetchAsync(CancellationToken cancellationToken = default)
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
        return Parse(bytes.ToArray());
    }

    public static IReadOnlyList<NativeModListing> Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The mod index is too large.");
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
                foreach (var version in project.GetProperty("versions").EnumerateArray())
                {
                    if (version.GetProperty("game").GetString() != LocalRecipe.MinecraftVersion) continue;
                    var file = version.GetProperty("file");
                    var fileName = file.GetProperty("name").GetString() ?? "";
                    var hash = file.GetProperty("sha256").GetString() ?? "";
                    var label = version.GetProperty("version").GetString() ?? "";
                    if (!fileName.EndsWith(".hnmod", StringComparison.Ordinal) || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)
                        || label.Length is < 1 or > 128) throw new InvalidDataException("The native mod index has an invalid version.");
                    if (revoked.Any(r => r.Id == id && (r.Hash is null || r.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)))) continue;
                    listings.Add(new(id, name, summary, label, authors));
                    break;
                }
            }
            return listings.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("The mod index is invalid.", error);
        }
    }
}

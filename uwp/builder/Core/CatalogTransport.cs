using System.Net;
using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Downloads a signed catalog and checks each downloaded artifact against its digest.</summary>
public sealed class CatalogTransport : IDisposable
{
    private const long MaxArtifactBytes = 4L * 1024 * 1024 * 1024;
    private readonly HttpClient http;
    private readonly TimeSpan inactivityTimeout;

    public CatalogTransport() : this(new SocketsHttpHandler { AllowAutoRedirect = false }) { }
    internal CatalogTransport(HttpMessageHandler handler, TimeSpan? inactivityTimeout = null)
    {
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MCHorizonUWPBuilder/{CatalogTrust.BuilderVersion}");
        this.inactivityTimeout = inactivityTimeout ?? TimeSpan.FromSeconds(45);
    }
    public void Dispose() => http.Dispose();

    public async Task<CompatibilityCatalog> GetCatalogAsync(Uri catalogUri, Uri signatureUri, byte[] trustedPublicKey, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadBoundedAsync(catalogUri, 2_000_000, cancellationToken);
        var signature = await ReadBoundedAsync(signatureUri, 64, cancellationToken);
        return CatalogSignature.Verify(catalog, signature, trustedPublicKey);
    }

    public async Task DownloadArtifactAsync(CatalogArtifact artifact, string output, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await DownloadArtifactOnceAsync(artifact, output, cancellationToken); return; }
            catch (Exception error) when (attempt < 2 && !cancellationToken.IsCancellationRequested
                && error is HttpRequestException or IOException or TaskCanceledException or TimeoutException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
            }
        }
    }

    private async Task DownloadArtifactOnceAsync(CatalogArtifact artifact, string output, CancellationToken cancellationToken)
    {
        if (artifact is null || artifact.Sha256 is null || artifact.Sha256.Length != 64 || !artifact.Sha256.All(char.IsAsciiHexDigit)
            || !Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri))
            throw new InvalidDataException("Invalid catalog artifact.");
        var destination = Path.GetFullPath(output);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The artifact destination already exists.");
        using var response = await GetAsync(uri, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaxArtifactBytes) throw new InvalidDataException("The artifact is too large.");
        var created = false;
        try
        {
            await using var outputStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            created = true;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while (true)
            {
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                inactivity.CancelAfter(inactivityTimeout);
                try { read = await input.ReadAsync(buffer, inactivity.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new TimeoutException("The download stopped receiving data."); }
                if (read == 0) break;
                total += read;
                if (total > MaxArtifactBytes) throw new InvalidDataException("The artifact is too large.");
                hash.AppendData(buffer, 0, read);
                await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The artifact does not match its catalog digest.");
        }
        catch
        {
            if (created) File.Delete(destination);
            throw;
        }
    }

    private async Task<byte[]> ReadBoundedAsync(Uri uri, int limit, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(uri, cancellationToken);
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The catalog response is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("The catalog response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 6; i++)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("Catalog downloads require HTTPS URLs without credentials.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Accept.ParseAdd("application/octet-stream");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("The download redirect has no destination.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); }
            catch { response.Dispose(); throw; }
            return response;
        }
        throw new InvalidDataException("The download redirected too many times.");
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>A one-time ownership check. Tokens stay in memory and are not written to the launcher's account store.</summary>
public sealed class MinecraftSignIn : IDisposable
{
    private const string MicrosoftBase = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string MinecraftBase = "https://api.minecraftservices.com";
    private readonly HttpClient http;

    public MinecraftSignIn() : this(new SocketsHttpHandler { AllowAutoRedirect = false }) { }
    internal MinecraftSignIn(HttpMessageHandler handler)
    {
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1024 * 1024 };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
    public void Dispose() => http.Dispose();

    public sealed class Challenge
    {
        internal Challenge(string userCode, string deviceCode, Uri verificationUri, DateTimeOffset expires, int interval)
            => (UserCode, DeviceCode, VerificationUri, Expires, Interval) = (userCode, deviceCode, verificationUri, expires, interval);
        public string UserCode { get; }
        public Uri VerificationUri { get; }
        internal string DeviceCode { get; }
        internal DateTimeOffset Expires { get; }
        internal int Interval { get; }
        public override string ToString() => "Microsoft sign-in challenge";
    }

    public async Task<Challenge> BeginAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MicrosoftBase + "/devicecode")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = HorizonIdentity.ClientId, ["scope"] = "XboxLive.signin offline_access" }),
        };
        using var response = await SendAsync(request, cancellationToken);
        if (response.Status != 200) throw new InvalidOperationException("Microsoft could not start sign-in.");
        var value = response.Body.RootElement;
        var code = RequiredString(value, "user_code");
        var secret = RequiredString(value, "device_code");
        var uri = Uri.TryCreate(OptionalString(value, "verification_uri"), UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps && parsed.UserInfo.Length == 0
            && (parsed.Host.Equals("microsoft.com", StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase))
                ? parsed : new Uri("https://www.microsoft.com/link");
        var interval = value.TryGetProperty("interval", out var intervalJson) && intervalJson.TryGetInt32(out var seconds) ? Math.Clamp(seconds, 1, 1800) : 5;
        var lifetime = value.TryGetProperty("expires_in", out var expiresJson) && expiresJson.TryGetInt32(out var expiry) ? Math.Clamp(expiry, 1, 1800) : 900;
        return new(code, secret, uri, DateTimeOffset.UtcNow.AddSeconds(lifetime), interval);
    }

    public async Task<bool> CompleteOwnershipCheckAsync(Challenge challenge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var interval = challenge.Interval;
        string microsoftToken;
        while (true)
        {
            if (DateTimeOffset.UtcNow >= challenge.Expires) throw new InvalidOperationException("The sign-in code expired. Start again.");
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            if (DateTimeOffset.UtcNow >= challenge.Expires) throw new InvalidOperationException("The sign-in code expired. Start again.");
            using var request = new HttpRequestMessage(HttpMethod.Post, MicrosoftBase + "/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["client_id"] = HorizonIdentity.ClientId, ["device_code"] = challenge.DeviceCode }),
            };
            using var response = await SendAsync(request, cancellationToken);
            var error = OptionalString(response.Body.RootElement, "error");
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval = Math.Min(interval + 5, 1800); continue; }
            if (response.Status != 200) throw new InvalidOperationException("Microsoft sign-in was declined or could not be completed.");
            microsoftToken = RequiredString(response.Body.RootElement, "access_token");
            break;
        }

        using var xbox = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" }, cancellationToken);
        if (xbox.Status != 200) throw new InvalidOperationException("Xbox Live refused the sign-in.");
        var xboxToken = RequiredString(xbox.Body.RootElement, "Token");
        using var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" }, cancellationToken);
        if (xsts.Status != 200) throw new InvalidOperationException("Xbox Live could not authorize this account. Check its Xbox profile and family settings.");
        var xstsToken = RequiredString(xsts.Body.RootElement, "Token");
        var claims = xsts.Body.RootElement.GetProperty("DisplayClaims").GetProperty("xui");
        if (claims.GetArrayLength() == 0) throw new InvalidDataException("Xbox Live returned incomplete sign-in information.");
        var userHash = RequiredString(claims[0], "uhs");
        var xToken = $"XBL3.0 x={userHash};{xstsToken}";
        using var primary = await PostJsonAsync(MinecraftBase + "/launcher/login", new { xtoken = xToken, platform = "PC_LAUNCHER" }, cancellationToken);
        string minecraftToken;
        if (primary.Status == 200 && OptionalString(primary.Body.RootElement, "access_token") is { Length: > 0 } token)
            minecraftToken = token;
        else
        {
            using var fallback = await PostJsonAsync(MinecraftBase + "/authentication/login_with_xbox", new { identityToken = xToken }, cancellationToken);
            if (fallback.Status != 200) throw new InvalidOperationException("Minecraft services refused the sign-in.");
            minecraftToken = RequiredString(fallback.Body.RootElement, "access_token");
        }
        using var ownershipRequest = new HttpRequestMessage(HttpMethod.Get, MinecraftBase + "/entitlements/mcstore");
        ownershipRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var ownership = await SendAsync(ownershipRequest, cancellationToken);
        if (ownership.Status != 200) throw new InvalidOperationException("Could not check Minecraft ownership.");
        return ownership.Body.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            && items.EnumerateArray().Any(item => OptionalString(item, "name") is "game_minecraft" or "product_minecraft");
    }

    private async Task<JsonResponse> PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try { return new((int)response.StatusCode, JsonDocument.Parse(bytes)); }
        catch (JsonException) { throw new InvalidDataException("A sign-in service returned an invalid response."); }
    }

    private static string? OptionalString(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static string RequiredString(JsonElement value, string name)
        => OptionalString(value, name) is { Length: > 0 and <= 65536 } text ? text : throw new InvalidDataException("A sign-in service returned incomplete information.");
    private sealed record JsonResponse(int Status, JsonDocument Body) : IDisposable { public void Dispose() => Body.Dispose(); }
}

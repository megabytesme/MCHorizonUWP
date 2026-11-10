using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Applies the UWP-only machine architecture fallback required by client 0.1.4.</summary>
internal static class ClientUwpSourceBackport
{
    private const string ClientVersion = "0.1.4";
    private const string HorizonCommit = "e797c725ad99ef311db216de7f41a2e6593353db";
    private const string SourceSha256 = "A568E04AE6B9A24D760494DA892182ED78C411226B651E11751DDA40908DAEFA";
    private const string BackportedSha256 = "B8844C46F2D03D3AFB8D782B3EDD02E72F67EA67512A49145E4C6D8D41B3CA65";

    internal static void Apply(string source, string clientVersion, string upstreamCommit)
    {
        if (clientVersion != ClientVersion || !upstreamCommit.Equals(HorizonCommit, StringComparison.OrdinalIgnoreCase)) return;

        var path = Path.Combine(source, "crates", "horizon-system", "src", "lib.rs");
        var original = File.ReadAllBytes(path);
        var digest = Convert.ToHexString(SHA256.HashData(original));
        var text = File.ReadAllText(path);
        if (digest == BackportedSha256) return;
        if (digest != SourceSha256)
            throw new InvalidDataException("The Horizon system architecture probe does not match the authenticated client 0.1.4 source.");

        var transformed = Transform(text);
        if (!Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(transformed))).Equals(BackportedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The Horizon system architecture probe backport did not produce the reviewed client 0.1.4 output.");
        File.WriteAllText(path, transformed, new System.Text.UTF8Encoding(false));
    }

    internal static string Transform(string text)
    {
        const string uwpCfg = "#[cfg(all(windows, target_vendor = \"uwp\"))]";
        const string desktopCfg = "#[cfg(all(windows, not(target_vendor = \"uwp\")))]";
        if (text.Contains(uwpCfg, StringComparison.Ordinal) && text.Contains(desktopCfg, StringComparison.Ordinal)) return text;

        var windowsCfg = new Regex(@"(?m)^#\[cfg\(windows\)\](?<newline>\r?\n)(?=fn emulating_on\(\))");
        if (windowsCfg.Matches(text).Count != 1)
            throw new InvalidDataException("The Horizon Windows architecture probe has an unexpected shape.");
        text = windowsCfg.Replace(text, "#[cfg(all(windows, not(target_vendor = \"uwp\")))]${newline}", 1);

        const string macosAnchor = "#[cfg(target_os = \"macos\")]";
        if (text.Split(macosAnchor, StringSplitOptions.None).Length != 2)
            throw new InvalidDataException("The Horizon macOS architecture probe anchor has an unexpected shape.");
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var fallback = string.Join(newline, uwpCfg,
            "fn emulating_on() -> Option<&'static str> { None }", "", macosAnchor);
        return text.Replace(macosAnchor, fallback, StringComparison.Ordinal);
    }
}

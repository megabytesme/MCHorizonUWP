using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Applies the validated TLS-directory fix to the compatible 0.1.3 client source.</summary>
internal static class ClientTlsBackport
{
    private const string ClientVersion = "0.1.3";
    private static readonly (string Path, string Resource, string OriginalSha256)[] Files =
    [
        ("uwp/host/tls-index.c", "tls-index.c", "2E143059ACD68061D53A6F3292EC7C7FBC7746ACDCE60A4EB2C1A809F86D7157"),
        ("uwp/host/strict-bridge-link.ps1", "strict-bridge-link.ps1", "B61292624C1792129C38DD94DB76C9892164EE26FC65AF83F9482C7FE90E6EAA")
    ];

    internal static void Apply(string source, string clientVersion)
    {
        if (clientVersion != ClientVersion) return;

        var pending = new List<(string Path, byte[] Bytes)>();
        foreach (var (relative, resource, originalHash) in Files)
        {
            var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            var original = File.ReadAllBytes(path);
            using var stream = typeof(ClientTlsBackport).Assembly.GetManifestResourceStream(
                "Horizon.Builder.Core.CompactClient." + resource)
                ?? throw new InvalidOperationException("A UWP TLS backport input is missing from the builder.");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            var replacement = output.ToArray();
            var digest = Convert.ToHexString(SHA256.HashData(original));
            if (digest != originalHash && digest != Convert.ToHexString(SHA256.HashData(replacement)))
                throw new InvalidDataException($"The {relative} file does not match the authenticated {ClientVersion} client.");
            if (!original.SequenceEqual(replacement)) pending.Add((path, replacement));
        }

        foreach (var (path, bytes) in pending) File.WriteAllBytes(path, bytes);
    }
}

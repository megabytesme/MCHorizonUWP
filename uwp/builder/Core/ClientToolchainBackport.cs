using System.Security.Cryptography;

namespace Horizon.Builder.Core;

/// <summary>Backports the prepared-toolchain hooks only to exact authenticated client scripts.</summary>
internal static class ClientToolchainBackport
{
    private static readonly (string Path, string Resource, string Original010, string Original011)[] Files =
    [
        ("uwp/host/build-game.cmd", "build-game.cmd", "DB4270108924A8E563713AFE8655F12104AF54A0FBF6A7206A767B1A386C729D", "77A14A2130CD9B99A5C5060376EDF1EA96A2164D56225CB62E0ED6D50399A90E"),
        ("uwp/pregame/host/build-shell.cmd", "build-shell.cmd", "77BFDF42DD64967403B63A4172C03169FA8D05B3961089B3BBA3FC43E43B4EF1", "27D8A0B69E77C4C49BC3C2731945D1A01B668CF8FF49CCC3AE4268322B6F350D"),
        ("uwp/host/package.ps1", "package.ps1", "FD3D3F23512B1BEE80C8FBEC522111E3A9EC7C752EF9770073F20FAD9543DD2A", "FD3D3F23512B1BEE80C8FBEC522111E3A9EC7C752EF9770073F20FAD9543DD2A")
    ];

    internal static void Apply(string source, string clientVersion)
    {
        if (clientVersion is not ("0.1.0" or "0.1.1")) throw new InvalidOperationException("This compact-toolchain backport does not support this client version.");
        var pending = new List<(string Path, byte[] Bytes)>();
        foreach (var (relative, resource, original010, original011) in Files)
        {
            var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            var original = File.ReadAllBytes(path);
            using var stream = typeof(ClientToolchainBackport).Assembly.GetManifestResourceStream("Horizon.Builder.Core.CompactClient." + resource)
                ?? throw new InvalidOperationException("A compact-toolchain client script is missing from the builder.");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            var replacement = output.ToArray();
            var digest = Convert.ToHexString(SHA256.HashData(original));
            var expected = clientVersion == "0.1.0" ? original010 : original011;
            if (digest != expected && digest != Convert.ToHexString(SHA256.HashData(replacement)))
                throw new InvalidDataException($"The {relative} script does not match the authenticated {clientVersion} client.");
            if (!original.SequenceEqual(replacement)) pending.Add((path, replacement));
        }
        foreach (var (path, bytes) in pending) File.WriteAllBytes(path, bytes);
    }
}

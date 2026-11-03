using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Canonical requested inputs. Caller verifies their provenance and actual contents.</summary>
public sealed class BuildInputs
{
    public string Schema { get; private init; } = "";
    public string Target { get; private init; } = "";
    public string PackageVersion { get; private init; } = "";
    public bool PrepareSounds { get; private init; }
    public bool UseResourceFramework { get; private init; }
    public IReadOnlyList<string> Features { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> ModIds { get; private init; } = Array.Empty<string>();
    public string SourceCommit { get; private init; } = "";
    public string RecipeSha256 { get; private init; } = "";
    public string CargoLockSha256 { get; private init; } = "";
    public string RustStdPatchSha256 { get; private init; } = "";
    public string ResourceInventorySha256 { get; private init; } = "";
    public string? TouchModSha256 { get; private init; }
    public string? DeviceVibrationModSha256 { get; private init; }
    public string? MicrosoftClientId { get; private init; }
    public string RustToolchain { get; private init; } = "";
    public string MsvcVersion { get; private init; } = "";
    public string WindowsSdkVersion { get; private init; } = "";

    private BuildInputs() { }

    public static BuildInputs Create(BuildSelection selection, string sourceCommit,
        string recipeSha256, string cargoLockSha256, string rustStdPatchSha256,
        string resourceInventorySha256, string? touchModSha256, string rustToolchain,
        string msvcVersion, string windowsSdkVersion, string? deviceVibrationModSha256 = null)
    {
        if (selection.NativeTouchMod != (touchModSha256 is not null))
            throw new ArgumentException("A selected touch mod needs exactly one content digest.");
        if (selection.NativeDeviceVibration != (deviceVibrationModSha256 is not null))
            throw new ArgumentException("A selected device vibration mod needs exactly one content digest.");
        if (!Regex.IsMatch(rustToolchain, "^[a-zA-Z0-9._+-]{1,64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid Rust toolchain identifier.");
        if (!Version.TryParse(msvcVersion, out _) || !Version.TryParse(windowsSdkVersion, out _))
            throw new ArgumentException("MSVC and Windows SDK versions must be numeric versions.");
        return new BuildInputs
        {
            Schema = "horizon-uwp-inputs-v6", Target = selection.RustTarget, PackageVersion = selection.ValidatedPackageVersion,
            PrepareSounds = selection.PrepareSounds,
            UseResourceFramework = selection.UseResourceFramework,
            Features = Array.AsReadOnly(selection.Features.ToArray()),
            ModIds = Array.AsReadOnly(selection.ModIds.ToArray()), SourceCommit = Hex(sourceCommit, 40),
            RecipeSha256 = Hex(recipeSha256, 64), CargoLockSha256 = Hex(cargoLockSha256, 64),
            RustStdPatchSha256 = Hex(rustStdPatchSha256, 64),
            ResourceInventorySha256 = Hex(resourceInventorySha256, 64),
            TouchModSha256 = touchModSha256 is null ? null : Hex(touchModSha256, 64),
            DeviceVibrationModSha256 = deviceVibrationModSha256 is null ? null : Hex(deviceVibrationModSha256, 64),
            MicrosoftClientId = selection.ValidatedMicrosoftClientId,
            RustToolchain = rustToolchain, MsvcVersion = msvcVersion, WindowsSdkVersion = windowsSdkVersion,
        };
    }

    public byte[] CanonicalUtf8() => JsonSerializer.SerializeToUtf8Bytes(this);
    public string Sha256() => Convert.ToHexString(SHA256.HashData(CanonicalUtf8())).ToLowerInvariant();

    private static string Hex(string value, int length)
    {
        if (value.Length != length || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException($"Expected a {length}-character hexadecimal digest.");
        return value.ToLowerInvariant();
    }
}

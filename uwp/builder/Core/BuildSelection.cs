namespace Horizon.Builder.Core;

/// <summary>Requested inputs only; this is not a verified recipe or a build record.</summary>
public sealed record BuildSelection(TargetProfile Target, bool NativeTouchMod, string PackageVersion = LocalRecipe.DefaultPackageVersion, bool PrepareSounds = false, bool UseResourceFramework = false, bool NativeDeviceVibration = false, string MicrosoftClientId = "", string Renderer = "native-d3d11")
{
    // Public Fluent Launcher application ID, selected at the user's request while their own is reviewed.
    public const string FluentLauncherClientId = "0844e754-1d2e-4861-8e2b-18059609badb";
    public const string TouchModId = "horizon_touch";
    public const string DeviceVibrationModId = "horizon_device_vibration";
    public bool IncludesNativeTouchMod => NativeTouchMod;
    public IReadOnlyList<string> ModIds => new[]
        { IncludesNativeTouchMod ? TouchModId : null, NativeDeviceVibration ? DeviceVibrationModId : null }
        .Where(id => id is not null).Select(id => id!).ToArray();
    public string ValidatedPackageVersion
    {
        get
        {
            var parts = PackageVersion.Split('.');
            if (parts.Length != 4 || parts.Any(part => part.Length == 0 || (part.Length > 1 && part[0] == '0')
                || !ushort.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new ArgumentException("Package version must contain four numeric components from 0 to 65535.");
            if (new Version(PackageVersion) < new Version(LocalRecipe.SourceManifestVersion))
                throw new ArgumentException($"Package version cannot be lower than the pinned source version {LocalRecipe.SourceManifestVersion}.");
            return PackageVersion;
        }
    }
    // Application (client) IDs are public GUIDs, not credentials.
    public string? ValidatedMicrosoftClientId
    {
        get
        {
            var text = MicrosoftClientId.Trim();
            if (text.Length == 0) return null;
            if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty)
                throw new ArgumentException("Microsoft application ID must be a nonempty GUID.");
            return id.ToString("D");
        }
    }
    public string RustTarget => Target switch
    {
        TargetProfile.DesktopX64 => "x86_64-uwp-windows-msvc",
        TargetProfile.SurfaceProX => "aarch64-uwp-windows-msvc",
        TargetProfile.WindowsMobile => "thumbv7a-uwp-windows-msvc",
        _ => throw new ArgumentOutOfRangeException(nameof(Target)),
    };

    public string ValidatedRenderer => Renderer switch
    {
        "native-d3d11" => Renderer,
        "angle" when Target == TargetProfile.DesktopX64 => Renderer,
        "angle" => throw new NotSupportedException("The current package recipe contains ANGLE runtime DLLs for x64 only; this is a package build constraint, not a device capability check."),
        "dx12" when Target == TargetProfile.DesktopX64 => Renderer,
        "dx12" => throw new NotSupportedException("The Direct3D 12 CoreWindow backend currently builds only for x64; the Windows bindings fail to compile for ARM32."),
        _ => throw new ArgumentException("Choose a supported renderer."),
    };

    // These features are recorded as individual tokens; no user-supplied shell
    // fragments or paths are interpolated. Execution requires a verified recipe.
    public IReadOnlyList<string> Features => new[]
        { "native-d3d11",
          Target == TargetProfile.DesktopX64 ? "uwp-dx12-renderer" : null,
          IncludesNativeTouchMod ? "native-touch-mod" : null,
          NativeDeviceVibration ? "native-device-vibration-mod" : null, "uwp-audio", "uwp-https", "uwp-controller", "xaml-shell" }
        .Where(feature => feature is not null).Select(feature => feature!).ToArray();
}

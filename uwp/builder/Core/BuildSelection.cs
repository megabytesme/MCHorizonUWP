namespace Horizon.Builder.Core;

/// <summary>Requested inputs only; this is not a verified recipe or a build record.</summary>
public sealed record BuildSelection(TargetProfile Target, bool NativeTouchMod, string PackageVersion = "1.0.10.0")
{
    public const string TouchModId = "horizon_touch";
    public IReadOnlyList<string> ModIds => NativeTouchMod ? [TouchModId] : [];
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
    public string RustTarget => Target switch
    {
        TargetProfile.DesktopX64 => "x86_64-uwp-windows-msvc",
        TargetProfile.SurfaceProX => "aarch64-uwp-windows-msvc",
        TargetProfile.WindowsMobile => "thumbv7a-uwp-windows-msvc",
        _ => throw new ArgumentOutOfRangeException(nameof(Target)),
    };

    // These features are recorded as individual tokens; no user-supplied shell
    // fragments or paths are interpolated. Execution requires a verified recipe.
    public IReadOnlyList<string> Features => NativeTouchMod
        ? ["native-d3d11", "native-touch-mod", "uwp-audio", "uwp-https"]
        : ["native-d3d11", "uwp-audio", "uwp-https"];
}

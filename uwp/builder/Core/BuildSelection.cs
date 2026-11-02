namespace Horizon.Builder.Core;

/// <summary>Requested inputs only; this is not a verified recipe or a build record.</summary>
public sealed record BuildSelection(TargetProfile Target, bool NativeTouchMod)
{
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
        ? ["native-d3d11", "native-touch-mod"]
        : ["native-d3d11"];
}

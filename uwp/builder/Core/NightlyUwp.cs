namespace Horizon.Builder.Core;

public static class NightlyUwp
{
    public const string UpstreamCommit = "0b14f8acfd97fa2c750e42795249dcc981178ca4";
    public const string November20Commit = "0a875da91dbb0fdcb7ba8c42c11341aa229af51a";
    public const string November20SourceTag = "nightly-source/2026-11-20-0a875da";
    public const string November20Patch = "0.1.13";
    public const string November20Client = "0.1.15";
    private const string November20InitialClient = "0.1.14";
    public const string PatchVersion = "0.1.5";
    public const string ClientVersion = "0.1.6";
    public const string CandidatePatchVersion = "0.1.12";
    public const string CandidateClientVersion = StreamingAudioClientVersion;
    private const string StreamingAudioClientVersion = "0.1.13";
    private const string FixedWgpuPatchVersion = "0.1.11";
    private const string FixedWgpuClientVersion = "0.1.12";
    private const string MobileControlsPatchVersion = "0.1.10";
    private const string MobileControlsClientVersion = "0.1.11";
    private const string ArmHudPatchVersion = "0.1.9";
    private const string ArmHudClientVersion = "0.1.10";
    private const string LegacyRendererPatchVersion = "0.1.8";
    private const string LegacyRendererClientVersion = "0.1.9";
    private const string CodeRuntimePatchVersion = "0.1.7";
    private const string CodeRuntimeClientVersion = "0.1.8";
    public const string EmbeddedPatchVersion = "0.1.6";
    public const string EmbeddedClientVersion = "0.1.7";
    internal const string WasmtimeConfig = ".cargo/wasmtime-uwp.toml";
    internal const string WasmtimeArchive = "uwp/wasm-abi-probe/wasmtime-50.0.2.crate";
    internal const string WasmStandardLibrary = "uwp/wasm-abi-probe/horizon-wasm-std.wasm";
    internal const int ResourceSchema = 7;
    internal const int PrepareOutputVersion = 5;
    public const string PublicNativeModsUnavailable =
        "Public native SDK mods are unavailable for this Nightly because the SDK build and mixin pipeline have not been validated for UWP.";

    // GitHub codeload ZIP for megabytesme/wgpu-dx11 commit 4c24e58de1ee12b9dd3a81cc4490662353011c30.
    internal const string WgpuArchiveHash = "4c8769b47eea565ca994d4699ff72e70f6647613b6205c8daa52f2b04a743eaa";
    internal const string WgpuPatchHash = "ee0a57e971221a3b705ca8434b67ea2f2909fb8b3b21444163e0bf5be44046e4";
    // Local git archive of aa7b65c43ef008d0a92d20fcdf9f10fa0883f157, including the upload-shadow commit.
    internal const string FixedWgpuArchiveHash = "8de8795116083533812728a855144a5f5e229d0562ef07990393bc5124c48c2e";

    private static bool IsNovember20(string? commit) => string.Equals(commit, November20Commit, StringComparison.OrdinalIgnoreCase);

    private static bool IsNovember20Client(string? client) => client is November20InitialClient or November20Client;

    public static bool Matches(string? commit) =>
        IsNovember20(commit) || string.Equals(commit, UpstreamCommit, StringComparison.OrdinalIgnoreCase);

    internal static string Date(string commit) => IsNovember20(commit) ? "2026-11-20" : "2026-11-18";

    internal static bool IsClient(string? version) =>
        version is ClientVersion or EmbeddedClientVersion or CodeRuntimeClientVersion
            or LegacyRendererClientVersion or ArmHudClientVersion or MobileControlsClientVersion
            or FixedWgpuClientVersion or CandidateClientVersion || IsNovember20Client(version);

    public static bool SupportsNativeSource(string patch, string client) =>
        (patch, client) is (EmbeddedPatchVersion, EmbeddedClientVersion)
            or (CodeRuntimePatchVersion, CodeRuntimeClientVersion)
            or (LegacyRendererPatchVersion, LegacyRendererClientVersion)
            or (ArmHudPatchVersion, ArmHudClientVersion)
            or (MobileControlsPatchVersion, MobileControlsClientVersion)
            or (FixedWgpuPatchVersion, FixedWgpuClientVersion)
            or (CandidatePatchVersion, CandidateClientVersion)
            || (patch == November20Patch && IsNovember20Client(client));

    internal static bool UsesPackagedCodeRuntime(string? client, TargetProfile target) =>
        (client is CodeRuntimeClientVersion or LegacyRendererClientVersion or ArmHudClientVersion
            or MobileControlsClientVersion or FixedWgpuClientVersion or CandidateClientVersion
            || IsNovember20Client(client))
            && target is TargetProfile.DesktopX64 or TargetProfile.DesktopX86 or TargetProfile.SurfaceProX;

    internal static bool UsesLegacyArm32Wasm(string? client, TargetProfile target) =>
        IsNovember20Client(client) && target == TargetProfile.WindowsMobile;

    internal static bool UsesWasmRuntime(string? client, TargetProfile target) =>
        UsesPackagedCodeRuntime(client, target) || UsesLegacyArm32Wasm(client, target);

    internal static bool UsesFixedWgpu(string? client) =>
        client is FixedWgpuClientVersion or StreamingAudioClientVersion || IsNovember20Client(client);

    internal static bool MatchesRecipe(string commit, string patch, string client) =>
        IsNovember20(commit)
            ? patch == November20Patch && IsNovember20Client(client)
            : string.Equals(commit, UpstreamCommit, StringComparison.OrdinalIgnoreCase)
                && ((patch, client) is (PatchVersion, ClientVersion)
                    || (!IsNovember20Client(client) && SupportsNativeSource(patch, client)));

    internal static string GameBuildId(string architecture, string patch = PatchVersion, string client = ClientVersion)
    {
        var commit = IsNovember20Client(client) ? November20Commit : UpstreamCommit;
        return $"mchorizonuwp-{client}-patch-{patch}-{architecture}-{commit}";
    }
}

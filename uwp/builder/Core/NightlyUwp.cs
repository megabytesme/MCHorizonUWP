namespace Horizon.Builder.Core;

public static class NightlyUwp
{
    public const string UpstreamCommit = "0b14f8acfd97fa2c750e42795249dcc981178ca4";
    public const string PatchVersion = "0.1.5";
    public const string ClientVersion = "0.1.6";
    public const string CandidatePatchVersion = "0.1.8";
    public const string CandidateClientVersion = "0.1.9";
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

    public static bool Matches(string? commit) => string.Equals(commit, UpstreamCommit, StringComparison.OrdinalIgnoreCase);

    internal static bool IsClient(string? version) =>
        version is ClientVersion or EmbeddedClientVersion or CodeRuntimeClientVersion or CandidateClientVersion;

    public static bool SupportsNativeSource(string patch, string client) =>
        (patch, client) is (EmbeddedPatchVersion, EmbeddedClientVersion)
            or (CodeRuntimePatchVersion, CodeRuntimeClientVersion)
            or (CandidatePatchVersion, CandidateClientVersion);

    internal static bool UsesPackagedCodeRuntime(string? client, TargetProfile target) =>
        (client is CodeRuntimeClientVersion or CandidateClientVersion)
            && target is TargetProfile.DesktopX64 or TargetProfile.DesktopX86 or TargetProfile.SurfaceProX;

    internal static bool MatchesRecipe(string patch, string client) =>
        (patch, client) is (PatchVersion, ClientVersion) || SupportsNativeSource(patch, client);

    internal static string GameBuildId(string architecture, string patch = PatchVersion, string client = ClientVersion) =>
        $"mchorizonuwp-{client}-patch-{patch}-{architecture}-{UpstreamCommit}";
}

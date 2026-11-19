namespace Horizon.Builder.Core;

public static class NightlyUwp
{
    public const string UpstreamCommit = "0b14f8acfd97fa2c750e42795249dcc981178ca4";
    public const string PatchVersion = "0.1.5";
    public const string ClientVersion = "0.1.6";
    internal const int ResourceSchema = 7;
    internal const int PrepareOutputVersion = 5;
    public const string PublicNativeModsUnavailable =
        "Public native SDK mods are unavailable for this Nightly because the SDK build and mixin pipeline have not been validated for UWP.";

    // GitHub codeload ZIP for megabytesme/wgpu-dx11 commit 4c24e58de1ee12b9dd3a81cc4490662353011c30.
    internal const string WgpuArchiveHash = "4c8769b47eea565ca994d4699ff72e70f6647613b6205c8daa52f2b04a743eaa";
    internal const string WgpuPatchHash = "ee0a57e971221a3b705ca8434b67ea2f2909fb8b3b21444163e0bf5be44046e4";

    public static bool Matches(string? commit) => string.Equals(commit, UpstreamCommit, StringComparison.OrdinalIgnoreCase);

    internal static string GameBuildId(string architecture) => $"mchorizonuwp-{ClientVersion}-patch-{PatchVersion}-{architecture}-{UpstreamCommit}";
}

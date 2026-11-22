using System.Text.Json;

namespace Horizon.Builder.Core;

public sealed record PackagedNativeOutput(string MainBundle, string OptionalBundle);

/// <summary>Builds trusted native hook sources as a separate executable optional package.</summary>
internal sealed class PackagedNativeBuild(string snapshot, string architecture, string staged, string[] ids)
{
    internal static async Task<PackagedNativeBuild> StageAsync(string snapshot, string python, string architecture,
        string buildId, IReadOnlyList<string> sources, IProgress<string>? progress)
    {
        const string stager = "uwp/native-mod-probe/stage-packaged.py";
        if (!File.Exists(Path.Combine(snapshot, stager)))
            throw new InvalidOperationException("The authenticated client bundle does not include native modpack staging.");
        if (architecture is not ("x64" or "x86" or "arm64"))
            throw new PlatformNotSupportedException("Executable optional packages require desktop Windows build 16299 or newer.");
        var arguments = new List<string>
        {
            "-I", stager, "--workspace", snapshot, "--architecture", architecture,
            "--game-build-id", buildId, "--trust-native-source",
        };
        foreach (var source in sources)
        {
            arguments.Add("--source");
            arguments.Add(source);
        }
        await LocalBuildRunner.RunProcess(python, snapshot, arguments, progress: progress, captureFailureOutput: true);
        var staged = Path.Combine(snapshot, "uwp", "game-bridge", "packaged-native");
        using var record = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(staged, "build-record.json")));
        var ids = record.RootElement.GetProperty("mods").EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()
                ?? throw new InvalidDataException("A native mod build record is missing its ID."))
            .ToArray();
        if (ids.Length != sources.Count || ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || ids.Any(id => id.Length is < 1 or > 64
                || id.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')))
            throw new InvalidDataException("The native mod build record has invalid or duplicate mod IDs.");
        return new(snapshot, architecture, staged, ids);
    }

    private string DllDirectory => Path.Combine(staged, "dll");

    internal async Task BuildAsync(string cargo, string powershell, string sdk, string msvc, string rustTarget,
        IReadOnlyDictionary<string, string> cargoEnvironment, IReadOnlyDictionary<string, string> hostEnvironment,
        IProgress<string>? progress)
    {
        var targetDirectory = cargoEnvironment["CARGO_TARGET_DIR"];
        var arguments = new List<string> { "+" + LocalRecipe.RustToolchain };
        var wasmConfig = Path.Combine(".cargo", "wasmtime-uwp.toml");
        if (File.Exists(Path.Combine(snapshot, wasmConfig))) arguments.AddRange(["--config", wasmConfig]);
        arguments.AddRange(["build", "--offline", "--locked", "--manifest-path", "uwp/game-bridge/Cargo.toml",
            "-Z", "build-std=std,panic_abort", "--target", rustTarget, "--release", "--target-dir", targetDirectory]);
        foreach (var id in ids) arguments.AddRange(["-p", "horizon-uwp-native-" + id.Replace('_', '-')]);
        progress?.Report($"Building {ids.Length} separate native mod libraries.");
        await LocalBuildRunner.RunProcess(cargo, snapshot, arguments, cargoEnvironment,
            progress: progress, captureFailureOutput: true);

        Directory.CreateDirectory(DllDirectory);
        var hostKit = Path.Combine(snapshot, "uwp", "host", "prebuilt", architecture);
        foreach (var id in ids)
        {
            var library = "horizon_uwp_mod_" + id;
            progress?.Report($"Strict-linking native mod {id}.");
            await LocalBuildRunner.RunProcess(powershell, snapshot,
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/host/strict-bridge-link.ps1",
                    "-Architecture", architecture,
                    "-RustLibrary", Path.Combine(targetDirectory, rustTarget, "release", library + ".lib"),
                    "-HostKit", hostKit, "-MsvcToolsetRoot", msvc, "-SdkRoot", sdk,
                    "-RustLld", cargoEnvironment["HORIZON_UWP_RUST_LLD"],
                    "-SentinelObject", Path.Combine(hostKit, "dead-import-sentinels.obj"),
                    "-Output", Path.Combine(DllDirectory, library + ".dll"), "-NativeMod"],
                hostEnvironment, progress: progress, captureFailureOutput: true);
        }
    }

    internal async Task<PackagedNativeOutput> PackageAsync(string powershell, string sdk, SignedPackageOutput baseOutput,
        PackageSigningIdentity identity, Version version, IProgress<string>? progress)
    {
        var output = Path.Combine(Path.GetDirectoryName(baseOutput.Client)!, "native-modpack");
        progress?.Report("Signing the main and native modpack bundles as an executable related set.");
        await LocalBuildRunner.RunProcess(powershell, snapshot,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "uwp/native-mod-probe/package-modpack.ps1",
                "-MainAppx", baseOutput.Client, "-StagedSources", staged,
                "-DllDirectory", DllDirectory, "-OutputDirectory", output, "-Version", version.ToString(),
                "-SdkRoot", sdk, "-SigningThumbprint", identity.Thumbprint, "-AllowSameMainVersion"],
            progress: progress, captureFailureOutput: true);
        var main = Path.Combine(output, "main.appxbundle");
        var optional = Path.Combine(output, "optional.appxbundle");
        if (!File.Exists(main) || !File.Exists(optional))
            throw new InvalidDataException("The native modpack packaging step did not produce both related-set bundles.");
        return new(main, optional);
    }
}

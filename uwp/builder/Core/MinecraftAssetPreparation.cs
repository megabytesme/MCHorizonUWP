using System.Diagnostics;

namespace Horizon.Builder.Core;

/// <summary>Builds Horizon's pinned downloader and preparer in a private authenticated release workspace.</summary>
public static class MinecraftAssetPreparation
{
    public sealed record PrefetchResult(string MinecraftDirectory, int LocalFilesReused, int NetworkFilesFetched, long BytesFetched);
    public static string CacheRoot => MinecraftAssetPrefetcher.CacheRoot;

    public static async Task<PrefetchResult> PrefetchAsync(string source, bool ownershipVerified, string? minecraftDirectory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        Func<long, long, Task<bool>>? confirmInsufficientSpace = null)
    {
        var result = await MinecraftAssetPrefetcher.PrefetchAsync(source, ownershipVerified, minecraftDirectory, progress,
            cancellationToken, confirmInsufficientSpace: confirmInsufficientSpace);
        return new(result.MinecraftDirectory, result.LocalFilesReused, result.NetworkFilesFetched, result.BytesFetched);
    }

    public static async Task<string> PrepareAsync(PreparedRelease release, BuildSelection selection, bool ownershipVerified,
        IProbe probe, IProgress<string>? progress = null, string? minecraftDirectory = null, CancellationToken cancellationToken = default,
        Func<long, long, Task<bool>>? confirmInsufficientSpace = null)
    {
        if (!ownershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition first.");
        LocalBuildRunner.ValidatePreparedRelease(release, selection);
        var source = Path.GetFullPath(release.SourceDirectory);
        var workspace = Directory.GetParent(source)?.FullName ?? throw new InvalidOperationException("The release workspace is invalid.");
        var privateData = MinecraftAssetPrefetcher.CacheRoot;
        await MinecraftAssetPrefetcher.PrefetchAsync(source, ownershipVerified, minecraftDirectory, progress,
            cancellationToken, confirmInsufficientSpace: confirmInsufficientSpace);
        var tool = await StageHelperAsync(source, workspace);
        var cargo = probe.FindOnPath("cargo.exe") ?? throw new InvalidOperationException("Cargo is required to prepare Minecraft resources.");
        var sdk = PrivateWindowsSdk.FindReady(probe, ["x64"]) ?? throw new InvalidOperationException("The x64 Windows SDK is missing.");
        var msvc = VisualCppInstallation.FindLinkToolset(probe, "x64")?.Toolset ?? throw new InvalidOperationException("The Microsoft link libraries are missing.");
        var clang = ClangFinder.Find(probe) ?? throw new InvalidOperationException("The compact LLVM C compiler is missing.");
        var rustLld = await LocalRecipe.FindRustLinkerAsync(probe);
        var compact = PrebuiltToolchain.Create(source, sdk, "x64", clang, rustLld, msvc, Path.Combine(workspace, "asset-toolchain"), probe);
        var environment = new Dictionary<string, string>(compact.Cargo, StringComparer.OrdinalIgnoreCase);
        environment["CARGO_BUILD_JOBS"] = LocalBuildRunner.CurrentCargoJobCount().ToString(System.Globalization.CultureInfo.InvariantCulture);
        progress?.Report("Building Horizon's Minecraft resource preparation helper.");
        await LocalBuildRunner.RunProcess(cargo, tool, ["+" + LocalRecipe.RustToolchain, "build", "--locked", "--release", "--target", "x86_64-pc-windows-msvc",
            "--manifest-path", Path.Combine(tool, "Cargo.toml"), "--target-dir", Path.Combine(tool, "target")], environment, progress: progress);
        var executable = Path.Combine(tool, "target", "x86_64-pc-windows-msvc", "release", "horizon-uwp-assets.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(privateData);
        foreach (var (name, folder) in new[] { ("HORIZON_HOME", "game"), ("HORIZON_LAUNCHER_HOME", "launcher"),
            ("XDG_DATA_HOME", "xdg-data"), ("XDG_CONFIG_HOME", "xdg-config"), ("HORIZON_LAUNCHER_TRASH", "trash"),
            ("HORIZON_LAUNCHER_CLIPBOARD", "clipboard"), ("HORIZON_LAUNCHER_DESKTOP", "desktop") })
            start.Environment[name] = Path.Combine(workspace, "isolated", folder);
        start.Environment["PATH"] = environment["PATH"];
        start.Environment["HORIZON_LAUNCHER_NO_SELF_UPDATE"] = "1";
        start.Environment["HORIZON_NO_AUDIO"] = "1";
        progress?.Report("Preparing your prefetched Minecraft resources with Horizon.");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Minecraft resource preparation.");
        var errors = process.StandardError.ReadToEndAsync();
        var lastUpdate = Stopwatch.StartNew();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (lastUpdate.Elapsed >= TimeSpan.FromSeconds(1) && !line.StartsWith("READY ", StringComparison.Ordinal))
            {
                progress?.Report(line.Length <= 300 ? line : line[..300]);
                lastUpdate.Restart();
            }
        }
        await process.WaitForExitAsync();
        var error = (await errors).Trim();
        if (process.ExitCode != 0)
        {
            var lastLine = error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            throw new InvalidOperationException("Minecraft resource preparation failed." + (string.IsNullOrWhiteSpace(lastLine) ? "" : " " + lastLine));
        }
        var resources = Path.Combine(source, ".local", "horizon-client", LocalRecipe.MinecraftVersion);
        if (!File.Exists(Path.Combine(resources, "horizon-resources.json")))
            throw new InvalidDataException("Horizon did not produce the expected Minecraft resources.");
        return resources;
    }

    internal static async Task<string> StageHelperAsync(string source, string workspace)
    {
        var tool = Path.Combine(workspace, "asset-tool");
        if (Directory.Exists(tool) || File.Exists(tool)) throw new IOException("The resource helper workspace already exists.");
        Directory.CreateDirectory(Path.Combine(tool, "src"));
        await WriteEmbeddedAsync("Cargo.toml", Path.Combine(tool, "Cargo.toml"));
        await WriteEmbeddedAsync("Cargo.lock", Path.Combine(tool, "Cargo.lock"));
        await WriteEmbeddedAsync("main.rs", Path.Combine(tool, "src", "main.rs"));
        foreach (var name in new[] { "mojang.rs", "sources.rs" })
        {
            var origin = Path.Combine(source, "launcher", "src", name);
            if (!File.Exists(origin) || (File.GetAttributes(origin) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The authenticated release is missing Horizon's asset downloader.");
            File.Copy(origin, Path.Combine(tool, "src", name), overwrite: false);
        }
        return tool;
    }

    private static async Task WriteEmbeddedAsync(string name, string destination)
    {
        await using var input = typeof(MinecraftAssetPreparation).Assembly.GetManifestResourceStream("Horizon.Builder.Core.AssetTool." + name)
            ?? throw new InvalidOperationException("The resource preparation helper is missing.");
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await input.CopyToAsync(output);
    }
}

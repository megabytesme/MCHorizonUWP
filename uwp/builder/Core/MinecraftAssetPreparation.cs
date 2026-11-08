using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

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
        Func<long, long, Task<bool>>? confirmInsufficientSpace = null, Func<int>? requestedCargoJobs = null)
    {
        if (!ownershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition first.");
        LocalBuildRunner.ValidatePreparedRelease(release, selection);
        var source = Path.GetFullPath(release.SourceDirectory);
        var workspace = Directory.GetParent(source)?.FullName ?? throw new InvalidOperationException("The release workspace is invalid.");
        var privateData = MinecraftAssetPrefetcher.CacheRoot;
        await MinecraftAssetPrefetcher.PrefetchAsync(source, ownershipVerified, minecraftDirectory, progress,
            cancellationToken, confirmInsufficientSpace: confirmInsufficientSpace);
        var tool = await StageHelperAsync(source, workspace, progress);
        var cargo = probe.FindOnPath("cargo.exe") ?? throw new InvalidOperationException("Cargo is required to prepare Minecraft resources.");
        var sdk = PrivateWindowsSdk.FindReady(probe, ["x64"]) ?? throw new InvalidOperationException("The x64 Windows SDK is missing.");
        var msvc = VisualCppInstallation.FindLinkToolset(probe, "x64")?.Toolset ?? throw new InvalidOperationException("The Microsoft link libraries are missing.");
        var clang = ClangFinder.Find(probe) ?? throw new InvalidOperationException("The compact LLVM C compiler is missing.");
        var rustLld = await LocalRecipe.FindRustLinkerAsync(probe);
        var compact = PrebuiltToolchain.Create(source, sdk, "x64", clang, rustLld, msvc, Path.Combine(workspace, "asset-toolchain"), probe);
        var environment = new Dictionary<string, string>(compact.Cargo, StringComparer.OrdinalIgnoreCase);
        var requested = requestedCargoJobs?.Invoke() ?? 0;
        var jobs = LocalBuildRunner.CurrentCargoJobCount(requested);
        environment["CARGO_BUILD_JOBS"] = jobs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        progress?.Report($"Starting Cargo compile for the Minecraft resource helper with {jobs} parallel jobs{(requested <= 0 ? " (automatic, memory-aware)" : " (selected limit)")}. The build-speed setting is now locked for this run.");
        progress?.Report("Building Horizon's Minecraft resource preparation helper.");
        var cargoTarget = LocalBuildRunner.SharedCargoTargetDirectory;
        await LocalBuildRunner.RunProcess(cargo, tool, ["+" + LocalRecipe.RustToolchain, "build", "--locked", "--release", "--target", "x86_64-pc-windows-msvc",
            "--manifest-path", Path.Combine(tool, "Cargo.toml"), "--target-dir", cargoTarget], environment, progress: progress);
        var executable = Path.Combine(cargoTarget, "x86_64-pc-windows-msvc", "release", "horizon-uwp-assets.exe");
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

    internal static async Task<string> StageHelperAsync(string source, string workspace, IProgress<string>? progress = null)
    {
        var inputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cargo.toml"] = ReadEmbedded("Cargo.toml"),
            ["Cargo.lock"] = ReadEmbedded("Cargo.lock"),
            [Path.Combine("src", "main.rs")] = ReadEmbedded("main.rs"),
        };
        foreach (var name in new[] { "mojang.rs", "sources.rs" })
        {
            var origin = Path.Combine(source, "launcher", "src", name);
            if (!File.Exists(origin) || (File.GetAttributes(origin) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The authenticated release is missing Horizon's asset downloader.");
            inputs[Path.Combine("src", name)] = await File.ReadAllBytesAsync(origin);
        }

        var fingerprint = Fingerprint(inputs);
        var canonical = Path.Combine(workspace, "asset-tool");
        if (IsReusable(canonical, inputs))
        {
            progress?.Report("Reusing the verified Minecraft resource helper workspace.");
            return canonical;
        }

        // A leftover helper may be incomplete, or may belong to a different source
        // release. Keep it intact and use a workspace keyed by the exact helper inputs.
        var keyed = Path.Combine(workspace, "asset-tool-" + fingerprint[..16]);
        if (IsReusable(keyed, inputs))
        {
            progress?.Report("Reusing the verified Minecraft resource helper workspace.");
            return keyed;
        }
        var destination = Directory.Exists(keyed) || File.Exists(keyed)
            ? keyed + "-" + Guid.NewGuid().ToString("N")
            : keyed;
        var staging = Path.Combine(workspace, ".asset-tool-stage-" + Guid.NewGuid().ToString("N"));
        progress?.Report("Preparing a private Minecraft resource helper workspace.");
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "src"));
            foreach (var (relative, bytes) in inputs)
            {
                var path = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
            }
            Directory.Move(staging, destination);
            return destination;
        }
        catch (IOException) when (IsReusable(destination, inputs))
        {
            // Another build using the same authenticated release completed the
            // same helper concurrently. Reuse it and discard only our staging tree.
            progress?.Report("Reusing the verified Minecraft resource helper workspace.");
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static byte[] ReadEmbedded(string name)
    {
        using var input = typeof(MinecraftAssetPreparation).Assembly.GetManifestResourceStream("Horizon.Builder.Core.AssetTool." + name)
            ?? throw new InvalidOperationException("The resource preparation helper is missing.");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static string Fingerprint(IReadOnlyDictionary<string, byte[]> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, bytes) in inputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var nameBytes = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
            hash.AppendData(BitConverter.GetBytes(nameBytes.Length));
            hash.AppendData(nameBytes);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsReusable(string root, IReadOnlyDictionary<string, byte[]> expected)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    var relative = Path.GetRelativePath(root, path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!string.Equals(relative, "src", StringComparison.OrdinalIgnoreCase)) return false;
                        pending.Push(path);
                        continue;
                    }
                    if (!expected.TryGetValue(relative, out var bytes) || !found.Add(relative)
                        || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return false;
                }
            }
            return found.Count == expected.Count && expected.Keys.All(found.Contains);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

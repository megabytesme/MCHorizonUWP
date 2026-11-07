using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>Reuses or downloads the pinned 26.3 client and assets before build tools are ready.</summary>
internal static class MinecraftAssetPrefetcher
{
    private const string Version = "26.3";
    private const int MaxFileBytes = 256 * 1024 * 1024;
    private static readonly string[] AllowedHosts = ["piston-meta.mojang.com", "piston-data.mojang.com", "resources.download.minecraft.net"];
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = MaxFileBytes
    };

    internal sealed record Result(string MinecraftDirectory, int LocalFilesReused, int NetworkFilesFetched, long BytesFetched);
    private sealed record Asset(string Url, string Sha1, long? Size, string Destination);

    internal static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Assets", Version);

    internal static async Task<Result> PrefetchAsync(string source, bool ownershipVerified, string? selectedMinecraftDirectory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, HttpMessageHandler? testHandler = null,
        string? testCacheRoot = null, Func<long, long, Task<bool>>? confirmInsufficientSpace = null,
        long? testAvailableSpaceBytes = null)
    {
        if (!ownershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition first.");
        var lockPath = Path.Combine(Path.GetFullPath(source), "research-lock.json");
        using var lockJson = JsonDocument.Parse(await File.ReadAllBytesAsync(lockPath, cancellationToken));
        var minecraft = lockJson.RootElement.GetProperty("minecraft");
        if (minecraft.GetProperty("version").GetString() != Version) throw new InvalidDataException("The selected source does not pin Minecraft 26.3.");
        var manifestSha1 = RequiredHash(minecraft, "manifest_sha1");
        var clientSha1 = RequiredHash(minecraft, "client_sha1");
        var cacheRoot = testCacheRoot ?? CacheRoot;
        var gameDirectory = Path.Combine(cacheRoot, "minecraft");
        var cacheDrive = Path.GetPathRoot(cacheRoot) ?? throw new InvalidOperationException("The Minecraft asset cache path is invalid.");

        var http = testHandler is null ? Http : new HttpClient(testHandler) { Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = MaxFileBytes };
        try
        {
            progress?.Report("Checking Mojang's pinned Minecraft 26.3 metadata.");
            var metadataUrl = $"https://piston-meta.mojang.com/v1/packages/{manifestSha1}/{Version}.json";
            var metadata = await GetBytesAsync(http, metadataUrl, cancellationToken);
            VerifyBytes(metadata, manifestSha1, null, "Minecraft version metadata");
            var versionDirectory = Path.Combine(gameDirectory, "versions", Version);
            var versionMetadataPath = Path.Combine(versionDirectory, Version + ".json");
            using var manifest = JsonDocument.Parse(metadata);
            var root = manifest.RootElement;
            var assetIndex = root.GetProperty("assetIndex");
            var assetIndexId = SafeSegment(RequiredString(assetIndex, "id"));
            var assetIndexSha1 = RequiredHash(assetIndex, "sha1");
            // The pinned version metadata is itself SHA-1 verified against research-lock.json.
            // It is therefore authoritative for the asset index hash, including when Mojang
            // republishes the same 26.3 version with a newer index than the source lock knew.
            // Verify the index against that hash below, then verify each downloaded object.
            progress?.Report("Verifying the asset index listed by the pinned Minecraft version metadata.");
            var assetIndexUrl = RequiredString(assetIndex, "url");
            var assetIndexBytes = await GetBytesAsync(http, assetIndexUrl, cancellationToken);
            VerifyBytes(assetIndexBytes, assetIndexSha1, OptionalSize(assetIndex), "Minecraft asset index");
            var indexPath = Path.Combine(gameDirectory, "assets", "indexes", assetIndexId + ".json");
            using var index = JsonDocument.Parse(assetIndexBytes);

            var localRoots = FindLocalMinecraftRoots(selectedMinecraftDirectory);
            var client = root.GetProperty("downloads").GetProperty("client");
            string? localClient = null;
            foreach (var localRoot in localRoots)
            {
                var candidate = Path.Combine(localRoot, "versions", Version, Version + ".jar");
                if (await MatchesAsync(candidate, clientSha1, OptionalSize(client), cancellationToken))
                {
                    localClient = candidate;
                    break;
                }
            }
            var files = new List<Asset>
            {
                new(RequiredString(client, "url"), clientSha1, OptionalSize(client), Path.Combine(versionDirectory, Version + ".jar"))
            };

            var objects = index.RootElement.GetProperty("objects").EnumerateObject();
            var unique = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in objects)
            {
                var hash = RequiredHash(item.Value, "hash");
                var size = OptionalSize(item.Value);
                if (size is > MaxFileBytes) throw new InvalidDataException("A Minecraft asset exceeds the download size limit.");
                if (unique.TryGetValue(hash, out var priorSize) && priorSize != size)
                    throw new InvalidDataException("Mojang listed the same Minecraft asset hash with different sizes.");
                unique[hash] = size;
            }
            var objectsRoot = Path.Combine(gameDirectory, "assets", "objects");
            foreach (var (hash, size) in unique)
                files.Add(new($"https://resources.download.minecraft.net/{hash[..2]}/{hash}", hash, size,
                    Path.Combine(objectsRoot, hash[..2], hash)));

            var localReused = 0;
            var alreadyCached = 0;
            var pending = new List<Asset>();
            var localCopies = new List<(string Source, Asset Asset)>();
            long bytesToAdd = 0;
            if (!await MatchesAsync(versionMetadataPath, manifestSha1, metadata.LongLength, cancellationToken))
                bytesToAdd = checked(bytesToAdd + metadata.LongLength);
            if (!await MatchesAsync(indexPath, assetIndexSha1, assetIndexBytes.LongLength, cancellationToken))
                bytesToAdd = checked(bytesToAdd + assetIndexBytes.LongLength);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await MatchesAsync(file.Destination, file.Sha1, file.Size, cancellationToken)) { alreadyCached++; continue; }
                var isClient = file.Destination.EndsWith(Version + ".jar", StringComparison.OrdinalIgnoreCase);
                var localCopy = isClient
                    ? localClient is not null && await MatchesAsync(localClient, file.Sha1, file.Size, cancellationToken) ? localClient : null
                    : await FindLocalAssetAsync(localRoots, objectsRoot, file, cancellationToken);
                if (file.Size is { } size) bytesToAdd = checked(bytesToAdd + size);
                else bytesToAdd = checked(bytesToAdd + MaxFileBytes);
                if (localCopy is not null) localCopies.Add((localCopy, file));
                else pending.Add(file);
            }

            var availableBytes = testAvailableSpaceBytes ?? new DriveInfo(cacheDrive).AvailableFreeSpace;
            if (bytesToAdd > availableBytes && confirmInsufficientSpace is not null
                && !await confirmInsufficientSpace(availableBytes, bytesToAdd))
                throw new OperationCanceledException("Minecraft asset preparation was canceled because the cache drive is low on space.", cancellationToken);

            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(gameDirectory);
            await WriteVerifiedAsync(versionMetadataPath, metadata, cancellationToken);
            await WriteVerifiedAsync(indexPath, assetIndexBytes, cancellationToken);
            foreach (var (localSource, asset) in localCopies)
            {
                if (await CopyVerifiedAsync(localSource, asset, cancellationToken)) localReused++;
                else pending.Add(asset);
            }

            var total = files.Count;
            var completed = localReused + alreadyCached;
            long bytesFetched = 0;
            var errors = new ConcurrentQueue<Exception>();
            var queue = new ConcurrentQueue<Asset>(pending);
            var lastProgress = Environment.TickCount64;
            progress?.Report($"Minecraft 26.3 assets: reusing {localReused} local files; downloading {pending.Count} of {total}.");
            var workers = Enumerable.Range(0, Math.Min(8, Math.Max(1, pending.Count))).Select(async _ =>
            {
                while (queue.TryDequeue(out var file))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var size = await DownloadVerifiedAsync(http, file, cancellationToken);
                        Interlocked.Add(ref bytesFetched, size);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        errors.Enqueue(error);
                    }
                    var done = Interlocked.Increment(ref completed);
                    var now = Environment.TickCount64;
                    if (done == total || now - Interlocked.Read(ref lastProgress) >= 1000)
                    {
                        Interlocked.Exchange(ref lastProgress, now);
                        progress?.Report($"Minecraft 26.3 assets: {done}/{total} files ready; {FormatBytes(Interlocked.Read(ref bytesFetched))} downloaded.");
                    }
                }
            }).ToArray();
            await Task.WhenAll(workers);
            cancellationToken.ThrowIfCancellationRequested();
            if (!errors.IsEmpty)
            {
                var first = errors.TryDequeue(out var error) ? error.Message : "A file could not be verified.";
                throw new IOException($"{errors.Count + 1} Minecraft asset files could not be prepared. {first}");
            }
            progress?.Report($"Minecraft 26.3 assets ready ({FormatBytes(Interlocked.Read(ref bytesFetched))} downloaded from Mojang).");
            return new(gameDirectory, localReused, pending.Count, Interlocked.Read(ref bytesFetched));
        }
        finally { if (testHandler is not null) http.Dispose(); }
    }

    private static async Task<byte[]> GetBytesAsync(HttpClient http, string address, CancellationToken cancellationToken)
    {
        ValidateUrl(address);
        using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxFileBytes) throw new InvalidDataException("A Mojang metadata file exceeds the download size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxFileBytes) throw new InvalidDataException("A Mojang metadata file exceeds the download size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task<long> DownloadVerifiedAsync(HttpClient http, Asset asset, CancellationToken cancellationToken)
    {
        ValidateUrl(asset.Url);
        var directory = Path.GetDirectoryName(asset.Destination) ?? throw new InvalidDataException("The Minecraft asset path is invalid.");
        Directory.CreateDirectory(directory);
        var temporary = asset.Destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxFileBytes) throw new InvalidDataException("A Minecraft asset exceeds the download size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[64 * 1024];
            long count = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    count += read;
                    if (count > MaxFileBytes || (asset.Size is { } expectedSize && count > expectedSize))
                        throw new InvalidDataException("A Minecraft asset exceeded the size Mojang lists.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }
            if (asset.Size is { } requiredSize && count != requiredSize) throw new InvalidDataException("A Minecraft asset has an unexpected size.");
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actual.Equals(asset.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A Minecraft asset failed its SHA-1 check.");
            File.Move(temporary, asset.Destination, overwrite: true);
            return count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string?> FindLocalAssetAsync(IReadOnlyList<string> roots, string objectsRoot, Asset file,
        CancellationToken cancellationToken)
    {
        var relative = Path.GetRelativePath(objectsRoot, file.Destination);
        foreach (var root in roots)
        {
            var local = Path.Combine(root, "assets", "objects", relative);
            if (await MatchesAsync(local, file.Sha1, file.Size, cancellationToken)) return local;
        }
        return null;
    }

    private static async Task<bool> CopyVerifiedAsync(string source, Asset file, CancellationToken cancellationToken)
    {
        if (!File.Exists(source) || !await MatchesAsync(source, file.Sha1, file.Size, cancellationToken)) return false;
        var directory = Path.GetDirectoryName(file.Destination) ?? throw new InvalidDataException("The Minecraft asset path is invalid.");
        Directory.CreateDirectory(directory);
        var temporary = file.Destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);
            if (!await MatchesAsync(temporary, file.Sha1, file.Size, cancellationToken)) return false;
            File.Move(temporary, file.Destination, overwrite: true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> MatchesAsync(string path, string sha1, long? size, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || (size is { } expected && new FileInfo(path).Length != expected)) return false;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset()).Equals(sha1, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task WriteVerifiedAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("The Minecraft metadata path is invalid.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void VerifyBytes(byte[] bytes, string sha1, long? size, string description)
    {
        if (size is { } expected && bytes.LongLength != expected) throw new InvalidDataException($"{description} has an unexpected size.");
        if (!Convert.ToHexString(SHA1.HashData(bytes)).Equals(sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{description} failed its SHA-1 check.");
    }

    private static IReadOnlyList<string> FindLocalMinecraftRoots(string? selected)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new List<string>();
        void Add(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
            }
            catch (ArgumentException) { }
            catch (IOException) { }
        }
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Add(selected);
            foreach (var name in new[] { ".minecraft", "minecraft", "meta", "data", "datastore" }) Add(Path.Combine(selected, name));
            foreach (var ancestor in new DirectoryInfo(Path.GetFullPath(selected)).Parent is { } parent ? new[] { parent.FullName } : [])
                Add(ancestor);
        }
        foreach (var name in new[] { ".minecraft", "PrismLauncher", "ModrinthApp", "ATLauncher", "com.modrinth.theseus",
                     "gdlauncher_carbon", "gdlauncher_next", "MultiMC" })
            Add(Path.Combine(appData, name));

        var candidates = new List<string>(roots);
        foreach (var root in roots.ToArray())
        {
            if (File.Exists(Path.Combine(root, "versions", Version, Version + ".jar"))) continue;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            var visited = 0;
            while (queue.Count > 0 && visited++ < 512)
            {
                var (path, depth) = queue.Dequeue();
                if (depth >= 4) continue;
                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(path); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }
                foreach (var child in children)
                {
                    try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue; }
                    catch (IOException) { continue; }
                    if (File.Exists(Path.Combine(child, "versions", Version, Version + ".jar"))) candidates.Add(child);
                    else if (Path.GetFileName(child) is "instances" or "profiles" or "data" or "minecraft" or ".minecraft" || depth < 2)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }
        return candidates.Where(path => File.Exists(Path.Combine(path, "versions", Version, Version + ".jar")))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string RequiredHash(JsonElement value, string name)
    {
        var hash = RequiredString(value, name).ToLowerInvariant();
        if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Mojang metadata contains an invalid SHA-1 hash.");
        return hash;
    }

    private static string RequiredString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new InvalidDataException("Mojang metadata is incomplete.")
            : throw new InvalidDataException("Mojang metadata is incomplete.");

    private static long? OptionalSize(JsonElement value)
        => value.TryGetProperty("size", out var property) && property.TryGetInt64(out var size) ? size : null;

    private static string SafeSegment(string value)
    {
        if (value.Length is 0 or > 80 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
            throw new InvalidDataException("Mojang metadata contains an invalid Minecraft version name.");
        return value;
    }

    private static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo) || !AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Mojang metadata contained an untrusted download URL.");
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GiB"
            : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0} MiB" : $"{bytes / 1024d:0} KiB";
}

using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace Horizon.Builder.Core;

internal sealed record NativeModTargetNormalization(string Manifest, IReadOnlyList<string> RemappedTargets);

/// <summary>Resolves mod targets against Rust's actual crate/module tree and fixes paths that only use a re-export alias.</summary>
internal sealed class NativeModTargetResolver
{
    private sealed record RustPackage(string CrateName, string RootFile);
    private sealed record ResolvedTarget(RustPackage Package, string SourceFile, string ModuleDirectory);

    private readonly RustPackage[] packages;
    private readonly ConcurrentDictionary<string, Lazy<string>> targetCache = new(StringComparer.Ordinal);

    private NativeModTargetResolver(RustPackage[] packages) => this.packages = packages;

    internal static NativeModTargetResolver Create(string sourceDirectory)
    {
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The prepared Horizon source directory is missing.");
        var packages = new List<RustPackage>();
        foreach (var manifest in Directory.EnumerateFiles(root, "Cargo.toml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(manifest);
            var packageSection = Table(text, "package");
            if (packageSection is null) continue;
            var name = Regex.Match(packageSection, "(?m)^\\s*name\\s*=\\s*\"([^\"]+)\"");
            if (!name.Success) continue;
            var libSection = Table(text, "lib");
            var libPath = libSection is null ? "src/lib.rs" : Regex.Match(libSection, "(?m)^\\s*path\\s*=\\s*\"([^\"]+)\"") is { Success: true } path
                ? path.Groups[1].Value : "src/lib.rs";
            var rootFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, libPath));
            if (File.Exists(rootFile)) packages.Add(new(name.Groups[1].Value.Replace('-', '_'), rootFile));
        }
        if (packages.Count == 0) throw new InvalidDataException("The prepared Horizon source has no Rust library crates.");
        return new(packages.ToArray());
    }

    internal NativeModTargetNormalization NormalizeManifest(string manifest)
    {
        var remapped = new List<string>();
        var normalized = Regex.Replace(manifest,
            "(?m)^(?<indent>\\s*target\\s*=\\s*\\\")(?<target>[^\\\"]+)(?<end>\\\"\\s*)$",
            match =>
            {
                var target = match.Groups["target"].Value;
                var rewritten = NormalizeTarget(target);
                if (!rewritten.Equals(target, StringComparison.Ordinal)) remapped.Add($"{target} → {rewritten}");
                return match.Groups["indent"].Value + rewritten + match.Groups["end"].Value;
            });
        return new(normalized, remapped);
    }

    internal NativeModTargetNormalization NormalizeBundle(string bundlePath, string modId)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(bundlePath);
        var entryName = $"mixins/{modId}/horizon-mixin.toml";
        var matches = archive.Entries.Where(entry => entry.FullName == entryName).ToArray();
        if (matches.Length != 1 || matches[0].Length is <= 0 or > 1024 * 1024)
            throw new InvalidDataException($"{modId} has no valid native-mixins manifest.");
        using var reader = new StreamReader(matches[0].Open());
        return NormalizeManifest(reader.ReadToEnd());
    }

    private string NormalizeTarget(string target) => targetCache.GetOrAdd(target,
        value => new Lazy<string>(() => NormalizeTargetCore(value), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private string NormalizeTargetCore(string target)
    {
        var parsed = ParseTarget(target);
        var requested = packages.FirstOrDefault(package => package.CrateName == parsed.Crate)
            ?? throw new InvalidDataException($"native-mod target '{target}' names a crate that is not in this Horizon source.");
        var direct = Resolve(requested, parsed.Modules, parsed.Item, parsed.IsType);
        if (direct.Target is not null)
            return target;
        if (direct.ModuleFound)
            throw new InvalidDataException($"native-mod target '{target}' names a function or type missing from its Horizon module.");

        var alternatives = packages.Where(package => package != requested)
            .Select(package => Resolve(package, parsed.Modules, parsed.Item, parsed.IsType))
            .Where(candidate => candidate.Target is not null)
            .Select(candidate => candidate.Target!)
            .ToArray();
        if (alternatives.Length == 1)
        {
            return alternatives[0].Package.CrateName + target[target.IndexOf("::", StringComparison.Ordinal)..];
        }

        var detail = alternatives.Length > 1 ? "multiple crates contain that target" : "the crate module or function is missing";
        throw new InvalidDataException($"native-mod target '{target}' is incompatible with this Horizon source: {detail}.");
    }

    private (bool ModuleFound, ResolvedTarget? Target) Resolve(RustPackage package, IReadOnlyList<string> modules, string item, bool isType)
    {
        if (!TryResolveModule(package.RootFile, modules, out var sourceFile, out var moduleDirectory)) return (false, null);
        var itemPattern = isType
            ? new Regex($"\\b(?:struct|enum|trait|type)\\s+{Regex.Escape(item)}\\b", RegexOptions.CultureInvariant)
            : new Regex($"\\bfn\\s+{Regex.Escape(item)}\\s*(?:<|\\()", RegexOptions.CultureInvariant);
        IEnumerable<string> candidateFiles = [sourceFile];
        if (Directory.Exists(moduleDirectory))
            candidateFiles = candidateFiles.Concat(Directory.EnumerateFiles(moduleDirectory, "*.rs", SearchOption.AllDirectories));
        foreach (var file in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (itemPattern.IsMatch(File.ReadAllText(file))) return (true, new(package, sourceFile, moduleDirectory));
        }
        return (true, null);
    }

    private static (string Crate, string[] Modules, string Item, bool IsType) ParseTarget(string target)
    {
        var plain = StripGenericArguments(target).Replace(" ", "", StringComparison.Ordinal);
        var parts = plain.Split("::", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Any(part => !Regex.IsMatch(part, "^(?:r#)?[A-Za-z_][A-Za-z0-9_]*$")))
            throw new InvalidDataException($"native-mod target '{target}' is not a supported Rust item path.");
        var crate = parts[0].StartsWith("r#", StringComparison.Ordinal) ? parts[0][2..] : parts[0];
        var body = parts.Skip(1).Select(part => part.StartsWith("r#", StringComparison.Ordinal) ? part[2..] : part).ToArray();
        var typeIndex = Array.FindIndex(body, part => char.IsUpper(part[0]));
        var moduleCount = typeIndex >= 0 ? typeIndex : body.Length - 1;
        if (moduleCount < 0 || moduleCount >= body.Length) throw new InvalidDataException($"native-mod target '{target}' is incomplete.");
        var item = body[^1];
        var isType = char.IsUpper(item[0]);
        if (!isType && typeIndex >= 0 && typeIndex == body.Length - 1)
            throw new InvalidDataException($"native-mod target '{target}' does not name a supported Rust item.");
        return (crate, body.Take(moduleCount).ToArray(), item, isType);
    }

    private static string StripGenericArguments(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var depth = 0;
        foreach (var character in value)
        {
            if (character == '<') { depth++; continue; }
            if (character == '>' && depth > 0) { depth--; continue; }
            if (depth == 0) result.Append(character);
        }
        return result.ToString();
    }

    private static bool TryResolveModule(string rootFile, IReadOnlyList<string> modules, out string sourceFile, out string moduleDirectory)
    {
        sourceFile = Path.GetFullPath(rootFile);
        moduleDirectory = Path.GetDirectoryName(sourceFile)!;
        if (!File.Exists(sourceFile)) return false;
        var content = File.ReadAllText(sourceFile);
        foreach (var module in modules)
        {
            var declaration = FindModuleDeclaration(content, module);
            if (declaration is null) return false;
            moduleDirectory = Path.Combine(moduleDirectory, module);
            if (declaration.Value.InlineContent is { } inline)
            {
                content = inline;
                continue;
            }
            var moduleBase = moduleDirectory;
            var candidates = declaration.Value.ExplicitPath is { } path
                ? new[] { Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, path)) }
                : new[] { moduleBase + ".rs", Path.Combine(moduleBase, "mod.rs") };
            sourceFile = candidates.FirstOrDefault(File.Exists) ?? "";
            if (sourceFile.Length == 0) return false;
            content = File.ReadAllText(sourceFile);
        }
        return true;
    }

    private static (string? ExplicitPath, string? InlineContent)? FindModuleDeclaration(string source, string module)
    {
        var pattern = $"(?m)^\\s*(?:#\\[path\\s*=\\s*\"(?<path>[^\"]+)\"\\]\\s*)?(?:pub(?:\\s*\\([^)]*\\))?\\s+)?mod\\s+{Regex.Escape(module)}\\s*(?<end>[;{{])";
        var match = Regex.Match(source, pattern, RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var explicitPath = match.Groups["path"].Success ? match.Groups["path"].Value : null;
        if (match.Groups["end"].Value == ";") return (explicitPath, null);
        var open = match.Index + match.Length - 1;
        var close = FindClosingBrace(source, open);
        if (close < 0) return null;
        return (null, source[(open + 1)..close]);
    }

    private static int FindClosingBrace(string source, int open)
    {
        var depth = 0;
        var commentDepth = 0;
        var state = 0; // 0 code, 1 line comment, 2 block comment, 3 string, 4 char
        for (var index = open; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (state == 1) { if (current == '\n') state = 0; continue; }
            if (state == 2)
            {
                if (current == '/' && next == '*') { commentDepth++; index++; }
                else if (current == '*' && next == '/')
                {
                    commentDepth--;
                    index++;
                    if (commentDepth == 0) state = 0;
                }
                continue;
            }
            if (state == 3 || state == 4)
            {
                if (current == '\\') { index++; continue; }
                if (state == 3 && current == '"' || state == 4 && current == '\'') state = 0;
                continue;
            }
            if (current == '/' && next == '/') { state = 1; index++; continue; }
            if (current == '/' && next == '*') { state = 2; commentDepth = 1; index++; continue; }
            if (current == '"') { state = 3; continue; }
            if (current == '\'') { state = 4; continue; }
            if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static string? Table(string text, string name)
    {
        var match = Regex.Match(text, $"(?ms)^\\[{Regex.Escape(name)}\\]\\s*(.*?)(?=^\\[|\\z)");
        return match.Success ? match.Groups[1].Value : null;
    }
}

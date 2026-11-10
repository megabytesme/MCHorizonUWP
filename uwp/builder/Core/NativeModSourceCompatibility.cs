using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Horizon.Builder.Core;

/// <summary>Checks mod struct literals against the selected Horizon source before showing the picker.</summary>
internal sealed class NativeModSourceCompatibility
{
    private const long MaxExpandedModSource = 16L * 1024 * 1024;
    private const int MaxModSourceFile = 2 * 1024 * 1024;
    private static readonly Regex StructDeclaration = new(
        @"(?m)^\s*pub\s+struct\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LocalTypeDeclaration = new(
        @"(?m)^\s*(?:pub(?:\s*\([^)]*\))?\s+)?(?:struct|enum|type)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LocalEnumDeclaration = new(
        @"(?m)^\s*(?:pub(?:\s*\([^)]*\))?\s+)?enum\s+[A-Za-z_][A-Za-z0-9_]*\s*\{",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PublicField = new(
        @"(?m)^\s*pub\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StructLiteral = new(
        @"\b(?<name>[A-Z][A-Za-z0-9_]*)\s*\{",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record SourceStruct(string Name, HashSet<string> Fields);
    private readonly IReadOnlyDictionary<string, SourceStruct> structs;

    private NativeModSourceCompatibility(IReadOnlyDictionary<string, SourceStruct> structs) => this.structs = structs;

    internal static NativeModSourceCompatibility Create(string sourceDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var definitions = new Dictionary<string, List<SourceStruct>>(StringComparer.Ordinal);
        foreach (var folder in new[] { "crates", "uwp" })
        {
            var root = Path.Combine(sourceRoot, folder);
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.rs", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                if (info.Length > MaxModSourceFile) continue;
                var text = File.ReadAllText(path);
                foreach (Match match in StructDeclaration.Matches(text))
                {
                    var open = text.IndexOf('{', match.Index + match.Length - 1);
                    var close = MatchingBrace(text, open);
                    if (close < 0) continue;
                    var name = match.Groups["name"].Value;
                    var body = Regex.Replace(text[(open + 1)..close], @"(?m)^[ \t]*///.*(?:\r?\n|$)", "");
                    var fields = PublicField.Matches(body).Select(field => field.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
                    if (!definitions.TryGetValue(name, out var list)) definitions[name] = list = [];
                    list.Add(new(name, fields));
                }
            }
        }
        var unique = definitions.Where(entry => entry.Value.Count == 1)
            .ToDictionary(entry => entry.Key, entry => entry.Value[0], StringComparer.Ordinal);
        return new(unique);
    }

    internal IReadOnlyList<string> CheckBundle(string bundlePath, string modId)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var sourceFiles = archive.Entries.Where(entry => entry.FullName.StartsWith($"mixins/{modId}/", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".rs", StringComparison.Ordinal)).ToArray();
        long expanded = 0;
        var errors = new List<string>();
        var sourceTexts = new List<(string Path, string Text)>(sourceFiles.Length);
        var localTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in sourceFiles)
        {
            if (entry.Length > MaxModSourceFile || entry.Length > MaxExpandedModSource - expanded)
                throw new InvalidDataException($"{modId} exceeds the native-mod source preflight limit.");
            expanded += entry.Length;
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            sourceTexts.Add((entry.FullName, text));
            foreach (Match declaration in LocalTypeDeclaration.Matches(text)) localTypes.Add(declaration.Groups["name"].Value);
            foreach (Match declaration in LocalEnumDeclaration.Matches(text))
            {
                var open = text.IndexOf('{', declaration.Index + declaration.Length - 1);
                var close = MatchingBrace(text, open);
                if (close < 0) continue;
                foreach (var variant in SplitTopLevel(text[(open + 1)..close]))
                {
                    var withoutDocs = Regex.Replace(variant, @"(?m)^\s*///.*(?:\r?\n|$)", "").TrimStart();
                    var name = Regex.Match(withoutDocs, @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant);
                    if (name.Success) localTypes.Add(name.Groups["name"].Value);
                }
            }
        }
        foreach (var (path, text) in sourceTexts)
        {
            foreach (Match match in StructLiteral.Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (localTypes.Contains(name) || !structs.TryGetValue(name, out var definition)) continue;
                var prefix = text[Math.Max(0, match.Index - 12)..match.Index];
                if (Regex.IsMatch(prefix, @"(?:\b(?:struct|enum|impl)\s*|->)\s*$", RegexOptions.CultureInvariant)) continue;
                var open = text.IndexOf('{', match.Index + match.Length - 1);
                var close = MatchingBrace(text, open);
                if (close < 0) throw new InvalidDataException($"{modId} has an unclosed {name} expression in {path}.");
                var fields = FieldNames(text[(open + 1)..close], out var usesUpdate);
                if (usesUpdate) continue;
                var missing = definition.Fields.Except(fields, StringComparer.Ordinal).OrderBy(field => field, StringComparer.Ordinal).ToArray();
                if (missing.Length > 0)
                {
                    var line = 1 + text.AsSpan(0, match.Index).Count('\n');
                    errors.Add($"{name} construction at {Path.GetFileName(path)}:{line} omits Horizon fields: {string.Join(", ", missing)}.");
                }
            }
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static HashSet<string> FieldNames(string body, out bool usesUpdate)
    {
        body = Regex.Replace(body, @"(?m)^[ \t]*///.*(?:\r?\n|$)", "");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        usesUpdate = false;
        foreach (var part in SplitTopLevel(body))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("..", StringComparison.Ordinal)) { usesUpdate = true; continue; }
            var match = Regex.Match(trimmed, @"^(?:r#)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                match = Regex.Match(trimmed, @"^(?:r#)?(?<name>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.CultureInvariant);
                if (!match.Success) continue;
            }
            fields.Add(match.Groups["name"].Value);
        }
        return fields;
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var start = 0;
        var stack = new Stack<char>();
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character is '"' or '\'') { index = SkipQuoted(body, index, character); continue; }
            if (character == '/' && index + 1 < body.Length && body[index + 1] == '/')
            {
                index = body.IndexOf('\n', index + 2);
                if (index < 0) yield break;
                continue;
            }
            if (character == '/' && index + 1 < body.Length && body[index + 1] == '*')
            {
                index = SkipBlockComment(body, index);
                continue;
            }
            if (character is '(' or '[' or '{') stack.Push(character);
            else if (character is ')' or ']' or '}') { if (stack.Count > 0) stack.Pop(); }
            else if (character == ',' && stack.Count == 0) { yield return body[start..index]; start = index + 1; }
        }
        if (start < body.Length) yield return body[start..];
    }

    private static int MatchingBrace(string text, int open)
    {
        if (open < 0 || open >= text.Length || text[open] != '{') return -1;
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' || character == '\'') { index = SkipQuoted(text, index, character); continue; }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index = text.IndexOf('\n', index + 2);
                if (index < 0) return -1;
                continue;
            }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index = SkipBlockComment(text, index);
                if (index < 0) return -1;
                continue;
            }
            if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static int SkipQuoted(string text, int start, char quote)
    {
        // A Rust lifetime is not a character literal.
        if (quote == '\'' && start + 1 < text.Length && (char.IsLetter(text[start + 1]) || text[start + 1] == '_'))
        {
            var end = start + 2;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            if (end >= text.Length || text[end] != '\'') return start;
        }
        for (var index = start + 1; index < text.Length; index++)
        {
            if (text[index] == '\\') { index++; continue; }
            if (text[index] == quote) return index;
            if (text[index] == '\n' && quote == '\'') return start;
        }
        return text.Length - 1;
    }

    private static int SkipBlockComment(string text, int start)
    {
        var depth = 1;
        for (var index = start + 2; index < text.Length - 1; index++)
        {
            if (text[index] == '/' && text[index + 1] == '*') { depth++; index++; }
            else if (text[index] == '*' && text[index + 1] == '/' && --depth == 0) return index + 1;
        }
        return -1;
    }
}

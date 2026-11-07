using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>Persists whether this Windows user wants startup update checks in the builder.</summary>
public static class BuilderUpdatePreferences
{
    public static bool ReadEnabled(string? settingsPath = null)
    {
        var path = settingsPath ?? DefaultPath;
        if (!File.Exists(path)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("check_for_updates", out var enabled)
                && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? enabled.GetBoolean()
                : true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    public static void WriteEnabled(bool enabled, string? settingsPath = null)
    {
        var path = settingsPath ?? DefaultPath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("The builder settings directory is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, bool>
        {
            ["check_for_updates"] = enabled,
        });
        File.WriteAllBytes(temporaryPath, bytes);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Builder", "settings.json");
}

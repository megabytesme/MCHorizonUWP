using System.Text;

namespace Horizon.Builder.Core;

/// <summary>Retains concise dependency and build progress without recording sign-in challenges or tokens.</summary>
public static class BuilderActivityLog
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 2 * 1024 * 1024;
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCHorizonUWP", "Builder", "Logs", "builder.log");

    public static void Record(string category, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var safeCategory = Clean(category, 32);
        var safeMessage = Clean(message, 800);
        if (safeMessage.Length == 0) return;
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{safeCategory}] {safeMessage}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length + Encoding.UTF8.GetByteCount(line) > MaxLogBytes)
                {
                    var previous = LogPath + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(LogPath, previous);
                }
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string ReadAll()
    {
        lock (Gate)
        {
            try
            {
                var previous = LogPath + ".previous";
                return (File.Exists(previous) ? File.ReadAllText(previous) : "")
                    + (File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No setup or build activity has been recorded yet.");
            }
            catch (IOException error) { return "The builder log could not be read: " + error.Message; }
            catch (UnauthorizedAccessException error) { return "The builder log could not be read: " + error.Message; }
        }
    }

    private static string Clean(string value, int maxLength)
    {
        var cleaned = new string(value.Where(character => !char.IsControl(character) || character is '\t').ToArray()).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "…";
    }
}

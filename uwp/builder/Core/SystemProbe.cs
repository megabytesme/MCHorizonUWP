namespace Horizon.Builder.Core;

public sealed class SystemProbe : IProbe
{
    /// <summary>Include paths installed since the builder process started, without removing existing paths.</summary>
    public static void RefreshProcessPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        var known = new HashSet<string>(parts.Select(p => p.Trim().Trim('"')), StringComparer.OrdinalIgnoreCase);
        foreach (var scope in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            var value = Environment.GetEnvironmentVariable("Path", scope) ?? "";
            foreach (var part in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                if (expanded.Length > 0 && known.Add(expanded)) parts.Add(expanded);
            }
        }
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, parts));
    }

    public string? EnvironmentValue(string name) => Environment.GetEnvironmentVariable(name);
    public bool FileExists(string path) => File.Exists(path);
    public IEnumerable<string> Directories(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    public string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    if (IsPythonExecutionAlias(executable, candidate, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.GetAttributes(candidate)))
                        continue;
                    return candidate;
                }
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    internal static bool IsPythonExecutionAlias(string executable, string candidate, string localAppData, FileAttributes attributes)
        => executable.Equals("python.exe", StringComparison.OrdinalIgnoreCase)
            && (attributes & FileAttributes.ReparsePoint) != 0
            && Path.GetFullPath(candidate).Equals(Path.GetFullPath(Path.Combine(localAppData, "Microsoft", "WindowsApps", "python.exe")), StringComparison.OrdinalIgnoreCase);
}

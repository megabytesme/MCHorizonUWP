namespace Horizon.Builder.Core;

/// <summary>Finds a standard Visual Studio installation with the UWP tools needed by a release.</summary>
public static class VisualCppInstallation
{
    internal static (string Root, string Toolset)? FindLinkToolset(IProbe probe, string architecture)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64"))
            throw new ArgumentException("Unsupported architecture.", nameof(architecture));
        if (FindInstalledLinkToolset(probe, architecture) is { } installed) return installed;
        var privateRoot = PrivateMsvcToolset.PrivateRoot;
        return PrivateMsvcToolset.IsComplete(probe, privateRoot, architecture) ? (privateRoot, privateRoot) : null;
    }

    internal static (string Root, string Toolset)? FindInstalledLinkToolset(IProbe probe, string architecture)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64"))
            throw new ArgumentException("Unsupported architecture.", nameof(architecture));
        var roots = new[] { probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files" }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var release in new[] { "2022", "18" })
                foreach (var edition in new[] { "BuildTools", "Community", "Professional", "Enterprise" })
                {
                    var candidate = Path.Combine(root, "Microsoft Visual Studio", release, edition);
                    var directory = Path.Combine(candidate, "VC", "Tools", "MSVC");
                    var toolset = probe.Directories(directory)
                        .OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out var version) ? version : new Version(0, 0))
                        .FirstOrDefault(path => HasLibraries(probe, path, architecture));
                    if (toolset is not null) return (candidate, toolset);
                }
        return null;
    }

    private static bool HasLibraries(IProbe probe, string toolset, string architecture)
    {
        var target = Path.Combine(toolset, "lib", architecture, "store");
        return new[] { "include\\vcruntime.h", "lib\\x64\\msvcrt.lib", "lib\\x64\\vcruntime.lib",
            "lib\\x64\\oldnames.lib", "lib\\x64\\libcmt.lib", "lib\\x64\\legacy_stdio_definitions.lib" }
            .All(path => probe.FileExists(Path.Combine(toolset, path)))
            && new[] { "msvcrt.lib", "vcruntime.lib", "oldnames.lib" }.All(name => probe.FileExists(Path.Combine(target, name)));
    }

    internal static string? FindToolset(IProbe probe, string root, string architecture)
    {
        var directory = Path.Combine(root, "VC", "Tools", "MSVC");
        return probe.Directories(directory)
            .OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out var version) ? version : new Version(0, 0))
            .FirstOrDefault(path => probe.FileExists(Path.Combine(path, "bin", "Hostx64", "x64", "cl.exe"))
                && probe.FileExists(Path.Combine(path, "bin", "Hostx64", architecture, "cl.exe"))
                && probe.FileExists(Path.Combine(path, "lib", "x64", "msvcrt.lib"))
                && probe.FileExists(Path.Combine(path, "lib", "x86", "store", "references", "platform.winmd"))
                && probe.FileExists(Path.Combine(path, "lib", architecture, "store", "vcruntime.lib"))
                && probe.FileExists(Path.Combine(path, "lib", architecture, "store", "vccorlib.lib")));
    }

    internal static (string Root, string Toolset)? FindCompact(IProbe probe, string architecture)
    {
        var roots = new[] { probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files" }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var release in new[] { "2022", "18" })
                foreach (var edition in new[] { "BuildTools", "Community", "Professional", "Enterprise" })
                {
                    var candidate = Path.Combine(root, "Microsoft Visual Studio", release, edition);
                    if (FindToolset(probe, candidate, architecture) is { } toolset) return (candidate, toolset);
                }
        return null;
    }

    public static string? FindForTarget(IProbe probe, TargetProfile target)
    {
        var architecture = target switch { TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
            TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => throw new ArgumentOutOfRangeException(nameof(target)) };
        var roots = new[] { probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files" }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var release in new[] { "2022", "18" })
                foreach (var edition in new[] { "BuildTools", "Community", "Professional", "Enterprise" })
                {
                    var candidate = Path.Combine(root, "Microsoft Visual Studio", release, edition);
                    if (!probe.FileExists(Path.Combine(candidate, "VC", "Auxiliary", "Build", "vcvarsall.bat"))) continue;
                    foreach (var toolset in probe.Directories(Path.Combine(candidate, "VC", "Tools", "MSVC")))
                    {
                        bool Has(string arch) => probe.FileExists(Path.Combine(toolset, "bin", "Hostx64", arch, "cl.exe"))
                            && probe.FileExists(Path.Combine(toolset, "lib", arch, "store", "vcruntime.lib"));
                        if (Has("x64") && Has(architecture)) return candidate;
                    }
                }
        return null;
    }
}

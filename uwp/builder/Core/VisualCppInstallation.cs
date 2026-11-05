namespace Horizon.Builder.Core;

/// <summary>Finds a standard Visual Studio installation with the UWP tools needed by a release.</summary>
public static class VisualCppInstallation
{
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

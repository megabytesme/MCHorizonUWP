namespace Horizon.Builder.Core;

/// <summary>Finds an existing x64-hosted Clang suitable for ARM cross compilation.</summary>
internal static class ClangFinder
{
    internal static string? Find(IProbe probe)
    {
        if (PrivateClang.IsComplete(PrivateClang.Root, probe.FileExists)) return PrivateClang.ClangExecutable;
        if (probe.FindOnPath("clang.exe") is { } path && HasArchiver(path, probe)) return path;
        var standalone = Path.Combine(probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files", "LLVM", "bin", "clang.exe");
        if (probe.FileExists(standalone) && HasArchiver(standalone, probe)) return standalone;
        foreach (var root in new[] { probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files" }.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var release in new[] { "2022", "18" })
                foreach (var edition in new[] { "BuildTools", "Community", "Professional", "Enterprise" })
                {
                    var candidate = Path.Combine(root, "Microsoft Visual Studio", release, edition, "VC", "Tools", "Llvm", "x64", "bin", "clang.exe");
                    if (probe.FileExists(candidate) && HasArchiver(candidate, probe)) return candidate;
                }
        return null;
    }

    private static bool HasArchiver(string clang, IProbe probe) => probe.FileExists(
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(clang))!, "llvm-lib.exe"));
}

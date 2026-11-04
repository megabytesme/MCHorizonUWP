namespace Horizon.Builder.Core;

public enum TargetProfile { DesktopX64, SurfaceProX, WindowsMobile }
public enum CheckStatus { Found, Missing, Review }
public sealed record SetupCheck(string Name, CheckStatus Status, string Detail, Uri? Instructions = null);

// A scan is evidence of file presence, not a compatibility or build guarantee.
public interface IProbe
{
    string? FindOnPath(string executable);
    bool FileExists(string path);
    IEnumerable<string> Directories(string path);
    string? EnvironmentValue(string name);
}

public sealed class SetupChecker(IProbe probe)
{
    public IReadOnlyList<SetupCheck> Scan(TargetProfile profile)
    {
        var checks = new List<SetupCheck>();
        CheckPath(checks, "Git", "git.exe", "Install Git for Windows from its official publisher.", "https://git-scm.com/install/windows");
        CheckPath(checks, "Rustup", "rustup.exe", "Install Rust using the official Rustup installer.", "https://rust-lang.org/learn/get-started/");
        CheckPath(checks, "Cargo", "cargo.exe", "Install Cargo with Rustup.", "https://rust-lang.org/learn/get-started/");
        CheckPath(checks, "Python", "python.exe", "Install Python from its official publisher.", "https://www.python.org/downloads/windows/");

        var kitsRoot = Path.Combine(probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Windows Kits", "10");
        var sdkVersion = profile == TargetProfile.WindowsMobile ? "10.0.15063.0" : null;
        var sdkBins = probe.Directories(Path.Combine(kitsRoot, "bin"))
            .Where(path => Version.TryParse(Path.GetFileName(path), out _))
            .Where(path => sdkVersion is null || Path.GetFileName(path) == sdkVersion)
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path)));
        var sdk = sdkBins.FirstOrDefault(path => probe.FileExists(Path.Combine(path, "x64", "makeappx.exe"))
            && probe.FileExists(Path.Combine(path, "x64", "signtool.exe"))
            && probe.FileExists(Path.Combine(kitsRoot, "Include", Path.GetFileName(path), "um", "Windows.h")));
        checks.Add(sdk is null
            ? new("Windows SDK and packaging tools", CheckStatus.Missing,
                sdkVersion is null ? "No SDK with headers, MakeAppx and SignTool was found in Windows Kits/10. Install a compatible Windows SDK." : $"Windows SDK {sdkVersion} with headers, MakeAppx and SignTool was not found. Its availability and licensing still require verification.",
                new Uri("https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads"))
            : new("Windows SDK and packaging tools", CheckStatus.Found, $"Found headers and packaging tools for {Path.GetFileName(sdk)}. Compatibility with the selected recipe is not yet verified."));

        var vsRoots = new[] { probe.EnvironmentValue("ProgramFiles(x86)"), probe.EnvironmentValue("ProgramFiles") }
            .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path!, "Microsoft Visual Studio"));
        var arch = profile switch { TargetProfile.DesktopX64 => "x64", TargetProfile.SurfaceProX => "arm64", _ => "arm" };
        var msvc = vsRoots.SelectMany(probe.Directories).SelectMany(probe.Directories)
            .SelectMany(edition => probe.Directories(Path.Combine(edition, "VC", "Tools", "MSVC")))
            .FirstOrDefault(toolset => probe.FileExists(Path.Combine(toolset, "bin", "Hostx64", arch, "cl.exe"))
                && probe.FileExists(Path.Combine(toolset, "lib", arch, "store", "vcruntime.lib")));
        checks.Add(msvc is null
            ? new("Visual C++ UWP tools", CheckStatus.Missing, $"No x64-hosted {arch} compiler and UWP libraries found in standard Visual Studio locations. Install the matching C++ UWP workload and target tools.",
                new Uri("https://learn.microsoft.com/en-us/windows/apps/winui/winui2/getting-started"))
            : new("Visual C++ UWP tools", CheckStatus.Found, $"Found {arch} compiler and UWP libraries. The recipe must still validate its required toolset version."));
        checks.Add(new("Rust UWP standard library", CheckStatus.Review,
            "The UWP build requires a recipe-specific Rust standard-library patch and target build. Presence and compatibility have not yet been verified."));
        checks.Add(new("Build recipe and signing", CheckStatus.Review,
            profile == TargetProfile.SurfaceProX
                ? "An ARM64 build recipe is available; validate the selected source and resources separately. Its packages require signing before installation."
                : "A pinned local build recipe is available; validate the selected source and resources separately. Its packages require signing before installation."));
        return checks;
    }

    private void CheckPath(List<SetupCheck> checks, string label, string name, string missing, string instructions)
    {
        var path = probe.FindOnPath(name);
        checks.Add(path is null ? new(label, CheckStatus.Missing, missing, new Uri(instructions))
            : new(label, CheckStatus.Found, $"Found on PATH. Version and recipe compatibility have not been checked."));
    }
}

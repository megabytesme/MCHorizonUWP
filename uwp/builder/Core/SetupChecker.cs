namespace Horizon.Builder.Core;

public enum TargetProfile { DesktopX64, SurfaceProX, WindowsMobile, DesktopX86 }
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
    public IReadOnlyList<SetupCheck> Scan(TargetProfile profile, bool splitRelease = false, bool unifiedToolchain = false, bool compactToolchain = false)
    {
        var checks = new List<SetupCheck>();
        CheckPath(checks, "Git", "git.exe", "Install Git for Windows from its official publisher.", "https://git-scm.com/install/windows");
        CheckPath(checks, "Rustup", "rustup.exe", "Install Rust using the official Rustup installer.", "https://rust-lang.org/learn/get-started/");
        CheckPath(checks, "Cargo", "cargo.exe", "Install Cargo with Rustup.", "https://rust-lang.org/learn/get-started/");
        CheckPath(checks, "Python", "python.exe", "Install Python from its official publisher.", "https://www.python.org/downloads/windows/");

        var kitsRoot = Path.Combine(probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Windows Kits", "10");
        var arch = profile switch { TargetProfile.DesktopX86 => "x86", TargetProfile.DesktopX64 => "x64", TargetProfile.SurfaceProX => "arm64", _ => "arm" };
        var hostSdk = profile == TargetProfile.SurfaceProX ? "10.0.26100.0" : unifiedToolchain ? "10.0.19041.0" : splitRelease ? "10.0.17763.0" : "10.0.15063.0";
        var hostBin = Path.Combine(kitsRoot, "bin", hostSdk, "x64");
        var packageBin = Path.Combine(kitsRoot, "bin", "10.0.26100.0", "x64");
        var hasSdk = probe.FileExists(Path.Combine(hostBin, "fxc.exe"))
            && probe.FileExists(Path.Combine(kitsRoot, "Include", hostSdk, "um", "Windows.h"))
            && probe.FileExists(Path.Combine(packageBin, "makeappx.exe")) && probe.FileExists(Path.Combine(packageBin, "signtool.exe"))
            && (profile != TargetProfile.WindowsMobile || splitRelease
                || probe.FileExists(Path.Combine(kitsRoot, "Include", "10.0.16299.0", "um", "Windows.h")));
        if (compactToolchain)
        {
            var selectedSdk = PrivateWindowsSdk.FindReady(probe, [arch, "x64"]);
            checks.Add(selectedSdk is null
                ? new("Windows SDK and packaging tools", CheckStatus.Missing, $"Windows SDK 19041 is missing required {arch} or x64 host files.")
                : new("Windows SDK and packaging tools", CheckStatus.Found, $"Found SDK 19041 for {arch} and the x64 host."));
        }
        else checks.Add(!hasSdk
            ? new("Windows SDK and packaging tools", CheckStatus.Missing,
                $"The recipe needs SDK {hostSdk} headers and shader compiler, plus SDK 10.0.26100.0 packaging tools." +
                (profile == TargetProfile.WindowsMobile && !splitRelease ? " The legacy mobile shell also needs SDK 10.0.16299.0." : ""),
                new Uri("https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads"))
            : new("Windows SDK and packaging tools", CheckStatus.Found, $"Found SDK {hostSdk} headers and shader compiler and SDK 10.0.26100.0 packaging tools. Target compatibility still needs validation."));

        if (compactToolchain)
        {
            var clang = ClangFinder.Find(probe);
            checks.Add(clang is null
                ? new("LLVM C compiler", CheckStatus.Missing, "A compatible Clang compiler is not available; the builder will install its compact private copy.", new Uri("https://github.com/llvm/llvm-project/releases"))
                : new("LLVM C compiler", CheckStatus.Found, "A local Clang compiler is available; the builder will use it for Rust dependencies."));
            var msvc = VisualCppInstallation.FindLinkToolset(probe, arch);
            checks.Add(msvc is null
                ? new("Microsoft C++ link libraries", CheckStatus.Missing, $"The x64 host and {arch} UWP libraries are missing; the builder will fetch hash-pinned Microsoft packages into its private per-user cache.", new Uri("https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution"))
                : new("Microsoft C++ link libraries", CheckStatus.Found, $"Found the x64 host and {arch} UWP link libraries."));
        }
        else
        {
            var vsRoots = new[] { probe.EnvironmentValue("ProgramFiles(x86)"), probe.EnvironmentValue("ProgramFiles") }
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path!, "Microsoft Visual Studio"));
            var msvc = vsRoots.SelectMany(probe.Directories).SelectMany(probe.Directories)
                .SelectMany(edition => probe.Directories(Path.Combine(edition, "VC", "Tools", "MSVC")))
                .FirstOrDefault(toolset => probe.FileExists(Path.Combine(toolset, "bin", "Hostx64", arch, "cl.exe"))
                    && probe.FileExists(Path.Combine(toolset, "lib", arch, "store", "vcruntime.lib")));
            checks.Add(msvc is null
                ? new("Visual C++ UWP tools", CheckStatus.Missing, $"No x64-hosted {arch} compiler and UWP libraries found in standard Visual Studio locations. Install the matching C++ UWP workload and target tools.",
                    new Uri("https://learn.microsoft.com/en-us/windows/apps/winui/winui2/getting-started"))
                : new("Visual C++ UWP tools", CheckStatus.Found, $"Found {arch} compiler and UWP libraries. The recipe must still validate its required toolset version."));
        }
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

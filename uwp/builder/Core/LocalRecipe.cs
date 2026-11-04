using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record RecipeStep(string Name, string Executable, IReadOnlyList<string> Arguments);
public sealed record LocalBuildPlan(string SourceCommit, string Target, IReadOnlyList<string> Features,
    IReadOnlyList<RecipeStep> Steps, IReadOnlyList<string> Notes);
public sealed record RustToolchainFacts(string Version, bool SourceAvailable);

/// <summary>Recipe for one locally available source revision.</summary>
public static class LocalRecipe
{
    public const string SourceCommit = "4f1cd27114a8536f49f6d2c1281b6713a54320aa";
    public const string UpstreamNightlyDate = "2026-11-03";
    public const string UpstreamNightlyCommit = "f42ef42ef6ceccb3c6a20bea2481d87949a51c81";
    public const string UpstreamNightlyShortCommit = "f42ef42";
    public const string DefaultPackageVersion = "2026.1104.930.1";
    public const string UpstreamReleaseVersion = "nightly-" + UpstreamNightlyDate;
    public const string UpstreamNightly = UpstreamNightlyDate + " " + UpstreamNightlyShortCommit;
    public const string RustVersion = "1.99.1";
    public const string MinecraftVersion = "26.3";
    public const string SourceManifestVersion = "1.0.1.0";

    /// <summary>Encode a release's UTC publication time in four APPX version fields.</summary>
    public static string PackageVersionFor(DateTimeOffset published)
    {
        var utc = published.ToUniversalTime();
        return FormattableString.Invariant($"{utc.Year}.{utc.Month * 100 + utc.Day}.{utc.Hour * 100 + utc.Minute}.{utc.Second}");
    }

    public static void ConfigureManifest(XDocument manifest, BuildSelection selection)
    {
        var identity = manifest.Root?.Element(manifest.Root.Name.Namespace + "Identity")
            ?? throw new InvalidDataException("The pinned package manifest has no identity.");
        if ((string?)identity.Attribute("Name") != "HorizonUWPPrototype"
            || (string?)identity.Attribute("Publisher") != "CN=HorizonUWPPrototype"
            || (string?)identity.Attribute("Version") != SourceManifestVersion)
            throw new InvalidDataException("The pinned package identity or version did not match the recipe.");
        identity.SetAttributeValue("Version", selection.ValidatedPackageVersion);
    }

    public static LocalBuildPlan Plan(BuildSelection selection)
    {
        var packageVersion = selection.ValidatedPackageVersion;
        var clientId = selection.ValidatedMicrosoftClientId;
        var arm32 = selection.Target == TargetProfile.WindowsMobile;
        var arch = selection.Target switch
        {
            TargetProfile.DesktopX64 => "x64",
            TargetProfile.SurfaceProX => "arm64",
            TargetProfile.WindowsMobile => "arm",
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        var steps = new List<RecipeStep>
        {
            new("Snapshot pinned source", "git", ["archive", "--format=tar", SourceCommit]),
            new("Prepare Rust source copy", "powershell", ["-NoProfile", "-File", "uwp/rust-std/prepare-source.ps1", "-Destination", ".local/builder-rust-std", "-Toolchain", "stable"]),
        };
        if (arm32) steps.Add(new("Prepare ARM32 overrides", "python", ["uwp/arm32-deps/prepare.py", "--offline-arm32"]));
        var prefix = new List<string> { "+stable" };
        if (arm32) prefix.AddRange(["--config", ".local/arm32-windows-sys/patches-offline-arm32.toml"]);
        var fetch = new List<string>(prefix);
        fetch.AddRange(["fetch", "--manifest-path", "uwp/game-bridge/Cargo.toml", "--target", selection.RustTarget,
            "--target", "x86_64-pc-windows-msvc", "-Z", "build-std=std,panic_abort"]);
        steps.Add(new("Fetch Cargo dependencies", "cargo", fetch.AsReadOnly()));
        if (selection.PrepareSounds)
        {
            steps.Add(new("Build resource preparer", "cargo", ["+stable", "build", "--manifest-path", "Cargo.toml",
                "-p", "horizon-prepare", "--bin", "horizon-prepare", "--target", "x86_64-pc-windows-msvc", "--target-dir", ".local/builder-prepare-target"]));
            steps.Add(new("Prepare sound assets", "horizon-prepare", ["sounds", "--root", "<snapshot>", "--minecraft-dir", "<selected-installation>"]));
        }
        var cargo = new List<string>(prefix);
        cargo.AddRange(["build", "--offline", "--manifest-path", "uwp/game-bridge/Cargo.toml", "--features", string.Join(',', selection.Features),
            "-Z", "build-std=std,panic_abort", "--target", selection.RustTarget, "--release", "--target-dir", ".local/builder-target"]);
        steps.Add(new("Build game bridge", "cargo", cargo.AsReadOnly()));
        steps.Add(new("Strict-link host", "cmd", ["/d", "/c", "uwp/host/build-game.cmd", arch]));
        if (selection.UseResourceFramework)
            steps.Add(new("Package reusable resources", "MakeAppx", ["<prepared-resources>", "<framework-output>"]));
        steps.Add(new("Create unsigned client package", "powershell", ["-NoProfile", "-File", "uwp/host/package.ps1", "-Architecture", arch,
            "-Executable", $"uwp/host/build/game-{arch}/HorizonUWP.exe", "-OutputDirectory", ".local/builder-output",
            selection.UseResourceFramework ? "-ResourceFrameworkName" : "-Resources",
            selection.UseResourceFramework ? "<framework-name>" : "<selected-resources>"]));
        var notes = new List<string> { "Build and staging occur in a new app-owned snapshot; the selected checkout is not modified.",
            $"Based on Horizon nightly {UpstreamNightlyDate}, source {UpstreamNightlyShortCommit}; this is a UWP port, not an official Horizon binary.",
            "The renderer is checked on the device before launch; unavailable graphics devices are not offered.",
            "The resulting APPX requires matching dependencies and a user-controlled signing identity.",
            $"The isolated manifest will use package version {packageVersion}; choose a version newer than the target device's installed package.",
            "At least 10 GiB free space is required. Cargo fetches missing dependencies before the offline build and uses all available processor cores.",
            "The resource manifest version is checked; resource ownership and complete content integrity are not established by this check." };
        if (selection.PrepareSounds) notes.Add("Sound preparation copies selected resources into the build workspace and reads your Minecraft asset store; it does not modify either selection.");
        if (selection.UseResourceFramework) notes.Add("The separate unsigned resource framework contains your private prepared resources; retain it for clients using the same content and install it as a dependency.");
        if (selection.NativeDeviceVibration) notes.Add("Device vibration is an optional native mod with its own setting, separate from controller vibration; unsupported devices are ignored.");
        if (clientId is null) notes.Add("No Microsoft application ID was supplied; sign-in needs a build configured with an approved public client ID.");
        else notes.Add("Microsoft sign-in uses the configured public application ID and device-code flow; Xbox/Minecraft access depends on that registration.");
        if (arm32) notes.Add("ARM32 uses the offline TLS dependency override; account, skin and Minecraft session requests use the WinRT HTTPS adapter.");
        return new(SourceCommit, selection.RustTarget, selection.Features, steps.AsReadOnly(), notes.AsReadOnly());
    }

    public static async Task ValidateSourceAsync(string sourceDirectory, IProbe probe)
    {
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing Horizon source directory.");
        var git = probe.FindOnPath("git.exe") ?? throw new InvalidOperationException("Git is required to inspect the selected source.");
        var start = new ProcessStartInfo(git) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-C", root, "-c", "core.fsmonitor=false", "cat-file", "-e", SourceCommit + "^{commit}" }) start.ArgumentList.Add(arg);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr); // Git output can contain local paths; do not log it.
        if (process.ExitCode != 0) throw new InvalidOperationException("The selected Git repository does not contain the pinned Horizon revision.");
    }

    public static void ValidateResources(string resourcesDirectory)
    {
        var manifest = Path.Combine(Path.GetFullPath(resourcesDirectory), "horizon-resources.json");
        var file = new FileInfo(manifest);
        if (!file.Exists || file.Length > 1024 * 1024) throw new InvalidDataException("Select prepared resources with a valid Horizon manifest.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        if (!document.RootElement.TryGetProperty("minecraft", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != MinecraftVersion)
            throw new InvalidDataException("The selected resources are not for Minecraft 26.3.");
    }

    /// Presence only: preparation verifies individual sound objects separately.
    public static string? SoundWarning(string resourcesDirectory)
    {
        var minecraft = Path.Combine(Path.GetFullPath(resourcesDirectory), "assets", "minecraft");
        return File.Exists(Path.Combine(minecraft, "sounds.json")) && Directory.Exists(Path.Combine(minecraft, "sounds"))
            ? null
            : "Sound metadata or files are missing. The client will be silent until sounds are prepared from your Minecraft installation.";
    }

    public static void ValidateSoundPreparation(string resourcesDirectory, string? minecraftDirectory)
    {
        if (minecraftDirectory is null || !Directory.Exists(minecraftDirectory))
            throw new DirectoryNotFoundException("Choose a Minecraft installation to prepare sounds.");
        var minecraft = Path.Combine(Path.GetFullPath(resourcesDirectory), "assets", "minecraft");
        if (File.Exists(Path.Combine(minecraft, "sounds.json")) || Directory.Exists(Path.Combine(minecraft, "sounds")))
            throw new InvalidDataException("Selected resources already contain some sound data. Use complete resources without sound preparation, or choose a sound-free copy.");
    }

    public static IReadOnlyList<string> ToolchainBlockers(BuildSelection selection, IProbe probe, RustToolchainFacts rust)
    {
        var blockers = new List<string>();
        foreach (var tool in new[] { "git.exe", "rustup.exe", "cargo.exe", "python.exe" })
            if (probe.FindOnPath(tool) is null) blockers.Add($"{tool} was not found on PATH.");
        if (!rust.Version.StartsWith("rustc " + RustVersion + " ", StringComparison.Ordinal))
            blockers.Add($"The recipe requires stable rustc {RustVersion} and matching rust-src.");
        if (!rust.SourceAvailable) blockers.Add("The stable toolchain does not contain rust-src.");
        var pf = probe.EnvironmentValue("ProgramFiles") ?? @"C:\Program Files";
        var pfx86 = probe.EnvironmentValue("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        var arch = selection.Target switch
        {
            TargetProfile.DesktopX64 => "x64",
            TargetProfile.SurfaceProX => "arm64",
            TargetProfile.WindowsMobile => "arm",
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        var vc = arch == "arm"
            ? Path.Combine(pfx86, "Microsoft Visual Studio", "2017", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat")
            : Path.Combine(pf, "Microsoft Visual Studio", "18", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat");
        if (!probe.FileExists(vc)) blockers.Add($"The checked-in {arch} host script's Visual Studio installation was not found.");
        if (arch == "arm" && selection.PrepareSounds && !probe.FileExists(Path.Combine(pf, "Microsoft Visual Studio", "18", "Community", "VC", "Auxiliary", "Build", "vcvarsall.bat")))
            blockers.Add("Sound preparation requires the x64 Visual Studio installation used by the checked-in desktop host script.");
        if ((arch is "arm" or "arm64") && !probe.FileExists(Path.Combine(pf, "LLVM", "bin", "clang.exe")))
            blockers.Add($"The {arch.ToUpperInvariant()} recipe requires clang.exe in the standard LLVM installation.");
        var sdk = Path.Combine(pfx86, "Windows Kits", "10", "bin");
        if (!probe.FileExists(Path.Combine(sdk, "10.0.15063.0", "x64", "fxc.exe")))
            blockers.Add("The host script requires Windows SDK 10.0.15063.0 fxc.exe.");
        if (!probe.FileExists(Path.Combine(sdk, "10.0.26100.0", "x64", "makeappx.exe")))
            blockers.Add("The package script requires Windows SDK 10.0.26100.0 MakeAppx.exe.");
        return blockers.AsReadOnly();
    }

    public static async Task<RustToolchainFacts> InspectRustAsync(IProbe probe)
    {
        var rustup = probe.FindOnPath("rustup.exe");
        if (rustup is null) return new("", false);
        async Task<string> Run(params string[] args)
        {
            var start = new ProcessStartInfo(rustup) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not inspect the Rust toolchain.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            return process.ExitCode == 0 ? (await output).Trim() : "";
        }
        var version = await Run("run", "stable", "rustc", "--version");
        var sysroot = await Run("run", "stable", "rustc", "--print", "sysroot");
        return new(version, !string.IsNullOrWhiteSpace(sysroot) && probe.FileExists(Path.Combine(sysroot, "lib", "rustlib", "src", "rust", "library", "Cargo.toml")));
    }
}

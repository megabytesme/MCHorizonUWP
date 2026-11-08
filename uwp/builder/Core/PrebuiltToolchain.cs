using System.Text;

namespace Horizon.Builder.Core;

/// <summary>Builds Rust and C dependencies with Rust's linker and a client-release host kit.</summary>
internal static class PrebuiltToolchain
{
    internal static CompactEnvironment Create(string source, string sdkRoot, string architecture, string clang,
        string rustLld, string msvcToolsetRoot, string wrapperDirectory, IProbe probe)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64"))
            throw new ArgumentException("Unsupported target architecture.", nameof(architecture));
        var root = Path.GetFullPath(source);
        var sdk = Path.GetFullPath(sdkRoot);
        var linker = Path.GetFullPath(rustLld);
        var compiler = Path.GetFullPath(clang);
        var msvc = Path.GetFullPath(msvcToolsetRoot);
        var kit = Path.Combine(root, "uwp", "host", "prebuilt");
        var hostKit = Path.Combine(kit, architecture);
        if (!PrivateWindowsSdk.IsComplete(sdk, [architecture, "x64"]))
            throw new InvalidOperationException("The selected Windows SDK is incomplete.");
        foreach (var path in new[] { compiler, linker, Path.Combine(hostKit, "HorizonUWPHost.lib"),
                     Path.Combine(hostKit, "dead-import-sentinels.obj"), Path.Combine(msvc, "include", "vcruntime.h"),
                     Path.Combine(msvc, "lib", "x64", "msvcrt.lib"), Path.Combine(msvc, "lib", "x64", "vcruntime.lib"),
                     Path.Combine(msvc, "lib", "x64", "legacy_stdio_definitions.lib") })
            if (!File.Exists(path)) throw new FileNotFoundException("The verified client release or local toolchain is incomplete.", path);

        string Sdk(string area, string architectureName) => Path.Combine(sdk, area, PrivateWindowsSdk.Version, architectureName);
        var targetRuntime = Path.Combine(hostKit, "link");
        foreach (var name in new[] { "libcmt.lib", "atexit.lib", "tls_index.obj" })
            if (!File.Exists(Path.Combine(targetRuntime, name)))
                throw new FileNotFoundException("The verified client release is missing a bridge-link input.", Path.Combine(targetRuntime, name));
        var targetMsvcRuntime = Path.Combine(msvc, "lib", architecture, "store");
        foreach (var name in new[] { "msvcrt.lib", "vcruntime.lib", "oldnames.lib" })
            if (!File.Exists(Path.Combine(targetMsvcRuntime, name)))
                throw new FileNotFoundException("The selected MSVC toolset is missing a UWP bridge-link input.", Path.Combine(targetMsvcRuntime, name));

        var include = string.Join(';', Path.Combine(msvc, "include"), Sdk("Include", "ucrt"), Sdk("Include", "um"),
            Sdk("Include", "shared"), Sdk("Include", "winrt"), Sdk("Include", "cppwinrt"));
        var hostLib = Path.Combine(msvc, "lib", "x64");
        var hostUcrt = Sdk("Lib", "ucrt\\x64");
        var hostUm = Sdk("Lib", "um\\x64");
        var targetUcrt = Sdk("Lib", "ucrt\\" + architecture);
        var targetUm = Sdk("Lib", "um\\" + architecture);
        var vcRoot = Path.GetFullPath(Path.Combine(msvc, "..", "..", ".."));
        var sdkVersionBin = Path.Combine(sdk, "bin", PrivateWindowsSdk.Version);
        var wrappers = Path.GetFullPath(wrapperDirectory);
        Directory.CreateDirectory(wrappers);
        var python = probe.FindOnPath("python.exe");
        var pythonRuntimeDirectory = python is null ? null : Path.GetDirectoryName(Path.GetFullPath(python));
        // Rust's MSVC build-script executables need the VC runtime DLLs at launch time on clean PCs.
        // The builder's pinned Python bundle carries the x64 runtime used by these host tools.
        if (pythonRuntimeDirectory is not null && !File.Exists(Path.Combine(pythonRuntimeDirectory, "vcruntime140.dll")))
            pythonRuntimeDirectory = null;
        var hostLinker = WriteLinker(wrappers, "rust-host-link.cmd", linker, [
            "/NOLOGO", $"/LIBPATH:{hostLib}", $"/LIBPATH:{hostUcrt}", $"/LIBPATH:{hostUm}"]);
        var targetLinker = WriteLinker(wrappers, "rust-uwp-" + architecture + "-link.cmd", linker, [
            "/NOLOGO", "/APPCONTAINER", "/NODEFAULTLIB:kernel32.lib", $"/LIBPATH:{targetRuntime}", $"/LIBPATH:{targetMsvcRuntime}",
            $"/LIBPATH:{targetUcrt}", $"/LIBPATH:{targetUm}"]);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["INCLUDE"] = include,
            ["PATH"] = string.Join(';', new[] { pythonRuntimeDirectory, Path.GetDirectoryName(compiler)!, Path.Combine(sdk, "bin", PrivateWindowsSdk.Version, "x64"), Environment.GetEnvironmentVariable("PATH") ?? "" }
                .Where(path => !string.IsNullOrWhiteSpace(path))),
            ["VCToolsInstallDir"] = msvc.TrimEnd('\\') + "\\",
            ["VCINSTALLDIR"] = vcRoot.TrimEnd('\\') + "\\",
            ["WindowsSdkDir"] = sdk.TrimEnd('\\') + "\\",
            ["WindowsSDKVersion"] = PrivateWindowsSdk.Version + "\\",
            ["UniversalCRTSdkDir"] = sdk.TrimEnd('\\') + "\\",
            ["UCRTVersion"] = PrivateWindowsSdk.Version,
            ["WindowsSdkBinPath"] = Path.Combine(sdk, "bin") + "\\",
            ["WindowsSdkVerBinPath"] = sdkVersionBin + "\\",
            // package.ps1 must use this verified private SDK instead of looking
            // for a machine-wide Windows Kits installation.
            ["HORIZON_UWP_PREPARED_ENV"] = "1",
            ["CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER"] = hostLinker,
            ["CC_x86_64_pc_windows_msvc"] = compiler,
            ["CFLAGS_x86_64_pc_windows_msvc"] = "-DWINAPI_FAMILY=WINAPI_FAMILY_DESKTOP_APP",
        };
        var triple = architecture switch { "x86" => "I686_UWP_WINDOWS_MSVC", "x64" => "X86_64_UWP_WINDOWS_MSVC",
            "arm" => "THUMBV7A_UWP_WINDOWS_MSVC", _ => "AARCH64_UWP_WINDOWS_MSVC" };
        environment[$"CARGO_TARGET_{triple}_LINKER"] = targetLinker;
        environment[$"CC_{TargetKey(architecture)}"] = compiler;
        environment[$"CFLAGS_{TargetKey(architecture)}"] = "-DWINAPI_FAMILY=WINAPI_FAMILY_APP";
        if (File.Exists(Path.Combine(Path.GetDirectoryName(compiler)!, "llvm-lib.exe")))
        {
            environment[$"AR_{TargetKey(architecture)}"] = Path.Combine(Path.GetDirectoryName(compiler)!, "llvm-lib.exe");
            environment["AR_x86_64_pc_windows_msvc"] = Path.Combine(Path.GetDirectoryName(compiler)!, "llvm-lib.exe");
        }
        environment["HORIZON_UWP_RUST_LLD"] = linker;
        environment["HORIZON_UWP_HOST_KIT"] = hostKit;
        environment["HORIZON_UWP_SDK_ROOT"] = sdk;
        environment["HORIZON_UWP_MSVC_TOOLSET"] = msvc;
        environment["HORIZON_UWP_PREBUILT"] = "1";
        return new(environment, environment);
    }

    private static string TargetKey(string architecture) => architecture switch
    {
        "x86" => "i686_uwp_windows_msvc", "x64" => "x86_64_uwp_windows_msvc",
        "arm" => "thumbv7a_uwp_windows_msvc", _ => "aarch64_uwp_windows_msvc"
    };

    private static string WriteLinker(string directory, string name, string linker, IReadOnlyList<string> arguments)
    {
        if (new[] { directory, linker }.Concat(arguments)
            .Any(value => value.Any(character => character is '"' or '\r' or '\n' or '%')))
            throw new InvalidDataException("A toolchain path contains characters that cannot safely be used in a linker wrapper.");
        var path = Path.Combine(directory, name);
        var lines = new List<string> { "@echo off", "setlocal DisableDelayedExpansion", "\"" + linker + "\" -flavor link" };
        lines[2] += " " + string.Join(" ", arguments.Select(argument => "\"" + argument + "\"")) + " %*";
        lines.Add("exit /b %ERRORLEVEL%");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        return path;
    }
}

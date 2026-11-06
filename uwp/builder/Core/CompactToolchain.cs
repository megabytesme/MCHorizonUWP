using System.Text;

namespace Horizon.Builder.Core;

internal sealed record CompactEnvironment(IReadOnlyDictionary<string, string> Cargo, IReadOnlyDictionary<string, string> Host);

/// <summary>Constructs isolated host and UWP target environments for a verified SDK and MSVC toolset.</summary>
internal static class CompactToolchain
{
    internal static CompactEnvironment Create(string toolset, string vcRoot, string sdkRoot, string architecture, string wrapperDirectory)
    {
        if (architecture is not ("x86" or "x64" or "arm" or "arm64")) throw new ArgumentException("Unsupported target architecture.");
        toolset = SafePath(toolset);
        vcRoot = SafePath(vcRoot);
        sdkRoot = SafePath(sdkRoot);
        wrapperDirectory = SafePath(wrapperDirectory);
        if (!PrivateWindowsSdk.IsComplete(sdkRoot, [architecture, "x64"])) throw new InvalidOperationException("The selected SDK lacks the host or target files.");
        var hostBin = Path.Combine(toolset, "bin", "Hostx64", "x64");
        var targetBin = Path.Combine(toolset, "bin", "Hostx64", architecture);
        var sdkBin = Path.Combine(sdkRoot, "bin", PrivateWindowsSdk.Version, "x64");
        foreach (var path in new[] { Path.Combine(hostBin, "cl.exe"), Path.Combine(targetBin, "cl.exe"), Path.Combine(targetBin, "link.exe"),
            Path.Combine(toolset, "lib", "x64", "msvcrt.lib"), Path.Combine(toolset, "lib", architecture, "store", "vcruntime.lib") })
            if (!File.Exists(path)) throw new FileNotFoundException("The selected C++ toolset is incomplete.", path);

        string Sdk(string directory, string area) => Path.Combine(sdkRoot, directory, PrivateWindowsSdk.Version, area);
        string Lib(string arch) => string.Join(';', Path.Combine(toolset, "lib", arch, "store"), Sdk("Lib", "ucrt\\" + arch), Sdk("Lib", "um\\" + arch));
        var include = string.Join(';', Path.Combine(toolset, "include"), Path.Combine(vcRoot, "Auxiliary", "VS", "include"),
            Sdk("Include", "ucrt"), Sdk("Include", "um"), Sdk("Include", "shared"), Sdk("Include", "winrt"), Sdk("Include", "cppwinrt"));
        var libpath = string.Join(';', Path.Combine(toolset, "lib", "x86", "store", "references"),
            Path.Combine(sdkRoot, "UnionMetadata", PrivateWindowsSdk.Version), Path.Combine(sdkRoot, "References", PrivateWindowsSdk.Version));
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VCToolsInstallDir"] = toolset.TrimEnd('\\') + "\\", ["VCINSTALLDIR"] = vcRoot.TrimEnd('\\') + "\\",
            ["WindowsSdkDir"] = sdkRoot.TrimEnd('\\') + "\\", ["WindowsSDKVersion"] = PrivateWindowsSdk.Version + "\\",
            ["UniversalCRTSdkDir"] = sdkRoot.TrimEnd('\\') + "\\", ["UCRTVersion"] = PrivateWindowsSdk.Version,
            ["WindowsSdkBinPath"] = Path.Combine(sdkRoot, "bin") + "\\", ["WindowsSdkVerBinPath"] = sdkBin[..sdkBin.LastIndexOf('\\')] + "\\",
            ["INCLUDE"] = include, ["LIBPATH"] = libpath
        };
        var cargo = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = string.Join(';', hostBin, sdkBin, currentPath),
            ["LIB"] = Path.Combine(toolset, "lib", "x64") + ";" + Lib("x64")
        };
        var target = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = string.Join(';', targetBin, sdkBin, currentPath), ["LIB"] = Lib(architecture),
            ["HORIZON_UWP_PREPARED_ENV"] = "1"
        };
        Directory.CreateDirectory(wrapperDirectory);
        var wrapper = Path.Combine(wrapperDirectory, "link-" + architecture + ".cmd");
        if (File.Exists(wrapper) || Directory.Exists(wrapper)) throw new IOException("The linker wrapper already exists.");
        static string BatchLiteral(string path) => path.Replace("%", "%%", StringComparison.Ordinal);
        File.WriteAllText(wrapper, "@echo off\r\nchcp 65001 >nul\r\nsetlocal DisableDelayedExpansion\r\nset \"LIB=" + BatchLiteral(Lib(architecture))
            + "\"\r\n\"" + BatchLiteral(Path.Combine(targetBin, "link.exe")) + "\" %*\r\nexit /b %ERRORLEVEL%\r\n", new UTF8Encoding(false));
        var triple = architecture switch { "x86" => "I686", "x64" => "X86_64", "arm" => "THUMBV7A", _ => "AARCH64" };
        cargo[$"CARGO_TARGET_{triple}_UWP_WINDOWS_MSVC_LINKER"] = wrapper;
        if (architecture == "x86") cargo["CC_i686_uwp_windows_msvc"] = Path.Combine(targetBin, "cl.exe");
        return new(cargo, target);
    }

    private static string SafePath(string value)
    {
        var full = Path.GetFullPath(value);
        if (full.Any(character => character is '"' or '\r' or '\n' or ';'))
            throw new InvalidDataException("A toolchain path contains characters that cannot safely be used in a Windows command file.");
        return full;
    }
}

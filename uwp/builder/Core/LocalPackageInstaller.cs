using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Horizon.Builder.Core;

/// <summary>Installs a builder-produced signed package after the user chooses to trust its certificate.</summary>
public static class LocalPackageInstaller
{
    public static bool CanInstall(TargetProfile target, Architecture machine, Version windows) => (machine, target) switch
    {
        (Architecture.X64, TargetProfile.DesktopX64 or TargetProfile.DesktopX86) => true,
        (Architecture.X86, TargetProfile.DesktopX86) => true,
        (Architecture.Arm64, TargetProfile.SurfaceProX or TargetProfile.DesktopX86) => true,
        (Architecture.Arm64, TargetProfile.DesktopX64) => windows.Build >= 22000,
        (Architecture.Arm, TargetProfile.WindowsMobile) => true,
        _ => false,
    };

    public static async Task InstallAsync(SignedPackageOutput output, IProgress<string>? progress = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local installation requires Windows.");
        if (output.Dependencies is not { Count: > 0 } || !File.Exists(output.Client) || !File.Exists(output.Certificate)
            || output.Dependencies.Any(path => !File.Exists(path)) || output.ResourceFramework is { } resource && !File.Exists(resource))
            throw new FileNotFoundException("The signed package or one of its installation dependencies is missing.");
        using var certificate = new X509Certificate2(output.Certificate);
        var identity = PackageSigning.GetOrCreateIdentity();
        if (certificate.HasPrivateKey || certificate.Thumbprint != identity.Thumbprint || certificate.Subject != identity.Publisher)
            throw new InvalidDataException("The public certificate does not match this builder's signing identity.");
        using (var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine))
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count == 0)
            {
                progress?.Report("Approve the Windows administrator prompt to trust your package certificate.");
                var certutil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "certutil.exe");
                var trust = new ProcessStartInfo(certutil) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                foreach (var arg in new[] { "-addstore", "TrustedPeople", Path.GetFullPath(output.Certificate) }) trust.ArgumentList.Add(arg);
                using var process = Process.Start(trust) ?? throw new InvalidOperationException("Could not start certificate installation.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException("Windows could not trust the certificate. The package was not installed.");
            }
        }
        var dependencies = output.Dependencies.Select(Path.GetFullPath).ToList();
        if (output.ResourceFramework is { } framework) dependencies.Add(Path.GetFullPath(framework));
        var payload = JsonSerializer.Serialize(new { Client = Path.GetFullPath(output.Client), Dependencies = dependencies });
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; $p=ConvertFrom-Json $env:HORIZON_UWP_INSTALL_PATHS; Add-AppxPackage -Path $p.Client -DependencyPath @($p.Dependencies)" })
            start.ArgumentList.Add(arg);
        start.Environment["HORIZON_UWP_INSTALL_PATHS"] = payload;
        progress?.Report("Installing the framework dependencies and client for your Windows account.");
        using var installer = Process.Start(start) ?? throw new InvalidOperationException("Could not start package installation.");
        var stdout = installer.StandardOutput.ReadToEndAsync();
        var stderr = installer.StandardError.ReadToEndAsync();
        await installer.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        if (installer.ExitCode != 0) throw new InvalidOperationException("Windows could not install the package. Open the included installation guide and check its dependencies.");
    }
}

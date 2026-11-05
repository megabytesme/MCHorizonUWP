using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace Horizon.Builder.Core;

public sealed record PackageSigningIdentity(string Thumbprint, string Publisher);

/// <summary>Creates a user-owned local signing identity and signs private APPX copies.</summary>
public static class PackageSigning
{
    private const string FriendlyName = "MCHorizonUWP local package signing";

    public static PackageSigningIdentity GetOrCreateIdentity()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Package signing requires Windows.");
        using var mutex = new Mutex(false, @"Local\MCHorizonUWP.PackageSigningIdentity");
        var acquired = false;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("Another builder is preparing the signing identity.");
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var matches = store.Certificates.Cast<X509Certificate2>().Where(cert => cert.FriendlyName == FriendlyName).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("More than one local MCHorizonUWP signing certificate exists; select or remove the duplicate before continuing.");
            if (matches.Length == 1)
            {
                var existing = matches[0];
                if (!existing.HasPrivateKey || existing.NotBefore > DateTime.Now || existing.NotAfter <= DateTime.Now
                    || !existing.Subject.StartsWith("CN=MCHorizonUWP-", StringComparison.Ordinal))
                    throw new InvalidOperationException("The existing package signing certificate is expired, not yet valid, or missing its private key. Preserve it and restore or renew the identity before continuing.");
                return new(existing.Thumbprint, existing.Subject);
            }
            var id = Guid.NewGuid().ToString("N");
            var parameters = new CngKeyCreationParameters { KeyUsage = CngKeyUsages.Signing,
                ExportPolicy = CngExportPolicies.AllowExport, Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider };
            using var key = CngKey.Create(CngAlgorithm.Rsa, "MCHorizonUWP.Package." + id, parameters);
            using var rsa = new RSACng(key);
            var request = new CertificateRequest("CN=MCHorizonUWP-" + id, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            var usages = new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            certificate.FriendlyName = FriendlyName;
            store.Add(certificate);
            return new(certificate.Thumbprint, certificate.Subject);
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static async Task SignAsync(string unsignedPackage, string signedPackage, string? publicCertificate,
        PackageSigningIdentity identity, string signTool)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Package signing requires Windows.");
        if (identity.Thumbprint.Length != 40 || !identity.Thumbprint.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid signing certificate thumbprint.");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        // Self-signed identities are intentionally not trusted automatically.
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, identity.Thumbprint, validOnly: false);
        if (matches.Count != 1 || !matches[0].HasPrivateKey || matches[0].Subject != identity.Publisher
            || matches[0].NotBefore > DateTime.Now || matches[0].NotAfter <= DateTime.Now)
            throw new InvalidOperationException("The selected signing identity is unavailable or has changed.");
        using (var zip = ZipFile.OpenRead(unsignedPackage))
        {
            var manifests = zip.Entries.Where(entry => entry.FullName.Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (manifests.Length != 1 || manifests[0].Length > 1024 * 1024 || zip.GetEntry("AppxSignature.p7x") is not null)
                throw new InvalidDataException("The package must contain one valid manifest and be unsigned.");
            var manifest = manifests[0];
            using var input = manifest.Open();
            var document = XDocument.Load(input);
            var root = document.Root ?? throw new InvalidDataException("The package manifest is invalid.");
            if ((string?)root.Element(root.Name.Namespace + "Identity")?.Attribute("Publisher") != identity.Publisher)
                throw new InvalidDataException("The package publisher does not match the signing certificate.");
        }
        var target = Path.GetFullPath(signedPackage);
        var certificatePath = publicCertificate is null ? null : Path.GetFullPath(publicCertificate);
        if (target.Equals(certificatePath, StringComparison.OrdinalIgnoreCase) || File.Exists(target) || Directory.Exists(target)
            || certificatePath is not null && (File.Exists(certificatePath) || Directory.Exists(certificatePath)))
            throw new IOException("Choose new package and certificate output paths.");
        File.Copy(unsignedPackage, target, overwrite: false);
        var start = new ProcessStartInfo(Path.GetFullPath(signTool)) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "sign", "/fd", "SHA256", "/sha1", identity.Thumbprint, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start SignTool.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"SignTool failed (exit code {process.ExitCode}); the output was retained for diagnosis.");
        using (var signed = ZipFile.OpenRead(target))
            if (signed.GetEntry("AppxSignature.p7x") is null)
                throw new InvalidDataException("SignTool did not add an APPX signature; the output was retained for diagnosis.");
        if (certificatePath is not null)
        {
            await using var certFile = new FileStream(certificatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await certFile.WriteAsync(matches[0].Export(X509ContentType.Cert));
        }
    }
}

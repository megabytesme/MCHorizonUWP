using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Horizon.Builder.Core;

try
{
    if (args.Length == 2 && args[0] == "self-test")
    {
        var data = await File.ReadAllBytesAsync(args[1]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = Sign(data, key);
        _ = CatalogSignature.Verify(data, signature, key.ExportSubjectPublicKeyInfo());
        data[0] ^= 1;
        try { _ = CatalogSignature.Verify(data, signature, key.ExportSubjectPublicKeyInfo()); throw new InvalidOperationException("A modified catalog was accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("Ephemeral catalog signing and tamper detection passed.");
        return;
    }
    if (args.Length == 4 && args[0] == "sign")
    {
        var data = await File.ReadAllBytesAsync(args[1]);
        using var key = OpenSigningKey(args[3]);
        var signature = Sign(data, key);
        await WriteNewAsync(args[2], signature);
        Console.WriteLine("Signed catalog with the current user's certificate-store key.");
        return;
    }
    if (args.Length == 3 && args[0] == "export-public")
    {
        using var key = OpenSigningKey(args[2]);
        await WriteNewAsync(args[1], key.ExportSubjectPublicKeyInfo());
        Console.WriteLine("Exported the public verification key; the private key remains in the certificate store.");
        return;
    }
    if (args.Length == 4 && args[0] == "verify")
    {
        _ = CatalogSignature.Verify(await File.ReadAllBytesAsync(args[1]), await File.ReadAllBytesAsync(args[2]), await File.ReadAllBytesAsync(args[3]));
        Console.WriteLine("Catalog signature and schema verified.");
        return;
    }
    throw new ArgumentException("Usage: self-test <catalog> | sign <catalog> <new-signature> <certificate-thumbprint> | export-public <new-public-key> <certificate-thumbprint> | verify <catalog> <signature> <trusted-public-key>");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}

static byte[] Sign(byte[] data, ECDsa key)
{
    if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
        throw new InvalidDataException("The signing key must use P-256.");
    // Refuse to sign malformed or ambiguous catalogs.
    _ = CompatibilityCatalog.Parse(new System.Text.UTF8Encoding(false, true).GetString(data));
    return key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}

static ECDsa OpenSigningKey(string thumbprint)
{
    if (thumbprint.Length != 40 || !thumbprint.All(char.IsAsciiHexDigit)) throw new ArgumentException("Provide a 40-digit certificate thumbprint.");
    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
    var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
    if (matches.Count != 1 || !matches[0].HasPrivateKey || matches[0].NotBefore > DateTime.Now || matches[0].NotAfter <= DateTime.Now)
        throw new InvalidOperationException("The valid signing certificate and its private key were not found in the current user's certificate store.");
    return matches[0].GetECDsaPrivateKey() ?? throw new InvalidOperationException("The certificate does not contain an ECDSA private key.");
}

static async Task WriteNewAsync(string path, byte[] bytes)
{
    await using var output = new FileStream(Path.GetFullPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
    await output.WriteAsync(bytes);
}

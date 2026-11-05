using System.Security.Cryptography;
using System.Text;

namespace Horizon.Builder.Core;

/// <summary>Checks an exact catalog byte sequence against a trusted P-256 public key.</summary>
public static class CatalogSignature
{
    public static CompatibilityCatalog Verify(byte[] catalog, byte[] signature, byte[] trustedPublicKey)
    {
        if (catalog.Length > 2_000_000 || signature.Length != 64 || trustedPublicKey.Length > 1024)
            throw new InvalidDataException("Invalid signed catalog input.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(trustedPublicKey, out var bytesRead);
            if (bytesRead != trustedPublicKey.Length || key.KeySize != 256
                || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new InvalidDataException("The trusted catalog key is not P-256.");
            if (!key.VerifyData(catalog, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("The catalog signature is invalid.");
            return CompatibilityCatalog.Parse(new UTF8Encoding(false, true).GetString(catalog));
        }
        catch (CryptographicException error) { throw new InvalidDataException("The catalog key or signature is invalid.", error); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The catalog is not valid UTF-8.", error); }
    }
}

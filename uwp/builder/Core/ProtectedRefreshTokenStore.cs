using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Builder.Core;

internal interface IRefreshTokenStore
{
    bool HasToken { get; }
    string? Read();
    void Write(string refreshToken);
    void Clear();
}

/// <summary>Stores the builder's refresh token in a current-user DPAPI blob under LocalAppData.</summary>
internal sealed class ProtectedRefreshTokenStore : IRefreshTokenStore
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const int MaximumStoredBytes = 128 * 1024;
    private const string Purpose = "MCHorizonUWP.Builder.MinecraftRefreshToken.v1\n";
    private readonly string path;

    internal ProtectedRefreshTokenStore(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCHorizonUWP", "Builder", "Auth", "minecraft-session.dpapi");
    }

    public bool HasToken => File.Exists(path);

    public string? Read()
    {
        if (!File.Exists(path)) return null;
        var encrypted = File.ReadAllBytes(path);
        if (encrypted.Length is 0 or > MaximumStoredBytes) throw new InvalidDataException("The saved Microsoft sign-in is invalid. Sign in again.");
        byte[]? clear = null;
        try
        {
            clear = Unprotect(encrypted);
            var value = new UTF8Encoding(false, true).GetString(clear);
            if (!value.StartsWith(Purpose, StringComparison.Ordinal))
                throw new InvalidDataException("The saved Microsoft sign-in is invalid. Sign in again.");
            var token = value[Purpose.Length..];
            Validate(token);
            return token;
        }
        catch (CryptographicException error)
        {
            throw new InvalidDataException("The saved Microsoft sign-in cannot be opened for this Windows account. Sign in again.", error);
        }
        catch (Win32Exception error)
        {
            throw new InvalidDataException("The saved Microsoft sign-in cannot be opened for this Windows account. Sign in again.", error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Write(string refreshToken)
    {
        Validate(refreshToken);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The saved sign-in path is invalid.");
        Directory.CreateDirectory(directory);
        var clear = Encoding.UTF8.GetBytes(Purpose + refreshToken);
        byte[]? encrypted = null;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            encrypted = Protect(clear);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(encrypted);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void Validate(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 65_536 || refreshToken.Any(char.IsControl))
            throw new InvalidDataException("Microsoft returned an invalid refresh token.");
    }

    private static byte[] Protect(byte[] value) => Transform(value, protect: true);
    private static byte[] Unprotect(byte[] value) => Transform(value, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        var entropy = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose));
        var input = Allocate(value);
        var optionalEntropy = Allocate(entropy);
        try
        {
            DataBlob output;
            var success = protect
                ? CryptProtectData(ref input, "MCHorizonUWP Builder sign-in", ref optionalEntropy, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref optionalEntropy, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (output.Length is <= 0 or > MaximumStoredBytes) throw new InvalidDataException("The saved Microsoft sign-in has an invalid size.");
                var bytes = new byte[output.Length];
                Marshal.Copy(output.Data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { _ = LocalFree(output.Data); }
        }
        finally
        {
            Free(input);
            Free(optionalEntropy);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void Free(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        var clear = new byte[blob.Length];
        Marshal.Copy(clear, 0, blob.Data, clear.Length);
        Marshal.FreeHGlobal(blob.Data);
        CryptographicOperations.ZeroMemory(clear);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

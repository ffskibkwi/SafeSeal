using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SafeSeal.Core;

public static class VaultManager
{
    private const string EntropySuffix = "SafeSealV1";

    public static void Save(byte[] rawData, string path)
    {
        if (rawData is null)
        {
            throw new ArgumentException("Raw data cannot be null.", nameof(rawData));
        }

        if (rawData.Length == 0)
        {
            throw new ArgumentException("Raw data cannot be empty.", nameof(rawData));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));
        }

        byte[] plaintext = new byte[rawData.Length];
        Buffer.BlockCopy(rawData, 0, plaintext, 0, rawData.Length);

        GCHandle plaintextHandle = default;
        bool isPinned = false;
        byte[] entropy = Array.Empty<byte>();
        byte[] hmac = Array.Empty<byte>();
        byte[] encrypted = Array.Empty<byte>();
        byte[] headerBytes = Array.Empty<byte>();

        try
        {
            plaintextHandle = GCHandle.Alloc(plaintext, GCHandleType.Pinned);
            isPinned = true;

            entropy = DeriveEntropy();

            using (var hmacProvider = new HMACSHA256(entropy))
            {
                hmac = hmacProvider.ComputeHash(plaintext);
            }

            encrypted = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);

            var header = new SealFileHeader(1, 0, hmac);
            headerBytes = header.ToBytes();

            using var stream = File.Create(path);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(encrypted, 0, encrypted.Length);
        }
        finally
        {
            if (isPinned)
            {
                plaintextHandle.Free();
            }

            Array.Clear(plaintext, 0, plaintext.Length);
            Array.Clear(entropy, 0, entropy.Length);
            Array.Clear(hmac, 0, hmac.Length);
            Array.Clear(encrypted, 0, encrypted.Length);
            Array.Clear(headerBytes, 0, headerBytes.Length);
        }
    }

    public static byte[] LoadSecurely(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Input path cannot be null or whitespace.", nameof(path));
        }

        byte[] fileData = File.ReadAllBytes(path);
        byte[] entropy = Array.Empty<byte>();
        byte[] decrypted = Array.Empty<byte>();
        byte[] computedHmac = Array.Empty<byte>();
        GCHandle decryptedHandle = default;
        bool isPinned = false;

        try
        {
            SealFileHeader header = SealFileHeader.Parse(fileData);

            if (fileData.Length <= SealFileHeader.HeaderLength)
            {
                throw new InvalidDataException("SafeSeal file does not contain an encrypted payload.");
            }

            byte[] encryptedPayload = fileData[SealFileHeader.HeaderLength..];
            entropy = DeriveEntropy();
            decrypted = ProtectedData.Unprotect(encryptedPayload, entropy, DataProtectionScope.CurrentUser);

            decryptedHandle = GCHandle.Alloc(decrypted, GCHandleType.Pinned);
            isPinned = true;

            using (var hmacProvider = new HMACSHA256(entropy))
            {
                computedHmac = hmacProvider.ComputeHash(decrypted);
            }

            if (!CryptographicOperations.FixedTimeEquals(computedHmac, header.Hmac))
            {
                throw new CryptographicException("SafeSeal integrity validation failed.");
            }

            byte[] result = new byte[decrypted.Length];
            Buffer.BlockCopy(decrypted, 0, result, 0, decrypted.Length);
            return result;
        }
        finally
        {
            if (isPinned)
            {
                decryptedHandle.Free();
            }

            Array.Clear(fileData, 0, fileData.Length);
            Array.Clear(entropy, 0, entropy.Length);
            Array.Clear(decrypted, 0, decrypted.Length);
            Array.Clear(computedHmac, 0, computedHmac.Length);
        }
    }

    private static byte[] DeriveEntropy()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new CryptographicException("Unable to derive Windows SID for entropy.");
        return Encoding.UTF8.GetBytes(sid + EntropySuffix);
    }
}

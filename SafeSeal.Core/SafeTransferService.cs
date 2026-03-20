using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SafeSeal.Core;

public sealed class SafeTransferService : ISafeTransferService
{
    private static readonly byte[] Magic = "SSTRANSFER_V1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const byte FormatVersion = 0x01;
    private const int KdfIterations = 200_000;
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly HiddenVaultStorageService _storage;
    private readonly IPinValidationService _pinValidation;

    public SafeTransferService()
        : this(SafeSealStorageOptions.CreateDefault())
    {
    }

    public SafeTransferService(SafeSealStorageOptions options)
        : this(new HiddenVaultStorageService(options), new PinValidationService())
    {
    }

    public SafeTransferService(HiddenVaultStorageService storage, IPinValidationService pinValidation)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _pinValidation = pinValidation ?? throw new ArgumentNullException(nameof(pinValidation));
    }

    public bool CanReadFormat(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[Magic.Length + 1];
            int read = stream.Read(header);
            if (read != header.Length)
            {
                return false;
            }

            return header[..Magic.Length].SequenceEqual(Magic)
                && header[Magic.Length] == FormatVersion;
        }
        catch
        {
            return false;
        }
    }

    public async Task CreateArchiveAsync(DocumentEntry document, string archivePath, string pin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));
        }

        _pinValidation.ValidatePinFormat(pin);

        byte[] imageData = await _storage.LoadAsync(document.StoredFileName, ct).ConfigureAwait(false);
        using SecureBufferScope imageScope = new(imageData);

        TransferArchivePayload payload = new(
            OriginalFileName: BuildOriginalFileName(document),
            OriginalFileSize: imageScope.Buffer.LongLength,
            MimeType: ResolveMimeType(document.OriginalExtension),
            CreatedAt: DateTime.UtcNow,
            WatermarkOptions: null,
            ImageData: imageScope.Buffer);

        byte[] plainJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = _pinValidation.DeriveKey(pin, salt, KdfIterations);
        byte[] cipher = new byte[plainJson.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plainJson, cipher, tag, BuildAad(KdfIterations));
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(Magic, ct).ConfigureAwait(false);
            stream.WriteByte(FormatVersion);

            byte[] iterations = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(iterations, KdfIterations);
            await stream.WriteAsync(iterations, ct).ConfigureAwait(false);
            await stream.WriteAsync(salt, ct).ConfigureAwait(false);
            await stream.WriteAsync(nonce, ct).ConfigureAwait(false);
            await stream.WriteAsync(cipher, ct).ConfigureAwait(false);
            await stream.WriteAsync(tag, ct).ConfigureAwait(false);
            Array.Clear(iterations, 0, iterations.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainJson);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public async Task<TransferArchiveContent> ExtractArchiveAsync(string archivePath, string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));
        }

        _pinValidation.ValidatePinFormat(pin);

        byte[] archive = await File.ReadAllBytesAsync(archivePath, ct).ConfigureAwait(false);
        byte[] key = Array.Empty<byte>();
        byte[] plain = Array.Empty<byte>();

        try
        {
            int minLength = Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize;
            if (archive.Length < minLength)
            {
                throw new InvalidDataException("Archive is truncated.");
            }

            int offset = 0;
            if (!archive.AsSpan(offset, Magic.Length).SequenceEqual(Magic))
            {
                throw new InvalidDataException("Invalid transfer archive magic.");
            }

            offset += Magic.Length;
            byte version = archive[offset++];
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported transfer archive version: {version}");
            }

            int iterations = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(offset, 4));
            offset += 4;

            byte[] salt = archive.AsSpan(offset, SaltSize).ToArray();
            offset += SaltSize;
            byte[] nonce = archive.AsSpan(offset, NonceSize).ToArray();
            offset += NonceSize;

            int cipherLength = archive.Length - offset - TagSize;
            if (cipherLength <= 0)
            {
                throw new InvalidDataException("Archive payload is empty.");
            }

            byte[] cipher = archive.AsSpan(offset, cipherLength).ToArray();
            offset += cipherLength;
            byte[] tag = archive.AsSpan(offset, TagSize).ToArray();

            key = _pinValidation.DeriveKey(pin, salt, iterations);
            plain = new byte[cipher.Length];

            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain, BuildAad(iterations));
            }
            catch (CryptographicException ex)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                throw new UnauthorizedAccessException("PIN is incorrect or archive was tampered.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(cipher);
                CryptographicOperations.ZeroMemory(tag);
            }

            TransferArchivePayload? payload = JsonSerializer.Deserialize<TransferArchivePayload>(plain, JsonOptions);
            if (payload is null || payload.ImageData is null)
            {
                throw new InvalidDataException("Archive payload is invalid.");
            }

            return new TransferArchiveContent(
                payload.OriginalFileName,
                payload.OriginalFileSize,
                payload.MimeType,
                payload.CreatedAt,
                payload.WatermarkOptions,
                payload.ImageData);
        }
        finally
        {
            if (key.Length > 0)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (plain.Length > 0)
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            Array.Clear(archive, 0, archive.Length);
        }
    }

    private static byte[] BuildAad(int iterations)
    {
        return Encoding.UTF8.GetBytes($"SSTRANSFER|V1|PBKDF2-SHA256|ITER={iterations}|AES-256-GCM");
    }

    private static string BuildOriginalFileName(DocumentEntry document)
    {
        string extension = string.IsNullOrWhiteSpace(document.OriginalExtension)
            ? ".bin"
            : document.OriginalExtension;

        return $"{document.DisplayName}{extension}";
    }

    private static string ResolveMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream",
        };
    }

    private sealed record TransferArchivePayload(
        string OriginalFileName,
        long OriginalFileSize,
        string MimeType,
        DateTime CreatedAt,
        WatermarkOptions? WatermarkOptions,
        byte[] ImageData);
}

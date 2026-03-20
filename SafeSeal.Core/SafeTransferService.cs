using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
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
    private readonly ILoggingService _logger;

    public SafeTransferService()
        : this(SafeSealStorageOptions.CreateDefault(), LogManager.SharedLogger)
    {
    }

    public SafeTransferService(SafeSealStorageOptions options)
        : this(new HiddenVaultStorageService(options), new PinValidationService(), LogManager.SharedLogger)
    {
    }

    public SafeTransferService(SafeSealStorageOptions options, ILoggingService? loggingService)
        : this(new HiddenVaultStorageService(options), new PinValidationService(), loggingService)
    {
    }

    public SafeTransferService(HiddenVaultStorageService storage, IPinValidationService pinValidation, ILoggingService? loggingService = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _pinValidation = pinValidation ?? throw new ArgumentNullException(nameof(pinValidation));
        _logger = loggingService ?? LogManager.SharedLogger;
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

        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Stopwatch operationStopwatch = Stopwatch.StartNew();
        string phase = "start";
        bool success = false;

        _logger.Info(
            nameof(SafeTransferService),
            "create_archive_start",
            operationId,
            new Dictionary<string, object?>
            {
                ["documentId"] = document.Id,
                ["archivePath"] = archivePath,
            });

        byte[] key = Array.Empty<byte>();
        byte[] plainJson = Array.Empty<byte>();
        byte[] salt = Array.Empty<byte>();
        byte[] nonce = Array.Empty<byte>();

        try
        {
            phase = "pin_validation";
            try
            {
                _pinValidation.ValidatePinFormat(pin);
                _logger.Trace(nameof(SafeTransferService), "create_archive_pin_valid", operationId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    nameof(SafeTransferService),
                    "create_archive_pin_invalid",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase,
                    },
                    ex);

                throw;
            }

            phase = "load_vault_payload";
            Stopwatch loadStopwatch = Stopwatch.StartNew();
            byte[] imageData = await _storage.LoadAsync(document.StoredFileName, ct).ConfigureAwait(false);
            loadStopwatch.Stop();

            _logger.Info(
                nameof(SafeTransferService),
                "create_archive_payload_loaded",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["imageBytes"] = imageData.LongLength,
                    ["durationMs"] = loadStopwatch.ElapsedMilliseconds,
                });

            using SecureBufferScope imageScope = new(imageData);

            phase = "payload_serialize";
            TransferArchivePayload payload = new(
                OriginalFileName: BuildOriginalFileName(document),
                OriginalFileSize: imageScope.Buffer.LongLength,
                MimeType: ResolveMimeType(document.OriginalExtension),
                CreatedAt: DateTime.UtcNow,
                WatermarkOptions: null,
                ImageData: imageScope.Buffer);

            plainJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            _logger.Trace(
                nameof(SafeTransferService),
                "create_archive_payload_serialized",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["plainJsonBytes"] = plainJson.LongLength,
                });

            salt = RandomNumberGenerator.GetBytes(SaltSize);
            nonce = RandomNumberGenerator.GetBytes(NonceSize);

            phase = "kdf_derive";
            Stopwatch kdfStopwatch = Stopwatch.StartNew();
            key = _pinValidation.DeriveKey(pin, salt, KdfIterations);
            kdfStopwatch.Stop();

            _logger.Info(
                nameof(SafeTransferService),
                "create_archive_kdf_derived",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["iterations"] = KdfIterations,
                    ["durationMs"] = kdfStopwatch.ElapsedMilliseconds,
                });

            byte[] cipher = new byte[plainJson.Length];
            byte[] tag = new byte[TagSize];

            phase = "encrypt_payload";
            Stopwatch encryptStopwatch = Stopwatch.StartNew();
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plainJson, cipher, tag, BuildAad(KdfIterations));
            }

            encryptStopwatch.Stop();
            _logger.Trace(
                nameof(SafeTransferService),
                "create_archive_payload_encrypted",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["cipherBytes"] = cipher.LongLength,
                    ["tagBytes"] = tag.LongLength,
                    ["durationMs"] = encryptStopwatch.ElapsedMilliseconds,
                });

            phase = "archive_write";
            Stopwatch writeStopwatch = Stopwatch.StartNew();
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

            writeStopwatch.Stop();
            _logger.Info(
                nameof(SafeTransferService),
                "create_archive_written",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["archivePath"] = archivePath,
                    ["cipherBytes"] = cipher.LongLength,
                    ["durationMs"] = writeStopwatch.ElapsedMilliseconds,
                });

            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(tag);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                nameof(SafeTransferService),
                "create_archive_failed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["durationMs"] = operationStopwatch.ElapsedMilliseconds,
                },
                ex);

            throw;
        }
        finally
        {
            if (key.Length > 0)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (plainJson.Length > 0)
            {
                CryptographicOperations.ZeroMemory(plainJson);
            }

            if (salt.Length > 0)
            {
                CryptographicOperations.ZeroMemory(salt);
            }

            if (nonce.Length > 0)
            {
                CryptographicOperations.ZeroMemory(nonce);
            }

            _logger.Trace(
                nameof(SafeTransferService),
                "create_archive_cleanup_completed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = "cleanup",
                });

            operationStopwatch.Stop();
            _logger.Info(
                nameof(SafeTransferService),
                "create_archive_end",
                operationId,
                new Dictionary<string, object?>
                {
                    ["success"] = success,
                    ["durationMs"] = operationStopwatch.ElapsedMilliseconds,
                });
        }
    }

    public async Task<TransferArchiveContent> ExtractArchiveAsync(string archivePath, string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));
        }

        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Stopwatch operationStopwatch = Stopwatch.StartNew();
        string phase = "start";
        bool success = false;

        _logger.Info(
            nameof(SafeTransferService),
            "extract_archive_start",
            operationId,
            new Dictionary<string, object?>
            {
                ["archivePath"] = archivePath,
            });

        byte[] archive = Array.Empty<byte>();
        byte[] key = Array.Empty<byte>();
        byte[] plain = Array.Empty<byte>();

        try
        {
            phase = "pin_validation";
            try
            {
                _pinValidation.ValidatePinFormat(pin);
                _logger.Trace(nameof(SafeTransferService), "extract_archive_pin_valid", operationId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    nameof(SafeTransferService),
                    "extract_archive_pin_invalid",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase,
                    },
                    ex);

                throw;
            }

            phase = "archive_read";
            Stopwatch readStopwatch = Stopwatch.StartNew();
            archive = await File.ReadAllBytesAsync(archivePath, ct).ConfigureAwait(false);
            readStopwatch.Stop();

            _logger.Info(
                nameof(SafeTransferService),
                "extract_archive_read",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["archiveBytes"] = archive.LongLength,
                    ["durationMs"] = readStopwatch.ElapsedMilliseconds,
                });

            int minLength = Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize;
            if (archive.Length < minLength)
            {
                throw new InvalidDataException("Archive is truncated.");
            }

            phase = "header_parse";
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

            _logger.Trace(
                nameof(SafeTransferService),
                "extract_archive_header_parsed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["iterations"] = iterations,
                    ["cipherBytes"] = cipherLength,
                });

            phase = "kdf_derive";
            Stopwatch kdfStopwatch = Stopwatch.StartNew();
            key = _pinValidation.DeriveKey(pin, salt, iterations);
            kdfStopwatch.Stop();

            _logger.Info(
                nameof(SafeTransferService),
                "extract_archive_kdf_derived",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["iterations"] = iterations,
                    ["durationMs"] = kdfStopwatch.ElapsedMilliseconds,
                });

            phase = "decrypt_payload";
            plain = new byte[cipher.Length];

            try
            {
                Stopwatch decryptStopwatch = Stopwatch.StartNew();
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain, BuildAad(iterations));
                decryptStopwatch.Stop();

                _logger.Info(
                    nameof(SafeTransferService),
                    "extract_archive_decrypt_success",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase,
                        ["plainBytes"] = plain.LongLength,
                        ["durationMs"] = decryptStopwatch.ElapsedMilliseconds,
                    });
            }
            catch (CryptographicException ex)
            {
                _logger.Warning(
                    nameof(SafeTransferService),
                    "extract_archive_auth_failed",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase,
                        ["delayMs"] = 400,
                    },
                    ex);

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

            phase = "payload_deserialize";
            TransferArchivePayload? payload = JsonSerializer.Deserialize<TransferArchivePayload>(plain, JsonOptions);
            if (payload is null || payload.ImageData is null)
            {
                throw new InvalidDataException("Archive payload is invalid.");
            }

            _logger.Trace(
                nameof(SafeTransferService),
                "extract_archive_payload_deserialized",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["originalFileName"] = payload.OriginalFileName,
                    ["imageBytes"] = payload.ImageData.LongLength,
                });

            success = true;
            return new TransferArchiveContent(
                payload.OriginalFileName,
                payload.OriginalFileSize,
                payload.MimeType,
                payload.CreatedAt,
                payload.WatermarkOptions,
                payload.ImageData);
        }
        catch (Exception ex)
        {
            _logger.Error(
                nameof(SafeTransferService),
                "extract_archive_failed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["durationMs"] = operationStopwatch.ElapsedMilliseconds,
                },
                ex);

            throw;
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

            if (archive.Length > 0)
            {
                Array.Clear(archive, 0, archive.Length);
            }

            _logger.Trace(
                nameof(SafeTransferService),
                "extract_archive_cleanup_completed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = "cleanup",
                });

            operationStopwatch.Stop();
            _logger.Info(
                nameof(SafeTransferService),
                "extract_archive_end",
                operationId,
                new Dictionary<string, object?>
                {
                    ["success"] = success,
                    ["durationMs"] = operationStopwatch.ElapsedMilliseconds,
                });
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

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SafeSeal.Core;

public sealed class TransferPackageService : ITransferPackageService
{
    private static readonly byte[] Magic = "SSTRANS2"u8.ToArray();
    private static readonly byte[] EndMagic = "SSTREND2"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const byte MajorVersion = 2;
    private const byte MinorVersion = 0;
    private const ushort FixedHeaderLength = 80;
    private const byte KdfId = 1;
    private const byte EncId = 1;
    private const int KdfIterations = 310_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IPinValidationService _pinValidationService;

    public TransferPackageService()
        : this(new PinValidationService())
    {
    }

    public TransferPackageService(IPinValidationService pinValidationService)
    {
        _pinValidationService = pinValidationService ?? throw new ArgumentNullException(nameof(pinValidationService));
    }

    public async Task<string> CreateMergedPackageAsync(
        IReadOnlyList<string> inputFiles,
        string pin,
        string outputPath,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);

        if (inputFiles.Count == 0)
        {
            throw new ArgumentException("At least one input file is required.", nameof(inputFiles));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
        }

        _pinValidationService.ValidatePinFormat(pin);

        List<string> files = inputFiles.Where(static path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (files.Count == 0)
        {
            throw new ArgumentException("No valid input files were provided.", nameof(inputFiles));
        }

        progress?.Report(new BatchProgress(files.Count, 0, 0, null, "Preparing"));

        string packageId = Guid.NewGuid().ToString("N");
        byte[] packageSalt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] packageKey = _pinValidationService.DeriveKey(pin, packageSalt, KdfIterations);
        byte[] headerNonce = RandomNumberGenerator.GetBytes(NonceSize);

        List<PackagePayloadBlock> payloadBlocks = new(capacity: files.Count);
        List<HeaderFileEntry> headerEntries = new(capacity: files.Count);

        try
        {
            long payloadOffset = 0;
            int completed = 0;

            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string path = files[index];
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Input file not found.", path);
                }

                byte[] plain = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                using SecureBufferScope plainScope = new(plain);

                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] fileKey = DeriveFileKey(packageKey, index);
                byte[] cipher = new byte[plainScope.Buffer.Length];
                byte[] tag = new byte[TagSize];

                try
                {
                    using var aes = new AesGcm(fileKey, TagSize);
                    aes.Encrypt(
                        nonce,
                        plainScope.Buffer,
                        cipher,
                        tag,
                        BuildFileAad(packageId, index, plainScope.Buffer.Length));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(fileKey);
                }

                byte[] payloadRegion = new byte[cipher.Length + tag.Length];
                Buffer.BlockCopy(cipher, 0, payloadRegion, 0, cipher.Length);
                Buffer.BlockCopy(tag, 0, payloadRegion, cipher.Length, tag.Length);

                payloadBlocks.Add(new PackagePayloadBlock(payloadRegion));

                string originalName = Path.GetFileName(path);
                string mime = ResolveMimeType(Path.GetExtension(path));
                uint crc = Crc32.Compute(plainScope.Buffer);

                headerEntries.Add(new HeaderFileEntry
                {
                    Index = (uint)index,
                    OriginalName = originalName,
                    RelativePath = string.Empty,
                    PlainSize = (ulong)plainScope.Buffer.LongLength,
                    CipherOffset = (ulong)payloadOffset,
                    CipherSize = (ulong)payloadRegion.LongLength,
                    FileNonce = nonce,
                    FileTag = tag,
                    Mime = mime,
                    Crc32Plain = crc,
                });

                payloadOffset += payloadRegion.LongLength;
                completed++;
                progress?.Report(new BatchProgress(files.Count, completed, 0, originalName, "Encrypting"));

                CryptographicOperations.ZeroMemory(cipher);
            }

            HeaderPlain headerPlain = new()
            {
                FileCount = (uint)headerEntries.Count,
                CreatedUnixMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CreatorApp = "SafeSeal/2.x",
                Locale = CultureInfo.CurrentUICulture.Name,
                PackageId = packageId,
                Files = headerEntries,
                Meta = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Format"] = "SSTRANS2",
                },
            };

            byte[] headerPlainBytes = JsonSerializer.SerializeToUtf8Bytes(headerPlain, JsonOptions);
            byte[] headerKey = DeriveHeaderKey(packageKey);
            byte[] encryptedHeader = new byte[headerPlainBytes.Length];
            byte[] headerTag = new byte[TagSize];

            try
            {
                using var aes = new AesGcm(headerKey, TagSize);
                aes.Encrypt(headerNonce, headerPlainBytes, encryptedHeader, headerTag, BuildHeaderAad());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(headerKey);
                CryptographicOperations.ZeroMemory(headerPlainBytes);
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] fixedHeader = new byte[FixedHeaderLength];
            WriteFixedHeader(
                fixedHeader,
                packageSalt,
                headerNonce,
                headerTag,
                (ulong)encryptedHeader.LongLength,
                (ulong)payloadBlocks.Sum(static x => x.Payload.LongLength));

            await stream.WriteAsync(fixedHeader, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(encryptedHeader, cancellationToken).ConfigureAwait(false);

            foreach (PackagePayloadBlock block in payloadBlocks)
            {
                await stream.WriteAsync(block.Payload, cancellationToken).ConfigureAwait(false);
                CryptographicOperations.ZeroMemory(block.Payload);
            }

            byte[] endMarker = BuildEndMarker((ulong)stream.Length + 20, Crc32.Compute(encryptedHeader));
            await stream.WriteAsync(endMarker, cancellationToken).ConfigureAwait(false);

            CryptographicOperations.ZeroMemory(endMarker);
            CryptographicOperations.ZeroMemory(fixedHeader);
            CryptographicOperations.ZeroMemory(encryptedHeader);
            CryptographicOperations.ZeroMemory(headerTag);

            progress?.Report(new BatchProgress(files.Count, files.Count, 0, null, "Completed"));
            return outputPath;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packageSalt);
            CryptographicOperations.ZeroMemory(packageKey);
            CryptographicOperations.ZeroMemory(headerNonce);
        }
    }

    public async Task<BatchResult> ExtractMergedPackageAsync(
        string packagePath,
        string pin,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new ArgumentException("Package path cannot be empty.", nameof(packagePath));
        }

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Package file not found.", packagePath);
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));
        }

        _pinValidationService.ValidatePinFormat(pin);
        Directory.CreateDirectory(outputDirectory);

        Stopwatch stopwatch = Stopwatch.StartNew();
        ConcurrentBag<string> outputFiles = new();
        ConcurrentBag<BatchFileError> errors = new();

        byte[] bytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        byte[] packageKey = Array.Empty<byte>();

        try
        {
            if (bytes.Length < FixedHeaderLength)
            {
                throw new InvalidDataException("Package header is truncated.");
            }

            ReadOnlySpan<byte> header = bytes.AsSpan(0, FixedHeaderLength);
            ValidateFixedHeader(header);

            byte[] packageSalt = header.Slice(16, SaltSize).ToArray();
            uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(32, 4));
            byte[] headerNonce = header.Slice(36, NonceSize).ToArray();
            byte[] headerTag = header.Slice(48, TagSize).ToArray();
            ulong encryptedHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(64, 8));
            ulong payloadSize = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(72, 8));

            long payloadOffsetAbsolute = FixedHeaderLength + (long)encryptedHeaderSize;
            if (payloadOffsetAbsolute < 0 || payloadOffsetAbsolute > bytes.LongLength)
            {
                throw new InvalidDataException("Package encrypted header offset is invalid.");
            }

            byte[] encryptedHeader = bytes.AsSpan(FixedHeaderLength, checked((int)encryptedHeaderSize)).ToArray();

            packageKey = _pinValidationService.DeriveKey(pin, packageSalt, checked((int)iterations));
            byte[] headerKey = DeriveHeaderKey(packageKey);
            byte[] plainHeader = new byte[encryptedHeader.Length];

            try
            {
                using var aes = new AesGcm(headerKey, TagSize);
                aes.Decrypt(headerNonce, encryptedHeader, headerTag, plainHeader, BuildHeaderAad());
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("PIN is incorrect or package was tampered.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(headerKey);
                CryptographicOperations.ZeroMemory(packageSalt);
                CryptographicOperations.ZeroMemory(headerNonce);
                CryptographicOperations.ZeroMemory(headerTag);
                CryptographicOperations.ZeroMemory(encryptedHeader);
            }

            HeaderPlain? parsedHeader = JsonSerializer.Deserialize<HeaderPlain>(plainHeader, JsonOptions);
            CryptographicOperations.ZeroMemory(plainHeader);

            if (parsedHeader?.Files is null)
            {
                throw new InvalidDataException("Package header payload is invalid.");
            }

            int total = parsedHeader.Files.Count;
            int completed = 0;
            int failed = 0;
            progress?.Report(new BatchProgress(total, 0, 0, null, "Decrypting"));

            foreach (HeaderFileEntry entry in parsedHeader.Files.OrderBy(static x => x.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    long entryOffset = payloadOffsetAbsolute + checked((long)entry.CipherOffset);
                    if (entryOffset < 0 || entryOffset > bytes.LongLength)
                    {
                        throw new InvalidDataException("Payload offset is out of range.");
                    }

                    int cipherSize = checked((int)entry.CipherSize);
                    int entryOffsetInt = checked((int)entryOffset);
                    if (entryOffset + cipherSize > bytes.LongLength)
                    {
                        throw new InvalidDataException("Payload region exceeds package size.");
                    }

                    ReadOnlySpan<byte> region = bytes.AsSpan(entryOffsetInt, cipherSize);
                    if (region.Length < TagSize)
                    {
                        throw new InvalidDataException("Payload region is invalid.");
                    }

                    ReadOnlySpan<byte> cipher = region[..^TagSize];
                    byte[] fileTagInPayload = region[^TagSize..].ToArray();
                    if (!fileTagInPayload.SequenceEqual(entry.FileTag))
                    {
                        throw new InvalidDataException("File tag mismatch.");
                    }

                    byte[] fileKey = DeriveFileKey(packageKey, checked((int)entry.Index));
                    byte[] plain = new byte[cipher.Length];

                    try
                    {
                        using var aes = new AesGcm(fileKey, TagSize);
                        aes.Decrypt(entry.FileNonce, cipher, fileTagInPayload, plain, BuildFileAad(parsedHeader.PackageId, checked((int)entry.Index), plain.Length));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(fileKey);
                        CryptographicOperations.ZeroMemory(fileTagInPayload);
                    }

                    uint crc = Crc32.Compute(plain);
                    if (crc != entry.Crc32Plain)
                    {
                        CryptographicOperations.ZeroMemory(plain);
                        throw new InvalidDataException("Plain CRC verification failed.");
                    }

                    string safeName = string.IsNullOrWhiteSpace(entry.OriginalName)
                        ? $"file_{entry.Index:D4}.bin"
                        : Path.GetFileName(entry.OriginalName);

                    string outputPath = Path.Combine(outputDirectory, safeName);
                    await File.WriteAllBytesAsync(outputPath, plain, cancellationToken).ConfigureAwait(false);
                    CryptographicOperations.ZeroMemory(plain);

                    outputFiles.Add(outputPath);
                    completed++;
                    progress?.Report(new BatchProgress(total, completed, failed, safeName, "Completed"));
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add(new BatchFileError(entry.OriginalName, "EXTRACT_ERROR", ex.Message));
                    progress?.Report(new BatchProgress(total, completed, failed, entry.OriginalName, "Failed"));
                }
            }

            long expectedMinimum = payloadOffsetAbsolute + checked((long)payloadSize);
            if (bytes.LongLength < expectedMinimum)
            {
                errors.Add(new BatchFileError(packagePath, "PAYLOAD_SIZE", "Payload size does not match header metadata."));
            }
        }
        finally
        {
            if (packageKey.Length > 0)
            {
                CryptographicOperations.ZeroMemory(packageKey);
            }

            CryptographicOperations.ZeroMemory(bytes);
        }

        stopwatch.Stop();
        return new BatchResult(outputFiles.ToArray(), errors.ToArray(), stopwatch.Elapsed);
    }

    private static void WriteFixedHeader(
        Span<byte> header,
        ReadOnlySpan<byte> packageSalt,
        ReadOnlySpan<byte> headerNonce,
        ReadOnlySpan<byte> headerTag,
        ulong encryptedHeaderSize,
        ulong payloadSize)
    {
        header.Clear();
        Magic.CopyTo(header[..8]);
        header[8] = MajorVersion;
        header[9] = MinorVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(10, 2), FixedHeaderLength);
        header[12] = KdfId;
        header[13] = EncId;
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(14, 2), 1);

        packageSalt.CopyTo(header.Slice(16, SaltSize));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(32, 4), KdfIterations);
        headerNonce.CopyTo(header.Slice(36, NonceSize));
        headerTag.CopyTo(header.Slice(48, TagSize));

        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(64, 8), encryptedHeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(72, 8), payloadSize);
    }

    private static void ValidateFixedHeader(ReadOnlySpan<byte> header)
    {
        if (!header[..8].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not an SSTRANS2 package.");
        }

        if (header[8] != MajorVersion)
        {
            throw new InvalidDataException($"Unsupported package major version: {header[8]}");
        }

        ushort fixedLen = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
        if (fixedLen != FixedHeaderLength)
        {
            throw new InvalidDataException("Unexpected fixed header length.");
        }

        if (header[12] != KdfId || header[13] != EncId)
        {
            throw new InvalidDataException("Unsupported cryptographic suite id.");
        }
    }

    private static byte[] BuildHeaderAad()
    {
        return "SSTRANS2|HEADER|PBKDF2-SHA256|AES-256-GCM"u8.ToArray();
    }

    private static byte[] BuildFileAad(string packageId, int index, int plainSize)
    {
        return Encoding.UTF8.GetBytes($"{packageId}|{index}|{plainSize}");
    }

    private static byte[] DeriveHeaderKey(byte[] packageKey)
    {
        return HkdfSha256(packageKey, "header", 32);
    }

    private static byte[] DeriveFileKey(byte[] packageKey, int index)
    {
        return HkdfSha256(packageKey, $"file:{index}", 32);
    }

    private static byte[] HkdfSha256(byte[] ikm, string info, int length)
    {
        byte[] zeroSalt = new byte[32];
        byte[] infoBytes = Encoding.UTF8.GetBytes(info);

        try
        {
            using HMACSHA256 hmacExtract = new(zeroSalt);
            byte[] prk = hmacExtract.ComputeHash(ikm);

            try
            {
                byte[] result = new byte[length];
                byte[] previous = Array.Empty<byte>();
                int offset = 0;
                byte counter = 1;

                while (offset < length)
                {
                    using HMACSHA256 hmacExpand = new(prk);
                    byte[] input = new byte[previous.Length + infoBytes.Length + 1];
                    Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                    Buffer.BlockCopy(infoBytes, 0, input, previous.Length, infoBytes.Length);
                    input[^1] = counter++;

                    byte[] block = hmacExpand.ComputeHash(input);
                    int copy = Math.Min(block.Length, length - offset);
                    Buffer.BlockCopy(block, 0, result, offset, copy);
                    offset += copy;

                    if (previous.Length > 0)
                    {
                        CryptographicOperations.ZeroMemory(previous);
                    }

                    previous = block;
                    CryptographicOperations.ZeroMemory(input);
                }

                if (previous.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(previous);
                }

                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(zeroSalt);
            CryptographicOperations.ZeroMemory(infoBytes);
        }
    }

    private static byte[] BuildEndMarker(ulong totalSize, uint headerCrc)
    {
        byte[] marker = new byte[20];
        EndMagic.CopyTo(marker.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(marker.AsSpan(8, 8), totalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(marker.AsSpan(16, 4), headerCrc);
        return marker;
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

    private sealed class PackagePayloadBlock
    {
        public PackagePayloadBlock(byte[] payload)
        {
            Payload = payload;
        }

        public byte[] Payload { get; }
    }

    private sealed class HeaderPlain
    {
        public uint FileCount { get; set; }

        public ulong CreatedUnixMs { get; set; }

        public string CreatorApp { get; set; } = string.Empty;

        public string Locale { get; set; } = string.Empty;

        public string PackageId { get; set; } = string.Empty;

        public List<HeaderFileEntry> Files { get; set; } = [];

        public Dictionary<string, string> Meta { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class HeaderFileEntry
    {
        public uint Index { get; set; }

        public string OriginalName { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public ulong PlainSize { get; set; }

        public ulong CipherOffset { get; set; }

        public ulong CipherSize { get; set; }

        public byte[] FileNonce { get; set; } = Array.Empty<byte>();

        public byte[] FileTag { get; set; } = Array.Empty<byte>();

        public string Mime { get; set; } = string.Empty;

        public uint Crc32Plain { get; set; }
    }

    private static class Crc32
    {
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;

            foreach (byte value in data)
            {
                crc ^= value;
                for (int i = 0; i < 8; i++)
                {
                    uint mask = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (0xEDB88320u & mask);
                }
            }

            return ~crc;
        }
    }
}




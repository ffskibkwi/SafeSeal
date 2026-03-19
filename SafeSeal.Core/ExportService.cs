using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace SafeSeal.Core;

public sealed class ExportService
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public bool ExportAsJpeg(BitmapSource image, string outputPath, int quality)
    {
        return ExportAsJpeg(image, outputPath, quality, metadataContext: null);
    }

    public bool ExportAsJpeg(BitmapSource image, string outputPath, int quality, ExportMetadataContext? metadataContext)
    {
        ValidateInputs(image, outputPath);

        int clampedQuality = Math.Clamp(quality, 70, 95);
        if (clampedQuality != quality)
        {
            Trace.TraceWarning("JPEG quality {0} is outside 70-95 and was clamped to {1}.", quality, clampedQuality);
        }

        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = clampedQuality,
        };

        bool metadataEmbedded = TryAddJpegFrameWithMetadata(encoder, image, metadataContext);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);

        ForceBitmapCleanup();
        return metadataEmbedded;
    }

    public bool ExportAsPng(BitmapSource image, string outputPath)
    {
        return ExportAsPng(image, outputPath, metadataContext: null);
    }

    public bool ExportAsPng(BitmapSource image, string outputPath, ExportMetadataContext? metadataContext)
    {
        ValidateInputs(image, outputPath);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        bool metadataEmbedded = false;
        if (metadataContext is not null)
        {
            try
            {
                EmbedPngTextChunks(outputPath, metadataContext);
                metadataEmbedded = true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("PNG metadata embedding failed for export: {0}", ex.Message);
            }
        }

        ForceBitmapCleanup();
        return metadataEmbedded;
    }

    private static bool TryAddJpegFrameWithMetadata(BitmapEncoder encoder, BitmapSource image, ExportMetadataContext? metadataContext)
    {
        if (metadataContext is null)
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            return false;
        }

        try
        {
            BitmapMetadata metadata = new("jpg");
            string payload = BuildMetadataPayload(metadataContext);

            metadata.SetQuery("/app1/ifd/{ushort=270}", payload);

            byte[] userComment = BuildExifUserComment(payload);
            metadata.SetQuery("/app1/ifd/exif:{ushort=37510}", userComment);
            Array.Clear(userComment, 0, userComment.Length);

            BitmapFrame frame = BitmapFrame.Create(image, null, metadata, null);
            encoder.Frames.Add(frame);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("JPEG metadata embedding failed for export: {0}", ex.Message);
            encoder.Frames.Add(BitmapFrame.Create(image));
            return false;
        }
    }

    private static string BuildMetadataPayload(ExportMetadataContext metadataContext)
    {
        string exportUtc = metadataContext.ExportUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return $"SafeSeal.SignatureId={metadataContext.SignatureId};SafeSeal.TemplateId={metadataContext.TemplateId};SafeSeal.TemplateVersion={metadataContext.TemplateVersion};SafeSeal.ExportUtc={exportUtc}";
    }

    private static void EmbedPngTextChunks(string path, ExportMetadataContext metadataContext)
    {
        byte[] original = File.ReadAllBytes(path);

        if (original.Length < PngSignature.Length || !original.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("Target file is not a valid PNG stream.");
        }

        Dictionary<string, string> entries = new(StringComparer.Ordinal)
        {
            ["SafeSeal.SignatureId"] = metadataContext.SignatureId,
            ["SafeSeal.TemplateId"] = metadataContext.TemplateId,
            ["SafeSeal.TemplateVersion"] = metadataContext.TemplateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SafeSeal.ExportUtc"] = metadataContext.ExportUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        int iendOffset = FindPngChunkOffset(original, "IEND");
        if (iendOffset < 0)
        {
            throw new InvalidDataException("PNG IEND chunk not found.");
        }

        using MemoryStream output = new(capacity: original.Length + (entries.Count * 120));
        output.Write(original, 0, iendOffset);

        foreach ((string key, string value) in entries)
        {
            WritePngTextChunk(output, key, value);
        }

        output.Write(original, iendOffset, original.Length - iendOffset);
        byte[] updated = output.ToArray();
        File.WriteAllBytes(path, updated);

        Array.Clear(original, 0, original.Length);
        Array.Clear(updated, 0, updated.Length);
    }

    private static int FindPngChunkOffset(byte[] pngBytes, string chunkType)
    {
        int offset = PngSignature.Length;

        while (offset + 12 <= pngBytes.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(pngBytes, offset + 4, 4);

            if (string.Equals(type, chunkType, StringComparison.Ordinal))
            {
                return offset;
            }

            offset += 12 + checked((int)length);
        }

        return -1;
    }

    private static void WritePngTextChunk(Stream stream, string keyword, string value)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes("tEXt");
        byte[] keywordBytes = Encoding.ASCII.GetBytes(keyword);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);

        byte[] data = new byte[keywordBytes.Length + 1 + valueBytes.Length];
        Buffer.BlockCopy(keywordBytes, 0, data, 0, keywordBytes.Length);
        data[keywordBytes.Length] = 0;
        Buffer.BlockCopy(valueBytes, 0, data, keywordBytes.Length + 1, valueBytes.Length);

        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        stream.Write(lengthBytes);
        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(data, 0, data.Length);

        byte[] crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);

        uint crc = ComputeCrc32(crcInput);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);

        Array.Clear(valueBytes, 0, valueBytes.Length);
        Array.Clear(data, 0, data.Length);
        Array.Clear(crcInput, 0, crcInput.Length);
    }

    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                uint mask = (uint)-(int)(crc & 1u);
                crc = (crc >> 1) ^ (0xEDB88320u & mask);
            }
        }

        return ~crc;
    }

    private static byte[] BuildExifUserComment(string payload)
    {
        byte[] prefix = "ASCII\0\0\0"u8.ToArray();
        byte[] body = Encoding.UTF8.GetBytes(payload);
        byte[] result = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, result, prefix.Length, body.Length);
        Array.Clear(body, 0, body.Length);
        return result;
    }

    private static void ValidateInputs(BitmapSource image, string outputPath)
    {
        if (image is null)
        {
            throw new ArgumentException("Image cannot be null.", nameof(image));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(outputPath));
        }
    }

    private static void ForceBitmapCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

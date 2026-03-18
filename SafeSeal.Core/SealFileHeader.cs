using System.IO;
using System.Text;

namespace SafeSeal.Core;

/// <summary>
/// Represents the fixed header metadata at the start of a SafeSeal file.
/// </summary>
public sealed class SealFileHeader
{
    /// <summary>
    /// The ASCII magic value that identifies SafeSeal files.
    /// </summary>
    public const string MagicText = "SEAL";

    /// <summary>
    /// The number of bytes used for the magic field.
    /// </summary>
    public const int MagicLength = 4;

    /// <summary>
    /// The number of bytes used for the version field.
    /// </summary>
    public const int VersionLength = 2;

    /// <summary>
    /// The number of bytes used for the HMAC field.
    /// </summary>
    public const int HmacLength = 32;

    /// <summary>
    /// The total number of bytes used by this header.
    /// </summary>
    public const int HeaderLength = MagicLength + VersionLength + HmacLength;

    private static readonly byte[] ExpectedMagicBytes = Encoding.ASCII.GetBytes(MagicText);

    /// <summary>
    /// Initializes a new header instance.
    /// </summary>
    /// <param name="versionMajor">Header major version.</param>
    /// <param name="versionMinor">Header minor version.</param>
    /// <param name="hmac">HMAC SHA-256 hash bytes for plaintext integrity validation.</param>
    public SealFileHeader(byte versionMajor, byte versionMinor, byte[] hmac)
    {
        if (hmac is null)
        {
            throw new ArgumentNullException(nameof(hmac));
        }

        if (hmac.Length != HmacLength)
        {
            throw new ArgumentException($"HMAC must be exactly {HmacLength} bytes.", nameof(hmac));
        }

        VersionMajor = versionMajor;
        VersionMinor = versionMinor;
        Hmac = new byte[HmacLength];
        Buffer.BlockCopy(hmac, 0, Hmac, 0, HmacLength);
    }

    /// <summary>
    /// Gets the header major version.
    /// </summary>
    public byte VersionMajor { get; }

    /// <summary>
    /// Gets the header minor version.
    /// </summary>
    public byte VersionMinor { get; }

    /// <summary>
    /// Gets the HMAC SHA-256 hash bytes.
    /// </summary>
    public byte[] Hmac { get; }

    /// <summary>
    /// Parses and validates the header from the provided file bytes.
    /// </summary>
    /// <param name="fileData">Raw file bytes beginning with the header.</param>
    /// <returns>A parsed and validated <see cref="SealFileHeader"/> instance.</returns>
    /// <exception cref="InvalidDataException">Thrown when header data is too short or has an invalid magic value.</exception>
    /// <exception cref="NotSupportedException">Thrown when the major version is not supported.</exception>
    public static SealFileHeader Parse(byte[] fileData)
    {
        if (fileData is null)
        {
            throw new ArgumentNullException(nameof(fileData));
        }

        if (fileData.Length < HeaderLength)
        {
            throw new InvalidDataException($"File is too short to contain a valid SafeSeal header ({HeaderLength} bytes required).");
        }

        for (int i = 0; i < MagicLength; i++)
        {
            if (fileData[i] != ExpectedMagicBytes[i])
            {
                throw new InvalidDataException("Invalid SafeSeal magic bytes.");
            }
        }

        byte versionMajor = fileData[4];
        byte versionMinor = fileData[5];

        if (versionMajor != 1)
        {
            throw new NotSupportedException($"SafeSeal major version {versionMajor} is not supported.");
        }

        byte[] hmac = new byte[HmacLength];
        Buffer.BlockCopy(fileData, 6, hmac, 0, HmacLength);

        return new SealFileHeader(versionMajor, versionMinor, hmac);
    }

    /// <summary>
    /// Serializes the header to bytes.
    /// </summary>
    /// <returns>A byte array containing the serialized header.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[HeaderLength];
        Buffer.BlockCopy(ExpectedMagicBytes, 0, bytes, 0, MagicLength);
        bytes[4] = VersionMajor;
        bytes[5] = VersionMinor;
        Buffer.BlockCopy(Hmac, 0, bytes, 6, HmacLength);
        return bytes;
    }
}

using System.IO;
using System.Security.Cryptography;
using SafeSeal.Core;

namespace SafeSeal.Tests;

public sealed class VaultManagerTests : IDisposable
{
    private readonly string _tempRoot;

    public VaultManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SafeSeal.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void SaveAndLoadSecurely_RoundTrip_ReturnsOriginalPayload()
    {
        byte[] input = RandomNumberGenerator.GetBytes(2048);
        string path = Path.Combine(_tempRoot, "roundtrip.seal");

        VaultManager.Save(input, path);
        byte[] output = VaultManager.LoadSecurely(path);

        Assert.Equal(input, output);
    }

    [Fact]
    public void LoadSecurely_WithTamperedPayload_ThrowsCryptographicException()
    {
        byte[] input = RandomNumberGenerator.GetBytes(1024);
        string path = Path.Combine(_tempRoot, "tampered.seal");

        VaultManager.Save(input, path);

        byte[] fileData = File.ReadAllBytes(path);
        int payloadIndex = SealFileHeader.HeaderLength;
        fileData[payloadIndex] ^= 0x7F;
        File.WriteAllBytes(path, fileData);

        Assert.Throws<CryptographicException>(() => VaultManager.LoadSecurely(path));
    }

    [Fact]
    public void LoadSecurely_WithInvalidMagic_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_tempRoot, "invalid.seal");
        File.WriteAllBytes(path, "NOTSEAL"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => VaultManager.LoadSecurely(path));
    }

    [Fact]
    public void Save_WithNullInput_ThrowsArgumentException()
    {
        string path = Path.Combine(_tempRoot, "null.seal");
        byte[]? input = null;

        Assert.Throws<ArgumentException>(() => VaultManager.Save(input!, path));
    }

    [Fact]
    public void Save_WithEmptyInput_ThrowsArgumentException()
    {
        string path = Path.Combine(_tempRoot, "empty.seal");

        Assert.Throws<ArgumentException>(() => VaultManager.Save(Array.Empty<byte>(), path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}

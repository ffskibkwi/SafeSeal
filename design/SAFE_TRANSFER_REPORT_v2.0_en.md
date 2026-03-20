# SafeSeal Feature Expansion Report v2.0

> Version: v2.0  
> Date: 2026-03-20  
> Report Type: New Feature Planning  
> Sources: Master Requirements + Codex Technical Design Discussion

---

## 1. Background and Objectives

### 1.1 Report Purpose

This report covers SafeSeal feature expansion Phase 2, focusing on:
1. **Import UI Optimization**: Clear communication about image processing mechanism
2. **Safe Transfer Feature**: Cross-client encrypted archive and recovery

### 1.2 Relationship with Previous Reports

| Report | Focus | Status |
|--------|-------|--------|
| IMPROVEMENT_REPORT_v1.0.md | Security/Performance/Core Features | ✅ Implemented |
| CODE_REVIEW_v2.0.md | Code Review and Issues | ✅ Complete |
| FEATURE_EXPANSION_REPORT_v1.0.md | Maturity/Internationalization | ⚠️ Partially Implemented |
| **This Document** | **Import Optimization + Safe Transfer** | **New Plan** |

---

## 2. Import UI Optimization

### 2.1 Problem Description

The current import flow does not clearly inform users:
1. Original image can be moved or deleted (program copies and encrypts separately)
2. Encrypted data is completely independent from original image
3. Changes to original image location don't affect imported documents

### 2.2 Optimization Solution

Add clear hint information in the import dialog:

```
┌─────────────────────────────────────────────────────────────┐
│  Import Image                                              │
│  ─────────────────────────────────────────────────────    │
│                                                              │
│  Selected: photo_001.jpg (4.2 MB)                         │
│                                                              │
│  ⚠️ Important:                                              │
│  ─────────────────────────────────────────────────────    │
│  • Program will copy and encrypt this image                  │
│  • Original can be moved, deleted, or modified              │
│  • Imported documents are completely independent            │
│  • Decryption requires this device login credentials        │
│  ─────────────────────────────────────────────────────    │
│                                                              │
│  Image name (optional): [________________]                  │
│                                                              │
│  [ Cancel ]                      [ Import & Encrypt ]      │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 Implementation

```csharp
// SafeSeal.App/Dialogs/ImportDialog.xaml.cs
public partial class ImportDialog : Window
{
    public ImportDialog(string filePath)
    {
        long fileSize = new FileInfo(filePath).Length;
        
        string sizeText = fileSize switch
        {
            < 1024 => $"{fileSize} B",
            < 1024 * 1024 => $"{fileSize / 1024.0:F1} KB",
            _ => $"{fileSize / (1024.0 * 1024.0):F1} MB"
        };
        
        SelectedFileText = Path.GetFileName(filePath);
        SelectedFileSizeText = sizeText;
    }
    
    public string SelectedFileText { get; }
    public string SelectedFileSizeText { get; }
    
    public string ImportHintText => LocalizationService.Instance["ImportHint"];
    public string ImportHintLine1 => LocalizationService.Instance["ImportHintLine1"];
    public string ImportHintLine2 => LocalizationService.Instance["ImportHintLine2"];
    public string ImportHintLine3 => LocalizationService.Instance["ImportHintLine3"];
    public string ImportHintLine4 => LocalizationService.Instance["ImportHintLine4"];
}
```

### 2.4 Internationalization

```csharp
// LocalizationService.cs - needs these additions
["ImportHint"] = ("Important: Your original photo will be copied and encrypted. The original can be moved, deleted, or modified without affecting imported documents.", "重要提示：程序将复制并加密此图片。原图可以移动、删除或修改，不影响已导入的文档。"),
["ImportHintLine1"] = ("• Program will copy and encrypt this image", "• 程序将复制并加密此图片"),
["ImportHintLine2"] = ("• Original can be moved, deleted or modified", "• 原图可以移动、删除或修改"),
["ImportHintLine3"] = ("• Imported documents are completely independent from original", "• 已导入的文档与原图完全独立"),
["ImportHintLine4"] = ("• Decryption requires this device login credentials", "• 解密需要此设备登录信息"),
```

---

## 3. Safe Transfer Feature

### 3.1 Feature Overview

**Safe Transfer** is a cross-device, cross-user encrypted archive mechanism:
- Users can export vault documents as standalone encrypted files
- Archives are protected by a 6-digit PIN
- Any device with SafeSeal installed can decrypt, provided it has both the archive file and correct PIN

### 3.2 Use Cases

| Scenario | Description |
|----------|-------------|
| Cross-device migration | Transfer documents between computers |
| Secure sharing | Share sensitive documents via email/cloud |
| Backup and recovery | Encrypted backup to cloud storage |
| Offline transfer | Transfer via USB drive without network |

### 3.3 Technical Design

#### 3.3.1 Architecture Overview

```
SafeTransferService
        │
        ├── CreateArchiveAsync()  ─→ Create encrypted archive
        │
        └── ExtractArchiveAsync()  ─→ Decrypt and extract
        │
PINValidationService
        │
        ├── ValidatePinFormat()    ─→ Validate PIN format
        │
        └── DeriveAes256Key()     ─→ PBKDF2 key derivation
```

#### 3.3.2 Key Derivation Parameters

**Selection**:
- Algorithm: PBKDF2-SHA256
- Iterations: 200,000
- Salt length: 32 bytes (randomly generated)
- Output key: 32 bytes (AES-256)

**Security Analysis**:
- 6-digit PIN = only 10^6 = 1,000,000 possibilities
- PBKDF2 200,000 iterations ≈ 500ms per attempt
- Full brute-force traversal ≈ 500,000,000 seconds (~16 years)

```csharp
// PINValidationService.cs
public sealed class PinValidationService : IPinValidationService
{
    private static readonly Regex PinRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    public void ValidatePinFormat(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !PinRegex.IsMatch(pin))
            throw new ArgumentException("PIN must be exactly 6 digits.");
    }

    public byte[] DeriveAes256Key(string pin, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            keyLength: 32); // AES-256
    }
}
```

#### 3.3.3 Archive File Format (.sstransfer)

```
┌──────────────────────────────────────────────────────────────┐
│                    .sstransfer File Format                    │
├──────────────────────────────────────────────────────────────┤
│  Offset  │  Field           │  Size    │  Description     │
├──────────┼──────────────────┼──────────┼──────────────────┤
│  0       │  Magic           │  14 B    │  "SSTRANSFER_V1" │
│  14      │  Version         │  1 B     │  0x01           │
│  15      │  KdfIterations   │  4 B     │  int32 (LE)     │
│  19      │  Salt            │  32 B    │  Random Salt    │
│  51      │  IV              │  12 B    │  GCM nonce     │
│  63      │  CipherText      │  N B     │  Encrypted data │
│  63+N    │  AuthTag         │  16 B    │  GCM tag       │
└──────────────────────────────────────────────────────────────┘
```

**Field Description**:
- `Magic`: Fixed identifier for format recognition
- `Version`: Version number, supports future format upgrades
- `KdfIterations`: Iteration count, supports security parameter upgrades
- `Salt`: Random salt, prevents rainbow table attacks
- `IV`: GCM initialization vector, prevents known-plaintext attacks
- `CipherText`: AES-256-GCM encrypted content
- `AuthTag`: Message authentication tag, detects tampering

#### 3.3.4 Encrypted Content

The encrypted content in the archive is a JSON structure:

```json
{
  "originalFileName": "photo_001.jpg",
  "originalFileSize": 4194304,
  "mimeType": "image/jpeg",
  "createdAt": "2026-03-20T10:30:00Z",
  "watermarkOptions": {
    "text": "RESTRICTED",
    "opacity": 0.3,
    "fontSize": 80
  },
  "imageData": "<base64 encoded image data>"
}
```

### 3.4 Service Interface

```csharp
// SafeSeal.Core/ISafeTransferService.cs
public interface ISafeTransferService
{
    /// <summary>
    /// Create safe transfer archive from vault document
    /// </summary>
    Task CreateArchiveAsync(
        DocumentEntry document,
        string archivePath, 
        string pin, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Extract archive content (can import to vault)
    /// </summary>
    Task<TransferArchiveContent> ExtractArchiveAsync(
        string archivePath, 
        string pin, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Check if file is valid sstransfer format
    /// </summary>
    bool CanReadFormat(string filePath);
}

// Archive content
public sealed record TransferArchiveContent(
    string OriginalFileName,
    long OriginalFileSize,
    string MimeType,
    DateTime CreatedAt,
    WatermarkOptions? WatermarkOptions,
    byte[] ImageData);
```

### 3.5 Core Implementation

```csharp
// SafeSeal.Core/SafeTransferService.cs
public sealed class SafeTransferService : ISafeTransferService
{
    private static readonly byte[] Magic = "SSTRANSFER_V1"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int SaltSize = 32;
    private const int IvSize = 12;
    private const int TagSize = 16;
    private const int DefaultIterations = 200_000;

    private readonly IPinValidationService _pinService;
    private readonly IWatermarkRenderer _watermarkRenderer;

    public SafeTransferService(
        IPinValidationService pinService,
        IWatermarkRenderer watermarkRenderer)
    {
        _pinService = pinService;
        _watermarkRenderer = watermarkRenderer;
    }

    public async Task CreateArchiveAsync(
        DocumentEntry document,
        string archivePath,
        string pin,
        CancellationToken ct = default)
    {
        _pinService.ValidatePinFormat(pin);
        
        byte[] plainData = await LoadVaultDataAsync(document, ct);
        
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
        
        byte[] key = _pinService.DeriveAes256Key(pin, salt, DefaultIterations);
        
        byte[] cipherText = new byte[plainData.Length];
        byte[] tag = new byte[TagSize];
        
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(
                iv, 
                plainData, 
                cipherText, 
                tag, 
                associatedData: BuildAad(DefaultIterations));
        }
        
        await using var fs = File.Create(archivePath);
        await fs.WriteAsync(Magic, ct);
        fs.WriteByte(FormatVersion);
        await fs.WriteAsync(BitConverter.GetBytes(DefaultIterations), ct);
        await fs.WriteAsync(salt, ct);
        await fs.WriteAsync(iv, ct);
        await fs.WriteAsync(cipherText, ct);
        await fs.WriteAsync(tag, ct);
        
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plainData);
    }

    public async Task<TransferArchiveContent> ExtractArchiveAsync(
        string archivePath,
        string pin,
        CancellationToken ct = default)
    {
        _pinService.ValidatePinFormat(pin);
        
        byte[] fileData = await File.ReadAllBytesAsync(archivePath, ct);
        
        int pos = 0;
        
        if (!fileData.AsSpan(pos, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Invalid archive format.");
        pos += Magic.Length;
        
        byte version = fileData[pos++];
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported archive version: {version}");
        
        int iterations = BinaryPrimitives.ReadInt32LittleEndian(
            fileData.AsSpan(pos, 4));
        pos += 4;
        
        byte[] salt = fileData[pos..(pos + SaltSize)]; pos += SaltSize;
        byte[] iv = fileData[pos..(pos + IvSize)]; pos += IvSize;
        
        int cipherLen = fileData.Length - pos - TagSize;
        byte[] cipher = fileData[pos..(pos + cipherLen)]; pos += cipherLen;
        byte[] tag = fileData[pos..(pos + TagSize)];
        
        byte[] key = _pinService.DeriveAes256Key(pin, salt, iterations);
        byte[] plain = new byte[cipherLen];
        
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(iv, cipher, tag, plain, associatedData: BuildAad(iterations));
        }
        catch (CryptographicException)
        {
            await Task.Delay(500, ct); // UX-level delay; cannot prevent offline attack
            throw new UnauthorizedAccessException("PIN incorrect or archive corrupted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        
        string json = Encoding.UTF8.GetString(plain);
        var content = JsonSerializer.Deserialize<TransferArchiveContent>(json);
        
        CryptographicOperations.ZeroMemory(plain);
        
        return content!;
    }

    private static byte[] BuildAad(int iterations) =>
        Encoding.UTF8.GetBytes($"SSTRANSFER|V1|PBKDF2-SHA256|ITER={iterations}|AES-256-GCM");
}
```

### 3.6 User Interface Flow

#### 3.6.1 Create Archive

```
┌─────────────────────────────────────────────────────────────┐
│  Safe Transfer - Create Archive                              │
│  ─────────────────────────────────────────────────────    │
│                                                              │
│  Document: document_001.seal                               │
│  Original file: photo.jpg (4.2 MB)                        │
│                                                              │
│  Set 6-digit PIN:                                         │
│  ┌─────────────────────────────────────────────┐          │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                   │          │
│  └─────────────────────────────────────────────┘          │
│                                                              │
│  Confirm PIN:                                              │
│  ┌─────────────────────────────────────────────┐          │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                   │          │
│  └─────────────────────────────────────────────┘          │
│                                                              │
│  ⚠️ Warning: PIN cannot be recovered if lost!             │
│      Please save PIN separately from the archive             │
│                                                              │
│  [ Cancel ]                      [ Create Safe Transfer ]    │
└─────────────────────────────────────────────────────────────┘
```

#### 3.6.2 Load Archive

```
┌─────────────────────────────────────────────────────────────┐
│  Safe Transfer - Load Archive                               │
│  ─────────────────────────────────────────────────────    │
│                                                              │
│  Select archive file:                                       │
│  ┌─────────────────────────────────────────────┐          │
│  │ document_transfer.sstransfer          [Browse]│         │
│  └─────────────────────────────────────────────┘          │
│                                                              │
│  Enter 6-digit PIN:                                        │
│  ┌─────────────────────────────────────────────┐          │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                   │          │
│  └─────────────────────────────────────────────┘          │
│                                                              │
│  [ Cancel ]                              [ Decrypt & Import]│
└─────────────────────────────────────────────────────────────┘
```

### 3.7 Security Considerations

#### 3.7.1 Brute Force Protection

| Threat | Risk Level | Mitigation |
|--------|------------|------------|
| 6-digit PIN enumeration | High | PBKDF2 high iterations (200k) |
| Offline attack | High | KDF parameters in file, upgradeable |
| Known plaintext attack | Low | GCM mode + random IV |
| Metadata tampering | Medium | GCM auth tag + AAD |
| Memory leakage | Medium | CryptographicOperations.ZeroMemory |

#### 3.7.2 Future Security Enhancements (V2)

- Optional longer passphrase (not just 6 digits)
- Argon2替代 PBKDF2（memory-hard）
- Continuous failure lockout
- Key splitting (multi-party authorization)

---

## 4. Implementation Plan

### 4.1 Phase 1: Import UI Optimization

| Task | Priority | Estimated Time |
|------|----------|----------------|
| Add import hint UI | P0 | 1h |
| Internationalization text additions | P0 | 0.5h |
| Testing | P1 | 0.5h |

### 4.2 Phase 2: Safe Transfer Core

| Task | Priority | Estimated Time |
|------|----------|----------------|
| PINValidationService | P0 | 2h |
| SafeTransferService | P0 | 4h |
| Archive file format definition | P0 | 1h |
| Create archive UI | P0 | 2h |
| Load archive UI | P0 | 2h |
| Testing | P1 | 4h |

### 4.3 Phase 3: Security Enhancement

| Task | Priority | Estimated Time |
|------|----------|----------------|
| Memory cleanup audit | P1 | 1h |
| Error handling optimization | P1 | 1h |
| Security documentation | P2 | 2h |

---

## 5. Technical Debt

- [ ] Memory cleanup: Audit all ZeroMemory calls for sensitive data
- [ ] Error messages: Avoid leaking internal paths or key information
- [ ] Logging audit: Ensure PIN and keys are not recorded

---

## 6. Appendices

### 6.1 Related Files

- Code Review Report: `CODE_REVIEW_v2.0.md`
- Feature Improvement Report: `IMPROVEMENT_REPORT_v1.0.md`
- Feature Expansion Report: `FEATURE_EXPANSION_REPORT_v1.0.md`

### 6.2 Codex Technical Discussion Notes

**Key Derivation Parameters**:
- Algorithm: PBKDF2-SHA256
- Iterations: 200,000
- Salt: 32 bytes
- Output: 32 bytes (AES-256)

**File Format Key Points**:
- Magic: "SSTRANSFER_V1" (14 bytes)
- Version stored in file, supports upgrades
- AAD binds version and KDF parameters

**Risk Mitigation**:
- Offline brute force risk is high, but KDF parameters are upgradeable
- Client delay (500ms) only prevents rapid enumeration
- Memory plaintext residue risk mitigated via ZeroMemory

---

**Report Completion Date**: 2026-03-20  
**Next Step**: Final version output

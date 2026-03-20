# SafeSeal v2.0 Feature Detailed Planning Report

> Version: v2.0  
> Date: 2026-03-20  
> Report Type: Batch Processing + Template Improvement + Japanese i18n Detailed Planning  
> Sources: Master Requirements + Codex Deep Technical Discussion  
> Security Baseline: Practical First (PIN + PBKDF2 + AES-GCM)

---

## 1. Batch Processing Feature

### 1.1 Feature Overview

#### 1.1.1 Batch Watermark Export
- Select multiple photos
- Apply same watermark settings
- Batch export to files

#### 1.1.2 Merged Transfer File
- Multiple photos with same PIN
- Package into single `.sstransfer` file
- Recipient enters PIN once to extract all photos

### 1.2 Service Interface Design

```csharp
public record BatchProgress(
    int Total,
    int Completed,
    int Failed,
    string? CurrentFile,
    string Stage);

public record BatchFileError(string FilePath, string Code, string Message);

public record BatchResult(
    IReadOnlyList<string> OutputFiles,
    IReadOnlyList<BatchFileError> Errors,
    TimeSpan Elapsed);

public interface IBatchWatermarkService
{
    Task<BatchResult> ExportAsync(
        IReadOnlyList<string> inputFiles,
        WatermarkOptions watermarkOptions,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ITransferPackageService
{
    Task<string> CreateMergedPackageAsync(
        IReadOnlyList<string> inputFiles,
        string pin,
        string outputPath,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);

    Task<BatchResult> ExtractMergedPackageAsync(
        string packagePath,
        string pin,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);
}
```

### 1.3 Batch Processing Characteristics

| Feature | Description |
|---------|-------------|
| Error Tolerance | Single file failure doesn't stop batch (configurable `StopOnFirstError`) |
| Progress Reporting | `IProgress<BatchProgress>` real-time updates |
| Cancellation | `CancellationToken` support |
| Concurrency | Default `Environment.ProcessorCount / 2` |
| Memory Efficiency | Large file streaming (`FileStream` + chunked buffer) |

### 1.4 Error Model

```csharp
public class InvalidPinException : Exception { }
public class CorruptedTransferException : Exception { }
public class TemplateValidationException : Exception 
{ 
    public IReadOnlyList<string> ValidationErrors { get; } 
}
public class FileProcessException : Exception
{
    public string FilePath { get; }
    public string Reason { get; }
}
```

---

## 2. Merged Transfer File Format (SSTRANS2)

### 2.1 Design Goals

- Single `.sstransfer` package multiple files
- Single PIN unlocks all files
- Streaming processing (don't load all files into memory)
- Integrity verification + backward compatibility

### 2.2 Top-Level Structure (Little Endian)

```
+---------------------------+
| FixedHeader (80 bytes)    |  Fixed header
+---------------------------+
| HeaderAeadBlock           |  Encrypted header (directory, algorithm params, per-file info)
+---------------------------+
| EncryptedPayload[n]        |  n file ciphertext regions (sequential storage)
+---------------------------+
| EndMarker (optional)      |  End marker (quick verification)
+---------------------------+
```

### 2.3 FixedHeader (Plaintext, fixed 80 bytes)

| Offset | Length | Type | Field |
|--------|--------|------|-------|
| 0 | 8 | bytes | Magic=`SSTRANS2` |
| 8 | 1 | u8 | MajorVersion=2 |
| 9 | 1 | u8 | MinorVersion=0 |
| 10 | 2 | u16 | HeaderLength |
| 12 | 1 | u8 | KdfId (1=PBKDF2-SHA256) |
| 13 | 1 | u8 | EncId (1=AES-256-GCM) |
| 14 | 2 | u16 | Flags |
| 16 | 16 | bytes | PackageSalt |
| 32 | 4 | u32 | KdfIterations |
| 36 | 12 | bytes | HeaderNonce |
| 48 | 16 | bytes | HeaderTag |
| 64 | 8 | u64 | EncryptedHeaderSize |
| 72 | 8 | u64 | PayloadSize |

### 2.4 HeaderPlain (After Decryption)

```csharp
HeaderPlain {
    u32 FileCount;
    u64 CreatedUnixMs;
    string CreatorApp;          // "SafeSeal/2.x"
    string Locale;             // "zh-CN" / "en-US" / "ja-JP"
    FileEntry[FileCount] Files;
    map<string,string> Meta;   // Reserved for extension
}

FileEntry {
    u32 Index;
    string OriginalName;      // Original filename
    string RelativePath;      // Optional
    u64 PlainSize;
    u64 CipherOffset;         // Relative to payload start
    u64 CipherSize;           // Includes GCM tag
    byte[12] FileNonce;
    byte[16] FileTag;
    string Mime;             // Optional
    u32 Crc32Plain;         // Quick verification
}
```

### 2.5 File Ciphertext Region

- Each file encrypted with separate AES-256-GCM
- File key derivation: `FileKey_i = HKDF-SHA256(PackageKey, info="file:"+Index)`
- File AAD: `PackageId + Index + PlainSize`

### 2.6 PIN/KDF Parameters

| Parameter | Value |
|-----------|-------|
| Algorithm | PBKDF2-HMAC-SHA256 |
| Iterations | 310,000 |
| Salt Length | 16 bytes |
| Output Key | 32 bytes (AES-256) |
| PIN Preprocessing | NFKC + trim (full-width/half-width compatible) |

### 2.7 Integrity and Extension

- Rely on AES-GCM authentication, no separate HMAC needed
- `Flags` + `Meta` + `MinorVersion` support backward compatibility
- Optional `EndMarker`:
  - 8 bytes Magic=`SSTREND2`
  - 8 bytes TotalSize
  - 4 bytes HeaderCrc32

---

## 3. Template System Improvement

### 3.1 Current Problems

- Template definition is crude, variable system is incomplete
- Missing type validation and preview mechanism
- No support for conditional logic and formatting

### 3.2 Model Layering

| Class | Responsibility |
|-------|----------------|
| `TemplateDefinition` | Template metadata (ID, name, scenario, language, version) |
| `TemplateSchema` | Variable declaration (key, type, required, validation rules, default) |
| `TemplateRenderer` | Rendering engine (input context → output text) |
| `TemplatePreset` | User-saved "variable value snapshot" |

### 3.3 Variable Syntax

```
Basic variable:     {{applicant.name}}
Formatting:         {{date.now | date:"yyyy-MM-dd"}}
Fallback:          {{passport.no ?? "N/A"}}
Conditional block: {{#if visa.multiple}}...{{/if}}
```

### 3.4 Variable Types and Validation

| Type | Validation Rules |
|------|-----------------|
| string | regex / min / max / length |
| number | min / max |
| date | format / range |
| bool | - |
| enum | values[] |

### 3.5 Scenario Classification

| Classification | Description |
|---------------|-------------|
| visa | Visa application documents |
| legal | Legal documents |
| research | Research materials |
| general | General purpose |

Each category has 3-5 preset templates, supports local import/export (`.sstpl.json`).

### 3.6 Real-Time Preview

```
┌─────────────────────────────────────────────────────────┐
│  Template Editor                                      │
├─────────────────────────────────────────────────────────┤
│  Scenario：[Visa ▼]  Template：[Passport App ▼]       │
├───────────────────────────────┬─────────────────────────┤
│  Template Text               │  Real-time Preview       │
│  ┌───────────────────────┐  │  ┌─────────────────────┐│
│  │ {{applicant.name}}     │  │  │                     ││
│  │ {{date.now | date:   │  │  │ Zhang San 2026-03-20││
│  │ "yyyy-MM-dd"}}       │  │  │                     ││
│  │ Passport: {{passport}} │  │  │ Passport: 1234567   ││
│  └───────────────────────┘  │  └─────────────────────┘│
├───────────────────────────────┴─────────────────────────┤
│  Variable Panel                                       │
│  ┌─────────────────────────────────────────────────┐│
│  │ applicant.name [Zhang San     ] ✓                 ││
│  │ passport.no    [1234567      ] ✓                 ││
│  │ date.now      [2026-03-20  ] ✓                 ││
│  └─────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

---

## 4. Japanese Internationalization

### 4.1 Resource Structure

```
SafeSeal.App/
├── Resources/
│   ├── Strings.resx           # Default (English)
│   ├── Strings.zh-CN.resx    # Chinese Simplified
│   └── Strings.ja-JP.resx    # Japanese
```

### 4.2 Core Translation Vocabulary

| English | Japanese |
|---------|----------|
| Import | インポート |
| Export | エクスポート |
| Delete | 削除 |
| Settings | 設定 |
| About | について |
| Watermark | 透かし |
| Template | テンプレート |
| Language | 言語 |
| Theme | テーマ |
| Transfer | 転送 |
| Safe Transfer | 安全転送 |
| Batch Processing | 一括処理 |
| PIN | PIN |
| Cancel | キャンセル |
| Confirm | 確認 |
| Error | エラー |
| Success | 成功 |
| Warning | 警告 |
| Document | ドキュメント |
| Secure Vault | セキュリティ金庫 |

### 4.3 Date/Number Formatting

| Item | Japanese Format |
|------|-----------------|
| Date | yyyy/MM/dd |
| Time | HH:mm:ss |
| Numbers | 1,234.56 |
| Percentage | 12.34% |

### 4.4 UI Notes

- Reserve 20-30% width margin for long Japanese text
- Formal and concise copy
- Error messages use "reason + suggested action" format

---

## 5. UI/UX Flow Design

### 5.1 Batch Watermark Export (4-Step Wizard)

```
Step 1: Select Files
┌─────────────────────────────────────────────────────────┐
│  Batch Export - Select Files                             │
│  ─────────────────────────────────────────────────   │
│  Drag files here or click to select                     │
│  ┌─────────────────────────────────────────────────┐ │
│  │ 📁 Drag files here                              │ │
│  └─────────────────────────────────────────────────┘ │
│  Selected: 12 files (156.3 MB)                        │
│  [ Continue ]                                          │
└─────────────────────────────────────────────────────────┘

Step 2: Watermark Settings
┌─────────────────────────────────────────────────────────┐
│  Batch Export - Watermark Settings                      │
│  ─────────────────────────────────────────────────   │
│  Template：[Passport App ▼]  [+ New]                  │
│  Watermark text: {{applicant.name}} - {{date.now}}     │
│  Font Size：[====●=====] 80                           │
│  Opacity：  [====●=====] 30%                         │
│  Angle：-35°                                          │
│  ┌─────────────────────────┐ │
│  │   [Real-time Preview]   │ │
│  └─────────────────────────┘ │
│  [ Previous ] [ Continue ]                               │
└─────────────────────────────────────────────────────────┘

Step 3: Output Settings
┌─────────────────────────────────────────────────────────┐
│  Batch Export - Output Settings                         │
│  ─────────────────────────────────────────────────   │
│  Output directory：[C:\Users\...\Export    ] [ Browse ]│
│  File naming：[{{original}}_{{date}}]                   │
│  Conflict strategy：[Overwrite ○ / Rename ○ / Skip ●]│
│  [ Previous ] [ Continue ]                              │
└─────────────────────────────────────────────────────────┘

Step 4: Execute and Results
┌─────────────────────────────────────────────────────────┐
│  Batch Export - Processing                             │
│  ─────────────────────────────────────────────────   │
│  ████████████░░░░░░░░░░░░  67% (8/12)             │
│  Current：photo_008.jpg                                │
│  [ Cancel ]                                          │
└─────────────────────────────────────────────────────────┘
```

### 5.2 Create Merged Transfer File

```
Step 1: Select Files
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Create Package                        │
│  ─────────────────────────────────────────────────   │
│  Check files to package:                              │
│  ☑ photo_001.jpg (4.2 MB)                          │
│  ☑ photo_002.jpg (3.8 MB)                          │
│  ☑ photo_003.jpg (5.1 MB)                          │
│  Total: 3 files, 13.1 MB                            │
│  [ Continue ]                                          │
└─────────────────────────────────────────────────────────┘

Step 2: Set PIN
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Set PIN                               │
│  ─────────────────────────────────────────────────   │
│  Set 6-digit PIN:                                    │
│  ┌─────────────────────────────────────────────┐   │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                  │   │
│  └─────────────────────────────────────────────┘   │
│  Confirm PIN:                                         │
│  ┌─────────────────────────────────────────────┐   │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                  │   │
│  └─────────────────────────────────────────────┘   │
│  ⚠️ PIN cannot be recovered if lost!                  │
│  [ Previous ] [ Create Transfer Package ]             │
└─────────────────────────────────────────────────────────┘

Step 3: Complete
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Complete                             │
│  ─────────────────────────────────────────────────   │
│  ✅ Transfer package created successfully!             │
│  File：transfer_package_20260320.sstrans (13.1 MB) │
│  Files: 3                                           │
│  Created: 2026-03-20 01:30                          │
│  [ Open Folder ]  [ Copy Path ]                      │
└─────────────────────────────────────────────────────────┘
```

### 5.3 Extract Package Flow

```
Step 1: Select File
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Load Package                         │
│  ─────────────────────────────────────────────────   │
│  Select transfer package:                             │
│  ┌─────────────────────────────────────────────┐   │
│  │ transfer_package.sstransfer           [Browse]│ │
│  └─────────────────────────────────────────────┘   │
│  [ Continue ]                                         │
└─────────────────────────────────────────────────────────┘

Step 2: Enter PIN
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Enter PIN                             │
│  ─────────────────────────────────────────────────   │
│  Enter 6-digit PIN to decrypt:                        │
│  ┌─────────────────────────────────────────────┐   │
│  │ [ ] [ ] [ ] [ ] [ ] [ ]                  │   │
│  └─────────────────────────────────────────────┘   │
│  [ Cancel ]  [ Decrypt and Preview ]                 │
└─────────────────────────────────────────────────────────┘

Step 3: Preview and Extract
┌─────────────────────────────────────────────────────────┐
│  Safe Transfer - Preview                              │
│  ─────────────────────────────────────────────────   │
│  Package contents:                                    │
│  📄 photo_001.jpg (4.2 MB)                          │
│  📄 photo_002.jpg (3.8 MB)                          │
│  📄 photo_003.jpg (5.1 MB)                          │
│  ─────────────────────────────────────────────────   │
│  Output directory：[C:\Users\...\Documents    ] [Browse]│
│  [ Cancel ]  [ Export All ]                         │
└─────────────────────────────────────────────────────────┘
```

---

## 6. Implementation Plan

### 6.1 Sprint 1 (2 Weeks)

| Task | Description | Time |
|------|-------------|------|
| Template System Refactor | Implement `TemplateDefinition`, `TemplateSchema`, `TemplateRenderer` | 16h |
| Variable Syntax Parser | Implement `{{var}}`, `{{date\|format}}` parsing | 8h |
| Real-time Preview Component | XAML two-way binding preview | 8h |

### 6.2 Sprint 2 (2 Weeks)

| Task | Description | Time |
|------|-------------|------|
| Batch Processing Service | Implement `IBatchWatermarkService` | 16h |
| Progress Reporting UI | Progress bar, cancel, error display | 8h |
| Scenario Template Presets | visa/legal/research/general classification | 8h |

### 6.3 Sprint 3 (2 Weeks)

| Task | Description | Time |
|------|-------------|------|
| Merged Transfer Format | Implement `SSTRANS2` format encoding/decoding | 16h |
| Merged Package Creation | Implement `CreateMergedPackageAsync` | 8h |
| Merged Package Extraction | Implement `ExtractMergedPackageAsync` | 8h |

### 6.4 Sprint 4 (2 Weeks)

| Task | Description | Time |
|------|-------------|------|
| Japanese Resource File | Create `Strings.ja-JP.resx` | 8h |
| Japanese UI Testing | Pseudo-localization testing, screenshot regression | 8h |
| Integration Testing | End-to-end batch processing test | 8h |

---

## 7. Technical Debt

- [ ] Large file memory optimization for streaming encryption/decryption
- [ ] Configurable concurrency
- [ ] Template import/export format standardization (.sstpl.json)
- [ ] Error message localization

---

## 8. Appendices

### 8.1 Related Files

- Safe Transfer Report: `SAFE_TRANSFER_REPORT_v2.0.md`
- Product Innovation Report: `PRODUCT_INNOVATION_REPORT.md`
- Code Review Report: `CODE_REVIEW_v2.0.md`

### 8.2 Codex Technical Discussion Notes

**Security Baseline**: Practical First (PIN + PBKDF2 + AES-GCM)

**Merged Transfer Format Key Points**:
- Magic: `SSTRANS2` (8 bytes)
- Version stored in file, supports upgrade
- AAD binds version and KDF parameters
- Streaming processing avoids memory overflow

**Template System Key Points**:
- Variable syntax: `{{var}}`, `{{date|format}}`
- Type validation: regex/min/max/length
- Scenario classification: visa/legal/research/general

**Japanese i18n Key Points**:
- Date format: `yyyy/MM/dd`
- Terminology unified table
- UI width margin reservation

---

**Report Completion Date**: 2026-03-20  
**Next Step**: Implementation code development

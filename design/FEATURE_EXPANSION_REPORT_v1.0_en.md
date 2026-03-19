# SafeSeal Feature Expansion Report

> Version: v1.0  
> Date: 2026-03-19  
> Report Type: Feature Expansion and Maturity Planning  
> Sources: Code Review Report + Codex Joint Review + Open Source Maturity Best Practices

---

## 1. Background and Objectives

### 1.1 Report Purpose

This report aims to elevate SafeSeal from a "functional prototype" to a "mature open source project," focusing on:
1. **Explicit requirements from Master**: About page, language switching
2. **Codex analysis supplement**: Project governance, observability, internationalization architecture
3. **Industry best practices**: Maturity paths for similar open source utilities

### 1.2 Requirements Sources

| Source | Specific Requirements |
|--------|---------------------|
| Master | About page (version number, license) |
| Master | Chinese/English interface switching |
| Codex Review | Project governance documents (CHANGELOG/CONTRIBUTING) |
| Codex Review | Observability (diagnostics page, logging) |
| Industry Practice | Internationalization framework, semantic versioning |

### 1.3 Relationship with Improvement Report

This document complements `IMPROVEMENT_REPORT_v1.0.md`:
- **Improvement Report**: Focuses on security, performance, core functionality
- **Expansion Report**: Focuses on maturity, internationalization, user experience

---

## 2. Core Feature Expansion

### 2.1 About Page

#### 2.1.1 Functional Requirements

| Element | Description |
|---------|-------------|
| Application Icon | 256x256 high-resolution icon |
| Application Name | SafeSeal |
| Version Number | Semantic version (e.g., v1.0.0) |
| Build Information | Build 20260319 |
| Copyright | © 2026 SafeSeal Contributors |
| License | MIT License |
| Acknowledgments | Third-party library list |
| Links | GitHub repository, issue feedback |

#### 2.1.2 Implementation

```csharp
// SafeSeal.Core/VersionInfo.cs
public static class VersionInfo
{
    public static Version Current => typeof(VersionInfo).Assembly.GetName().Version!;
    
    public static string SemanticVersion => "v1.0.0";
    
    public static string BuildDate => "2026-03-19";
    
    public static string FullInfo => $"""
        SafeSeal {SemanticVersion}
        Build: {BuildDate}
        .NET {Environment.Version}
        License: MIT
        """;
}
```

#### 2.1.3 UI Design

```
┌─────────────────────────────────────────────────────────┐
│  [Icon]                                                │
│                                                         │
│  SafeSeal                                              │
│  Version v1.0.0 (Build 20260319)                      │
│                                                         │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  A secure tool for watermarking and storing             │
│  sensitive photographs.                                 │
│                                                         │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  License: MIT License                                  │
│                                                         │
│  Third-party Dependencies:                             │
│  • CommunityToolkit.Mvvm (MIT)                        │
│  • Microsoft.Data.Sqlite (MIT)                        │
│                                                         │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  [ GitHub ]  [ Report Issue ]  [ Close ]              │
└─────────────────────────────────────────────────────────┘
```

---

### 2.2 Internationalization (i18n)

#### 2.2.1 Design Goals

| Dimension | Current | Target |
|-----------|---------|--------|
| Language | Hard-coded Chinese | Chinese/English bilingual support |
| Switching | None | Runtime switching |
| Extensibility | None | Easy to add new languages |
| Formatting | Fixed | Locale-aware |

#### 2.2.2 Technical Architecture

**Recommended Solution: .NET ResourceManager + RESX Files**

```
SafeSeal.App/
├── Resources/
│   ├── Strings.resx           # Default (English)
│   ├── Strings.zh-CN.resx     # Chinese (Simplified)
│   └── Strings.zh-TW.resx     # Chinese (Traditional) - Reserved
├── Services/
│   └── LocalizationService.cs  # Language switching service
└── ViewModels/
    └── SettingsViewModel.cs    # Settings page VM
```

#### 2.2.3 Resource File Structure

```xml
<!-- Strings.resx (English - Default) -->
<root>
  <data name="AppTitle" xml:space="preserve">
    <value>SafeSeal</value>
  </data>
  <data name="Import" xml:space="preserve">
    <value>Import</value>
  </data>
  <data name="Export" xml:space="preserve">
    <value>Export</value>
  </data>
  <data name="Delete" xml:space="preserve">
    <value>Delete</value>
  </data>
  <data name="Settings" xml:space="preserve">
    <value>Settings</value>
  </data>
  <data name="About" xml:space="preserve">
    <value>About</value>
  </data>
  <data name="Language" xml:space="preserve">
    <value>Language</value>
  </data>
  <data name="Opacity" xml:space="preserve">
    <value>Opacity</value>
  </data>
  <data name="WatermarkText" xml:space="preserve">
    <value>Watermark Text</value>
  </data>
  <data name="Error_FileNotFound" xml:space="preserve">
    <value>File not found.</value>
  </data>
  <data name="Error_DecryptionFailed" xml:space="preserve">
    <value>Decryption failed. File may be corrupted.</value>
  </data>
</root>
```

```xml
<!-- Strings.zh-CN.resx (Chinese) -->
<root>
  <data name="AppTitle" xml:space="preserve">
    <value>SafeSeal</value>
  </data>
  <data name="Import" xml:space="preserve">
    <value>导入</value>
  </data>
  <data name="Export" xml:space="preserve">
    <value>导出</value>
  </data>
  <data name="Delete" xml:space="preserve">
    <value>删除</value>
  </data>
  <data name="Settings" xml:space="preserve">
    <value>设置</value>
  </data>
  <data name="About" xml:space="preserve">
    <value>关于</value>
  </data>
  <data name="Language" xml:space="preserve">
    <value>语言</value>
  </data>
  <data name="Opacity" xml:space="preserve">
    <value>透明度</value>
  </data>
  <data name="WatermarkText" xml:space="preserve">
    <value>水印文字</value>
  </data>
  <data name="Error_FileNotFound" xml:space="preserve">
    <value>文件未找到。</value>
  </data>
  <data name="Error_DecryptionFailed" xml:space="preserve">
    <value>解密失败，文件可能已损坏。</value>
  </data>
</root>
```

#### 2.2.4 Language Switching Service

```csharp
// SafeSeal.App/Services/LocalizationService.cs
public sealed class LocalizationService
{
    private static readonly LocalizationService _instance = new();
    public static LocalizationService Instance => _instance;
    
    public event EventHandler? LanguageChanged;
    
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name != value.Name)
            {
                _currentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                Thread.CurrentThread.CurrentCulture = value;
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    
    public void SetLanguage(string cultureName)
    {
        CurrentCulture = new CultureInfo(cultureName);
        SavePreference(cultureName);
    }
    
    public void LoadSavedLanguage()
    {
        string? saved = LoadPreference();
        if (!string.IsNullOrEmpty(saved))
            SetLanguage(saved);
    }
    
    private const string PreferenceKey = "Language";
    
    private void SavePreference(string cultureName) =>
        SafeSealStorageOptions.Settings[PreferenceKey] = cultureName;
    
    private string? LoadPreference() =>
        SafeSealStorageOptions.Settings.TryGetValue(PreferenceKey, out var v) ? v : null;
}
```

#### 2.2.5 XAML Binding Examples

```xml
<!-- MainWindow.xaml -->
<TextBlock Text="{Binding LocalizedStrings[Import]}" />
<Button Content="{Binding LocalizedStrings[Export]}" />
<ComboBox ItemsSource="{Binding AvailableLanguages}" 
          SelectedItem="{Binding SelectedLanguage}" />
```

#### 2.2.6 Implementation Steps

| Phase | Task | Estimated Time |
|-------|------|----------------|
| 1 | Create RESX file structure | 1h |
| 2 | Extract existing hard-coded strings | 2h |
| 3 | Implement LocalizationService | 1h |
| 4 | Create settings page (language selection) | 2h |
| 5 | Integrate into main interface | 1h |
| 6 | Test Chinese/English switching | 1h |

---

### 2.3 Settings Page

#### 2.3.1 Feature Planning

| Setting | Type | Default Value |
|---------|------|---------------|
| Language | Dropdown | System Language |
| Theme | Dropdown (Light/Dark/System) | System |
| Log Level | Dropdown (Debug/Info/Warning) | Warning |
| Auto-save Preview | Toggle | On |
| Preview Quality | Slider 1-100 | 85 |
| Data Directory | Path Selection | Default Path |
| Diagnostics | Button (Open Log Directory) | - |

#### 2.3.2 Implementation

```csharp
// SafeSeal.Core/SafeSealSettings.cs
public sealed record SafeSealSettings
{
    public string Language { get; init; } = "en-US";
    public string Theme { get; init; } = "System";
    public LoggingLevel LogLevel { get; init; } = LoggingLevel.Warning;
    public bool AutoSavePreview { get; init; } = true;
    public int PreviewQuality { get; init; } = 85;
    public string DataDirectory { get; init; } = "";
}

public enum LoggingLevel { Debug, Info, Warning, Error }
```

---

### 2.4 Observability

#### 2.4.1 Structured Logging

**Current State**: No logging system

**Goal**: Complete logging infrastructure

```csharp
// SafeSeal.Core/LoggingService.cs
public static class LoggingService
{
    private static ILoggerFactory? _factory;
    
    public static void Initialize(LoggingLevel minimumLevel)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSeal", "Logs");
        
        Directory.CreateDirectory(logDir);
        
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddProvider(new FileLoggerProvider(
                Path.Combine(logDir, $"safeseal_{DateTime.Now:yyyyMMdd}.log"),
                new FileLoggerOptions { RetainDays = 7 }));
        });
    }
    
    public static ILogger<T> GetLogger<T>() => _factory!.CreateLogger<T>();
}
```

**Log Example**:
```
2026-03-19 18:00:00.123 [INF] SafeSeal.Core.VaultManager: File saved successfully. Path=C:\Users\...\vault\abc123.seal Size=2048576
2026-03-19 18:00:01.456 [WRN] SafeSeal.Core.WatermarkRenderer: JPEG quality 95 exceeds recommended max 95, clamped
2026-03-19 18:00:02.789 [ERR] SafeSeal.Core.VaultManager: Decryption failed. Path=... Exception=CryptographicException
```

#### 2.4.2 Diagnostics Page

```
┌─────────────────────────────────────────────────────────┐
│  SafeSeal Diagnostics                                  │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  Version: v1.0.0 (Build 20260319)                     │
│  .NET: 10.0.0                                         │
│  OS: Windows 11 23H2                                  │
│                                                         │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  Data Directory: C:\Users\...\AppData\...             │
│  Log Directory: C:\Users\...\Local\SafeSeal           │
│  Cache Size: 128.5 MB                                  │
│  Stored Items: 24                                     │
│                                                         │
│  ─────────────────────────────────────────────────    │
│                                                         │
│  [ Open Log Directory ]  [ Export Diagnostics ]       │
│                                                         │
│  [ Close ]                                            │
└─────────────────────────────────────────────────────────┘
```

---

## 3. Project Governance Expansion

### 3.1 CHANGELOG

#### 3.1.1 Format Specification (Keep a Changelog)

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-03-19

### Added
- Initial release
- Core encryption using DPAPI + HMAC-SHA256
- Watermark rendering with customizable text
- Export to JPG/PNG formats
- Basic vault management (import, delete, rename)
- WPF user interface

### Security
- CurrentUser-scoped encryption
- HMAC integrity verification

### Known Issues
- Path traversal vulnerability (see IMPROVEMENT_REPORT_v1.0.md)
- Non-atomic delete operations
- Template system not yet implemented

## [0.1.0] - 2026-02-01

### Added
- Project scaffolding
- Basic prototype implementation
```

#### 3.1.2 Release Workflow

```
1. Determine version number (git tag v1.0.0)
         ↓
2. Update CHANGELOG.md
         ↓
3. Update VERSION_INFO (AssemblyVersion)
         ↓
4. Create Release PR
         ↓
5. Auto-build on merge
         ↓
6. GitHub Release publication
         ↓
7. Auto-generate Release Notes
```

---

### 3.2 CONTRIBUTING.md

#### 3.2.1 Contribution Guide Structure

```markdown
# Contributing to SafeSeal

## Code of Conduct
Please read [CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md) before contributing.

## Getting Started

### Prerequisites
- .NET 10 SDK
- Windows 10/11

### Development Setup
1. Fork the repository
2. Clone your fork
3. Create a feature branch
4. Run tests: `dotnet test`
5. Build: `dotnet build`

## Development Workflow

### 1. Create an Issue
Before starting work, please create an issue to discuss the change.

### 2. Branch Naming
- `feature/` - New features
- `fix/` - Bug fixes
- `refactor/` - Code refactoring
- `docs/` - Documentation

### 3. Commit Messages
Follow [Conventional Commits](https://www.conventionalcommits.org/):
```
feat: add language switcher to settings
fix: resolve path traversal in HiddenVaultStorage
docs: update README with new screenshots
```

### 4. Pull Request Process
1. Ensure all tests pass
2. Update documentation if needed
3. Request review from maintainers
4. Squash commits before merging

## Coding Standards

### C# Style Guide
- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use nullable reference types
- Prefer records for immutable types
- Add XML doc comments for public APIs

### Testing
- Unit tests required for new features
- Maintain >80% code coverage for Core library
- Use xUnit framework

## Security
- Do not commit secrets or credentials
- Run `dotnet security` before submitting PR
- Follow secure coding practices
```

---

### 3.3 CODE_OF_CONDUCT.md

```markdown
# Code of Conduct

## Our Pledge
We pledge to make participation in our project a harassment-free experience for everyone.

## Our Standards
Examples of behavior that contributes to a positive environment:
- Using welcoming and inclusive language
- Being respectful of differing viewpoints
- Gracefully accepting constructive criticism
- Focusing on what is best for the community

## Enforcement
Instances of abusive, harassing, or otherwise unacceptable behavior may be
reported to the project maintainers. All complaints will be reviewed and
investigated promptly and fairly.

## Attribution
This Code of Conduct is adapted from the [Contributor Covenant](https://www.contributor-covenant.org/).
```

---

### 3.4 ISSUE Templates

#### Bug Report Template
```markdown
## Bug Description
A clear description of the bug.

## Steps to Reproduce
1. Go to '...'
2. Click on '...'
3. See error

## Expected Behavior
What should happen.

## Actual Behavior
What actually happens.

## Environment
- OS: [e.g. Windows 11]
- Version: [e.g. v1.0.0]
- Build: [e.g. 20260319]

## Related Logs
```
[paste relevant log entries]
```

## Screenshots
[If applicable]
```

#### Feature Request Template
```markdown
## Feature Description
A clear description of the feature you want.

## Use Case
Who would use this feature and why.

## Proposed Solution
Your proposed solution.

## Alternatives
Other solutions you've considered.

## Additional Context
Any other context or screenshots.
```

---

## 4. Maturity Expansion

### 4.1 Semantic Versioning (SemVer)

| Version Format | Description |
|----------------|-------------|
| MAJOR | Incompatible API changes |
| MINOR | Backward-compatible new features |
| PATCH | Backward-compatible bug fixes |

**SafeSeal Version Plan**:

| Version | Status | Milestone |
|---------|--------|-----------|
| 0.1.0 | Historical | Prototype stage |
| 0.2.0 | Planned | Security fixes + Template system MVP |
| 1.0.0 | Target | First stable release |

---

### 4.2 CI/CD Pipeline

#### GitHub Actions Configuration

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Restore
        run: dotnet restore
        
      - name: Build
        run: dotnet build --configuration Release --no-restore
        
      - name: Test
        run: dotnet test --configuration Release --no-build --verbosity normal
        
      - name: Security Scan
        run: dotnet tool install --global dotnet-sec &&
             dotnet-sec scan
      
      - name: Pack
        if: github.ref == 'refs/heads/main'
        run: dotnet publish -c Release -r win-x64 --self-contained

  release:
    needs: build
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Create Release
        uses: softprops/action-gh-release@v1
        with:
          files: SafeSeal.App/bin/Release/net10.0-windows/win-x64/publish/*.exe
          generate_release_notes: true
```

---

### 4.3 Dependency Security Scanning

```bash
# Install tools
dotnet tool install --global dotnet-outdated-tool
dotnet tool install --global dotnet-deps-check

# Check outdated dependencies
dotnet-outdated

# Check for security vulnerabilities
dotnet list package --vulnerable
```

---

## 5. Expansion Roadmap

### Phase 1: Basic Maturity (v0.2)

| Feature | Priority | Estimated Time |
|---------|----------|----------------|
| About page | P0 | 2h |
| Language switching (Chinese/English) | P0 | 8h |
| Basic settings page | P1 | 4h |
| CHANGELOG establishment | P0 | 1h |
| CONTRIBUTING.md | P1 | 2h |
| ISSUE templates | P1 | 1h |

### Phase 2: Observability (v0.3)

| Feature | Priority | Estimated Time |
|---------|----------|----------------|
| Logging service | P1 | 4h |
| Diagnostics page | P1 | 3h |
| Structured logging | P1 | 2h |

### Phase 3: Governance Enhancement (v0.4)

| Feature | Priority | Estimated Time |
|---------|----------|----------------|
| CI/CD pipeline | P1 | 4h |
| Security scanning integration | P2 | 2h |
| CODE_OF_CONDUCT | P2 | 1h |
| Semantic versioning | P2 | 1h |

### Phase 4: Advanced Features (v1.0)

| Feature | Priority | Estimated Time |
|---------|----------|----------------|
| Dark theme | P2 | 6h |
| Auto-update | P2 | 8h |
| Command-line interface | P3 | 12h |
| Plugin system | P3 | 20h |

---

## 6. Technical Debt

### 6.1 Internationalization Debt

- [ ] Hard-coded strings not fully extracted
- [ ] Date/number formatting not locale-aware
- [ ] RTL (Right-to-Left) support missing

### 6.2 Testing Debt

- [ ] UI automation tests missing
- [ ] Localization tests missing
- [ ] Logging tests missing

---

## 7. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Internationalization introducing bugs | Medium | Medium | Complete localization testing |
| String hard-coding omissions | High | Low | Static analysis tools |
| Multi-language maintenance cost | Medium | Medium | Automated checks |

---

## 8. Appendices

### 8.1 Related Documents

- Feature Improvement Report: `IMPROVEMENT_REPORT_v1.0.md`
- Code Review Report: `CODE_REVIEW_REPORT.md`
- Project Design Documentation: `design/project_doc.md`

### 8.2 References

- [.NET Internationalization Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/globalization-localization)
- [Keep a Changelog](https://keepachangelog.com/)
- [Semantic Versioning](https://semver.org/)
- [Conventional Commits](https://www.conventionalcommits.org/)

---

**Report Completion Date**: 2026-03-19  
**Next Step**: Joint Review with Codex → Final Version Output

## 9. Codex Joint Review Comments

### 9.1 Overall Assessment

The report structure is complete and the direction is correct, forming a complementary "security/core capabilities + maturity/experience" framework with the existing `IMPROVEMENT_REPORT_v1.0.md`.

The internationalization and governance plans are implementable, but the current time estimates are optimistic, and executing at the proposed priorities would create resource competition with the security fixes phase.

### 9.2 Technical Feasibility Assessment

| Item | Conclusion | Review Notes |
|------|------------|--------------|
| Internationalization architecture (ResourceManager + RESX) | Feasible (Medium-High) | Architecture choice is correct for WPF. Recommend adding `DefaultThreadCurrentUICulture/DefaultThreadCurrentCulture` and startup language restoration strategy. |
| RESX file structure | Direction correct (Medium) | Key design is usable, but the example is simplified XML, missing standard `.resx` headers and `resheader` elements. Cannot be directly used as-is. |
| Language switching service design | General feasibility (Medium) | `LocalizationService` concept is usable, but the example depends on `SafeSealStorageOptions.Settings` which doesn't exist in current code. WPF binding refresh mechanism is not covered. |
| Integration with existing code | Feasible (Medium) | Requires new settings persistence layer, resource access wrapper, and ViewModel binding properties. `MainWindow.xaml`, `MainViewModel`, and `UserFacingErrorHandler` have extensive hard-coding requiring significant refactoring. |

### 9.3 Completeness Check

1. Hard-coded string extraction priority: Currently only a general task, missing layered priorities. Recommend:
   - P0: Main flow buttons/dialogs/error messages
   - P1: Settings page and status text
   - P2: Logs and diagnostics text, edge case messages

2. Roadmap time estimates: "Language switching (8h)" and "Extract hard-coded strings (2h)" are optimistic. For current codebase scale, recommend 14-24h (including regression testing).

3. Complementarity with Improvement Report: No direct conflicts between the two reports, but priorities need alignment. The P0 security fixes in `IMPROVEMENT_REPORT_v1.0.md` should precede P0 maturity tasks in this report.

4. Maturity elements: Version, CI/CD, contribution guidelines, and ISSUE templates are covered. Still recommend adding: Release signing/artifact verification, Support Policy, SBOM/dependency pinning, and failure recovery/rollback criteria (DoD).

### 9.4 Improvement Suggestions

1. Establish `ISettingsStore` first (JSON or SQLite), then implement language preference persistence. Avoid binding to UI before infrastructure is ready.

2. Add `LocExtension` or unified `ILocalizationProvider` for localization. Avoid scattered dictionary index bindings in XAML.

3. Add localization tests: Resource key integrity checks, culture switching UI smoke tests, formatting (date/number) regression tests.

4. Add DoD to milestones: Each Phase needs defined "pass conditions + rollback conditions."

**Code Example Compile Feasibility Check (Static Review)**
> Current environment lacks `dotnet` SDK; cannot execute actual compilation. The following is static compile feasibility assessment based on existing codebase.

| Example | Conclusion | Required Corrections |
|---------|------------|----------------------|
| `VersionInfo.cs` (2.1.2) | Generally compilable | Needs namespace confirmation and project placement; `SemanticVersion/BuildDate` recommend build injection instead of hardcoding. |
| `LocalizationService.cs` (2.2.4) | Not directly compilable | `SafeSealStorageOptions.Settings` doesn't exist in current code; needs settings storage service injection. Also needs `using System.Globalization; using System.Threading;`. |
| XAML binding example (2.2.5) | Not directly compilable | `LocalizedStrings`, `AvailableLanguages`, `SelectedLanguage` don't exist in current `MainViewModel`. |
| `SafeSealSettings.cs` (2.3.2) | Compilable | Recommend avoiding custom `LogLevel` name collision with `Microsoft.Extensions.Logging.LogLevel`. |
| `LoggingService.cs` (2.4.1) | Not directly compilable | `readonly` field `_factory` cannot be assigned in `Initialize`; `FileLoggerProvider`/`FileLoggerOptions` undefined; current project lacks logging implementation dependencies. |
| CI YAML (4.2) | Needs adjustment | `dotnet-sec` command and tool name need verification; current写法 will likely fail in actual pipeline. |

### 9.5 Priority Adjustment Recommendations

Recommended unified execution order for both reports:

1. **P0 (Execute First)**: Path traversal fix, delete consistency fix, security regression tests (from Improvement Report Phase 0).

2. **P1**: Internationalization infrastructure (RESX + settings persistence + main flow text extraction + language switching).

3. **P2**: About page, basic governance documents (CHANGELOG/CONTRIBUTING/ISSUE templates), logging MVP implementation.

4. **P3**: Diagnostics page, CI/CD strengthening, security scanning automation, themes and advanced features.

This ensures "stop the bleeding first, then mature" and reduces rework risk.

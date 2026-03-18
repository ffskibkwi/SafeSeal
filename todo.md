Here's a structured set of prompts to feed Codex CLI in sequence. The order matters 鈥?each phase builds on the last.

---

## Phase 0 鈥?Project Scaffolding

**Prompt 1 [x] 鈥?Generate `.gitignore` first (as you noted)**
```
Generate a .gitignore file for a .NET 9 WPF application project. 
Include rules for: Visual Studio build artifacts, .NET runtime outputs 
(bin/, obj/), user-specific VS settings (.vs/, *.user, *.suo), 
NuGet package cache, and any *.seal vault files that should never 
be committed. Save it to the repository root.
```

**Prompt 2 [x] 鈥?Scaffold the solution structure**
```
Read ./design/project_doc.md. Based on the architecture described, 
scaffold a .NET 9 WPF solution named "SafeSeal" with the following 
project structure:

SafeSeal/
鈹溾攢鈹€ SafeSeal.App/          # WPF frontend (XAML + ViewModels)
鈹溾攢鈹€ SafeSeal.Core/         # VaultManager, crypto logic, file format
鈹溾攢鈹€ SafeSeal.Tests/        # xUnit test project
鈹斺攢鈹€ SafeSeal.sln

Use MVVM pattern. Add project references so App depends on Core, 
and Tests depends on Core. Do not write any feature code yet.
```

---

## Phase 1 鈥?Core Security Layer

**Prompt 3 [x] 鈥?`.seal` file format and header**
```
Read ./design/project_doc.md, section "Storage Specification". 

In SafeSeal.Core, create a SealFileHeader.cs class that represents 
the 40-byte .seal file header with these fields:
- Magic (4 bytes, ASCII "SEAL")
- Version (2 bytes, major/minor, current value 1.0)
- HMAC (32 bytes, HMACSHA256 of plaintext)

Include:
- A static Parse(byte[] fileData) method that validates magic bytes, 
  throws InvalidDataException if invalid, and throws 
  NotSupportedException if version major != 1
- A ToBytes() serialization method
- XML doc comments on every public member
```

**Prompt 4 [x] 鈥?VaultManager crypto implementation**
```
Read ./design/project_doc.md, sections 2.1, 2.2, and 4.1.

In SafeSeal.Core, implement VaultManager.cs with these exact 
requirements:

1. DPAPI scope: DataProtectionScope.CurrentUser
2. Entropy: derive at runtime from 
   (WindowsIdentity.GetCurrent().User.Value + "SafeSealV1") 
   encoded as UTF-8 bytes 鈥?do NOT hardcode a static byte array
3. Save(byte[] rawData, string path): 
   - Compute HMACSHA256 of rawData using the derived entropy as key
   - Encrypt rawData with DPAPI + entropy
   - Write SealFileHeader + encrypted payload to disk
4. LoadSecurely(string path):
   - Parse and validate SealFileHeader
   - Decrypt payload
   - Verify HMACSHA256 鈥?throw CryptographicException if mismatch
   - Return decrypted bytes
5. Memory safety: ALL byte[] buffers containing plaintext must be 
   pinned with GCHandle.Alloc(Pinned) and zeroed with Array.Clear() 
   in a finally block before the handle is freed
6. No LINQ on large byte arrays 鈥?use Range operators (data[38..]) 
   for slicing
```

**Prompt 5 [x] 鈥?Unit tests for VaultManager**
```
In SafeSeal.Tests, write xUnit unit tests for VaultManager covering:

1. Round-trip: Save then LoadSecurely returns identical bytes
2. Tamper detection: Modify one byte in the encrypted payload, 
   confirm LoadSecurely throws CryptographicException
3. Wrong file: Pass a file without "SEAL" magic bytes, confirm 
   InvalidDataException is thrown
4. Empty input: Confirm Save throws ArgumentException for 
   null or zero-length input

Use a temp directory (Path.GetTempPath()) for test files and 
clean up in Dispose(). Do not mock DPAPI 鈥?test against 
the real implementation.
```

---

## Phase 2 鈥?Watermark Engine

**Prompt 6 [x] 鈥?Watermark renderer**
```
Read ./design/project_doc.md, section 3.2 and 5.1.

In SafeSeal.Core, create WatermarkRenderer.cs with a single method:

BitmapSource Render(byte[] imageBytes, WatermarkOptions options)

Where WatermarkOptions is a record containing:
- string Template (e.g. "ONLY FOR {Purpose} - {Date}")
- double Opacity (range 0.10 to 0.40, clamp if out of range)
- int TileDensity (maps to horizontal spacing: Low=450, Med=350, High=250)

Requirements:
- Expand {Date} to DateTime.Now.ToString("yyyy-MM-dd")
- DO NOT expand {Machine} macro 鈥?remove it from the implementation 
  as it leaks the NetBIOS hostname (security decision)
- Use WPF DrawingContext with a fixed -35 degree rotation
- The input imageBytes buffer must be pinned and zeroed after the 
  BitmapSource is fully constructed
- Return a frozen BitmapSource so it is safe to pass across threads
```

**Prompt 7 [x] 鈥?Export service**
```
In SafeSeal.Core, create ExportService.cs with:

void ExportAsJpeg(BitmapSource image, string outputPath, int quality)
void ExportAsPng(BitmapSource image, string outputPath)

Requirements:
- JPEG quality range 70-95, clamp and log a warning if out of range
- Never write to a temp file 鈥?encode directly to a FileStream
- Call GC.Collect() + GC.WaitForPendingFinalizers() after export 
  to prompt cleanup of any residual bitmap memory
- Throw ArgumentException for null inputs with descriptive messages
```

---

## Phase 3 鈥?WPF Shell

**Prompt 8 [x] 鈥?Main window and MVVM wiring**
```
Read ./design/UI.md for layout requirements.

In SafeSeal.App, implement the main WPF shell using MVVM pattern 
(no code-behind logic, only event routing):

1. MainWindow.xaml 鈥?outer shell with three regions:
   - Left panel: vault ListView showing Name, Date Added, File Size
   - Center: live watermark preview (Image control bound to ViewModel)
   - Right panel: watermark controls (Template selector, Opacity slider, 
     Density radio buttons, Export button)

2. MainViewModel.cs:
   - ObservableCollection<VaultItemViewModel> VaultItems
   - Commands: AddItemCommand, DeleteItemCommand, RenameItemCommand, 
     ExportCommand
   - SelectedItem property that triggers watermark preview refresh

3. Use CommunityToolkit.Mvvm NuGet package for 
   [RelayCommand] and [ObservableProperty] source generators

4. All VaultManager and WatermarkRenderer calls must be 
   dispatched on a background Task 鈥?never block the UI thread
```

**Prompt 9 [x] 鈥?Error handling and user messaging**
```
In SafeSeal.App, create a UserFacingErrorHandler.cs service that 
maps internal exceptions to the safe messages defined in 
./design/project_doc.md section 5.2:

- CryptographicException 鈫?
  "This file is locked to another user or device. 
   Security policy prevents access."
- InvalidDataException 鈫?
  "This file is not a valid SafeSeal vault item."
- NotSupportedException 鈫?
  "This vault item was created with a newer version of SafeSeal 
   and cannot be opened."
- Any other Exception 鈫?
  "An unexpected error occurred. No sensitive data was written to disk."

Show errors via a modal MessageBox. Never surface stack traces, 
inner exception messages, or type names to the user.
```

---

## Phase 4 鈥?Build & Publish

**Prompt 10 [x] 鈥?Final build configuration**
```
Add a publish profile to SafeSeal.App for a self-contained 
single-file executable targeting win-x64 with these settings:

- PublishSingleFile: true
- SelfContained: true  
- RuntimeIdentifier: win-x64
- PublishReadyToRun: true   鈫?default optimized build
- EnableCompressionInSingleFile: true
- NativeAOT: false          鈫?intentionally disabled, 
                               WPF AOT support is not production-ready

Also create a build.ps1 PowerShell script in the repo root that 
runs dotnet publish with the above profile and copies the output 
.exe to a ./release folder, printing the final file size.
```

---

## Tips for Working with Codex CLI

A few practices that will save you debugging time:

- **One prompt per session ideally.** Don't chain Prompt 3 and 4 together 鈥?let Codex complete and compile each step before moving forward.
- **Always verify before proceeding.** After Prompts 4 and 5, run `dotnet test` before issuing Prompt 6. Crypto bugs caught early are far cheaper than crypto bugs caught in Phase 3.
- **Reference the doc explicitly.** The `Read ./design/project_doc.md` prefix on each prompt keeps Codex grounded in your spec rather than making assumptions.
- **If Codex drifts**, add this line to any prompt: *"Do not introduce any patterns, libraries, or design decisions not present in the referenced documents without explaining the reason."*


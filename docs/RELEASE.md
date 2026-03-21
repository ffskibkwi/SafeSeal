# SafeSeal Release Guide

This repository keeps **source code** and **release artifacts** separated:
- Source code stays in Git commits/tags.
- Compiled binaries are attached to GitHub Releases as assets (`.zip`, `.sha256`).
- No binaries should be committed to the repository.

## 1. Versioning policy

`Directory.Build.props` is the single version source:
- `Version`
- `AssemblyVersion`
- `FileVersion`
- `InformationalVersion`

Update this file before creating a release tag.

## 2. Create a release from GitHub (recommended)

1. Ensure `main` is green locally:
   - `dotnet build .\SafeSeal.App\SafeSeal.App.csproj -c Release`
   - `dotnet test .\SafeSeal.Tests\SafeSeal.Tests.csproj -c Release`
2. Commit and push your changes.
3. Create and push a tag (example: `v2.0.1`):
   - `git tag v2.0.1`
   - `git push origin v2.0.1`
4. GitHub Actions workflow `Release` runs automatically.
5. Open GitHub `Releases` page:
   - Verify assets:
     - `SafeSeal-v2.0.1-win-x64.zip`
     - `SafeSeal-v2.0.1-win-x64.zip.sha256`
   - Publish/edit notes if needed.

## 3. Manual trigger (without tag)

You can run the same workflow via **Actions -> Release -> Run workflow**.
This produces a `manual-<run_number>` package for verification.

## 4. Separation checklist

- `bin/`, `obj/`, `artifacts/`, `publish/` are ignored by `.gitignore`.
- Only source and config files are committed.
- End users download binaries from GitHub Releases assets, not from repository source tree.
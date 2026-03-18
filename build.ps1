$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "SafeSeal.App\SafeSeal.App.csproj"
$publishProfile = "win-x64-singlefile"
$publishOutput = Join-Path $PSScriptRoot "SafeSeal.App\bin\Release\net9.0-windows\win-x64\publish"
$releaseDir = Join-Path $PSScriptRoot "release"

Write-Host "Publishing SafeSeal.App with profile '$publishProfile'..."
dotnet publish $projectPath -c Release -p:PublishProfile=$publishProfile

if (!(Test-Path $releaseDir)) {
    New-Item -Path $releaseDir -ItemType Directory | Out-Null
}

$exe = Get-ChildItem -Path $publishOutput -Filter "*.exe" -File |
    Sort-Object Length -Descending |
    Select-Object -First 1

if ($null -eq $exe) {
    throw "No executable found in publish output: $publishOutput"
}

$destPath = Join-Path $releaseDir $exe.Name
Copy-Item -Path $exe.FullName -Destination $destPath -Force

$sizeMb = [Math]::Round(((Get-Item $destPath).Length / 1MB), 2)
Write-Host "Release executable: $destPath"
Write-Host "Final file size: $sizeMb MB"

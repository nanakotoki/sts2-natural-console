# Pack the NaturalConsole mod into a distributable zip.
# Usage: run from the repo root ->  .\pack.ps1
$ErrorActionPreference = "Stop"

$modName = "NaturalConsole"
$outDir  = "release"

# Refresh PATH so dotnet is found even in a freshly-opened shell.
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

Write-Host "[1/4] Building..." -ForegroundColor Cyan
dotnet build "$modName.csproj" -c Debug
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "[2/4] Reading manifest..." -ForegroundColor Cyan
$manifest = Get-Content "$modName.json" -Raw | ConvertFrom-Json
$version  = ($manifest.version -replace '^v', '')

$stage = Join-Path $outDir $modName
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "[3/4] Staging files..." -ForegroundColor Cyan
Copy-Item ".\.godot\mono\temp\bin\Debug\$modName.dll" $stage -Force
Copy-Item "$modName.json" $stage -Force

Write-Host "[4/4] Zipping..." -ForegroundColor Cyan
$zip = Join-Path $outDir "$modName-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip

Write-Host ""
Write-Host "Done: $zip" -ForegroundColor Green
Write-Host "Users extract this into 'Slay the Spire 2/mods/' (resulting in mods/$modName/*),"
Write-Host "and must also have BaseLib installed."

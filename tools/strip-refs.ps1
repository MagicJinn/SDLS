# Regenerate stripped reference assemblies in dependencies/ from real game DLLs.
# Requires: .NET SDK, dotnet tool restore (JetBrains.Refasmer.CliTool)
#
# Usage:
#   .\tools\strip-refs.ps1
#   .\tools\strip-refs.ps1 -GameManagedPath "D:\Steam\steamapps\common\Sunless Sea\Sunless Sea_Data\Managed"

param(
    [string]$GameManagedPath = "",
    [string]$SourceDir = "dependencies/source",
    [string]$OutputDir = "dependencies",
    [string]$TempDir = "dependencies/refs-temp"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$RequiredDlls = @(
    "JsonFx.dll",
    "Sunless.Game.dll",
    "Failbetter.Core.dll",
    "UnityEngine.UI.dll",
    "Ionic.Zip.dll"
)

function Copy-GameDlls {
    param([string]$ManagedPath)

    if (-not (Test-Path $ManagedPath)) {
        throw "Game Managed folder not found: $ManagedPath"
    }

    New-Item -ItemType Directory -Path $SourceDir -Force | Out-Null

    foreach ($dll in $RequiredDlls) {
        $sourcePath = Join-Path $ManagedPath $dll
        if (-not (Test-Path $sourcePath)) {
            throw "Missing required DLL in game install: $sourcePath"
        }
        Copy-Item $sourcePath (Join-Path $SourceDir $dll) -Force
        Write-Host "Copied $dll from game install" -ForegroundColor Green
    }
}

function Test-SourceDllsPresent {
    foreach ($dll in $RequiredDlls) {
        if (-not (Test-Path (Join-Path $SourceDir $dll))) {
            return $false
        }
    }
    return $true
}

Write-Host "SDLS reference assembly generator" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan

if ($GameManagedPath) {
    Copy-GameDlls -ManagedPath $GameManagedPath
}
elseif (-not (Test-SourceDllsPresent)) {
    Write-Host ""
    Write-Host "No source DLLs found in $SourceDir." -ForegroundColor Yellow
    Write-Host "Copy the five required DLLs from your Sunless Sea install, or rerun with:" -ForegroundColor Yellow
    Write-Host '  .\tools\strip-refs.ps1 -GameManagedPath "...\Sunless Sea_Data\Managed"' -ForegroundColor Yellow
    exit 1
}

Write-Host "Restoring local dotnet tools..." -ForegroundColor White
dotnet tool restore | Out-Null

if (Test-Path $TempDir) {
    Remove-Item $TempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

$inputDlls = $RequiredDlls | ForEach-Object { Join-Path $SourceDir $_ }
Write-Host "Stripping IL from source assemblies..." -ForegroundColor White
dotnet refasmer -v -O $TempDir -c --all @inputDlls

foreach ($dll in $RequiredDlls) {
    $outputPath = Join-Path $TempDir $dll
    if (-not (Test-Path $outputPath)) {
        throw "Refasmer did not produce $dll"
    }
}

Write-Host ""
Write-Host "Size comparison:" -ForegroundColor Cyan
foreach ($dll in $RequiredDlls) {
    $sourceSize = (Get-Item (Join-Path $SourceDir $dll)).Length
    $refSize = (Get-Item (Join-Path $TempDir $dll)).Length
    $ratio = [math]::Round(100 * $refSize / $sourceSize, 1)
    Write-Host ("  {0,-22} {1,12:N0} -> {2,10:N0} ({3}%)" -f $dll, $sourceSize, $refSize, $ratio)
}

foreach ($dll in $RequiredDlls) {
    Copy-Item (Join-Path $TempDir $dll) (Join-Path $OutputDir $dll) -Force
}

Remove-Item $TempDir -Recurse -Force

Write-Host ""
Write-Host "Reference assemblies written to $OutputDir" -ForegroundColor Green
Write-Host "Run 'dotnet build' to verify compilation." -ForegroundColor Green

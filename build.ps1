# SDLS Build Script
# Creates two distribution packages: Full and Plugin Only

param(
    [string]$SevenZipPath = ""
)

# Function to find 7-Zip installation
function Find-SevenZip {
    $commonPaths = @(
        "${env:ProgramFiles}\7-Zip\7z.exe",
        "${env:ProgramFiles(x86)}\7-Zip\7z.exe",
        "${env:ProgramW6432}\7-Zip\7z.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    # Try to find in PATH
    try {
        $pathResult = Get-Command "7z.exe" -ErrorAction Stop
        return $pathResult.Source
    }
    catch {
        return $null
    }
}

# Function to create zip using .NET
function New-ZipArchive {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )
    
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    
    if (Test-Path $DestinationPath) {
        Remove-Item $DestinationPath -Force
    }
    
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SourcePath, $DestinationPath)
    Write-Host "Created: $DestinationPath" -ForegroundColor Green
}

# Function to create 7z archive
function New-SevenZipArchive {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [string]$SevenZipExe
    )
    
    if (Test-Path $DestinationPath) {
        Remove-Item $DestinationPath -Force
    }
    
    # Change to the source directory and archive contents directly
    $currentDir = Get-Location
    $absoluteDestinationPath = [System.IO.Path]::GetFullPath($DestinationPath)
    Set-Location $SourcePath
    
    # Archive all contents in current directory (without parent folder name)
    $arguments = @("a", "-t7z", "`"$absoluteDestinationPath`"", "*")
    
    try {
        $process = Start-Process -FilePath $SevenZipExe -ArgumentList $arguments -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -eq 0) {
            Write-Host "Created: $DestinationPath" -ForegroundColor Green
        }
        else {
            Write-Host "Failed to create 7z archive: $DestinationPath (Exit code: $($process.ExitCode))" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "Error creating 7z archive: $_" -ForegroundColor Red
    }
    finally {
        # Restore original directory
        Set-Location $currentDir
    }
}

# Main script starts here
Write-Host "SDLS Build Script Starting..." -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Build the project first
Write-Host "Building project..." -ForegroundColor White
try {
    $buildProcess = Start-Process -FilePath "dotnet" -ArgumentList "build", "-c", "Release", "-p:Optimize=true" -Wait -PassThru -NoNewWindow
    if ($buildProcess.ExitCode -eq 0) {
        Write-Host "Project built successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Build failed with exit code: $($buildProcess.ExitCode)" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Error running dotnet build: $_" -ForegroundColor Red
    Write-Host "Make sure .NET SDK is installed and accessible from PATH" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Check if required files exist
$dllPath = "bin\Release\net35\SDLS.dll"
$configPath = "SDLS_config.ini"
$bepInExBinPath = "BepInExBin"

if (-not (Test-Path $dllPath)) {
    Write-Host "ERROR: $dllPath not found!" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $configPath)) {
    Write-Host "ERROR: $configPath not found!" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $bepInExBinPath)) {
    Write-Host "ERROR: $bepInExBinPath folder not found!" -ForegroundColor Red
    exit 1
}

Write-Host "All required files found!" -ForegroundColor Green

# Find or ask for 7-Zip path
if ([string]::IsNullOrEmpty($SevenZipPath)) {
    $SevenZipPath = Find-SevenZip
}

if ([string]::IsNullOrEmpty($SevenZipPath)) {
    $SevenZipPath = Read-Host "Please enter the full path to 7z.exe (or press Enter to skip 7z creation)"
}

$use7Zip = $false
if (-not [string]::IsNullOrEmpty($SevenZipPath) -and (Test-Path $SevenZipPath)) {
    $use7Zip = $true
    Write-Host "Using 7-Zip: $SevenZipPath" -ForegroundColor Green
}
else {
    Write-Host "7-Zip not available - will create .zip files only" -ForegroundColor Yellow
}

# Clean up any existing build folders
$fullPackagePath = "SDLS - Full"
$pluginOnlyPath = "SDLS - Plugin Only"

if (Test-Path $fullPackagePath) {
    Remove-Item $fullPackagePath -Recurse -Force
    Write-Host "Cleaned up existing $fullPackagePath folder" -ForegroundColor Yellow
}

if (Test-Path $pluginOnlyPath) {
    Remove-Item $pluginOnlyPath -Recurse -Force
    Write-Host "Cleaned up existing $pluginOnlyPath folder" -ForegroundColor Yellow
}

Write-Host "`nCreating folder structures..." -ForegroundColor Cyan

# Create SDLS - Full package
Write-Host "Building Full Package..." -ForegroundColor White

# Create folder structure for full package
New-Item -ItemType Directory -Path "$fullPackagePath\BepInEx\plugins" -Force | Out-Null

# Copy SDLS.dll to plugins folder
Copy-Item $dllPath "$fullPackagePath\BepInEx\plugins\SDLS.dll" -Force
Write-Host "Copied SDLS.dll to Full package plugins folder" -ForegroundColor Green

# Copy config to root of full package
Copy-Item $configPath "$fullPackagePath\SDLS_config.ini" -Force
Write-Host "Copied SDLS_config.ini to Full package root" -ForegroundColor Green

# Copy all contents of BepInExBin to full package root
$bepInExItems = Get-ChildItem $bepInExBinPath -Recurse
foreach ($item in $bepInExItems) {
    $relativePath = $item.FullName.Substring((Get-Item $bepInExBinPath).FullName.Length + 1)
    $destinationPath = Join-Path $fullPackagePath $relativePath
    
    if ($item.PSIsContainer) {
        New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    }
    else {
        $destinationDir = Split-Path $destinationPath -Parent
        New-Item -ItemType Directory -Path $destinationDir -Force -ErrorAction SilentlyContinue | Out-Null
        Copy-Item $item.FullName $destinationPath -Force
    }
}
Write-Host "Copied BepInExBin contents to Full package" -ForegroundColor Green

# Create SDLS - Plugin Only package
Write-Host "`nBuilding Plugin Only Package..." -ForegroundColor White

# Create folder structure for plugin only package
New-Item -ItemType Directory -Path "$pluginOnlyPath\BepInEx\plugins" -Force | Out-Null

# Copy SDLS.dll to plugins folder
Copy-Item $dllPath "$pluginOnlyPath\BepInEx\plugins\SDLS.dll" -Force
Write-Host "Copied SDLS.dll to Plugin Only package plugins folder" -ForegroundColor Green

# Copy config to root of plugin only package
Copy-Item $configPath "$pluginOnlyPath\SDLS_config.ini" -Force
Write-Host "Copied SDLS_config.ini to Plugin Only package root" -ForegroundColor Green

Write-Host "`nCreating archives..." -ForegroundColor Cyan

# Create archives folder
$archivesFolder = "Archives"
if (Test-Path $archivesFolder) {
    Remove-Item $archivesFolder -Recurse -Force
    Write-Host "Cleaned up existing Archives folder" -ForegroundColor Yellow
}
New-Item -ItemType Directory -Path $archivesFolder -Force | Out-Null

# Create ZIP files
Write-Host "Creating ZIP archives..." -ForegroundColor White
New-ZipArchive -SourcePath $fullPackagePath -DestinationPath "$archivesFolder\SDLS - Full.zip"
New-ZipArchive -SourcePath $pluginOnlyPath -DestinationPath "$archivesFolder\SDLS - Plugin Only.zip"

# Create 7Z files if 7-Zip is available
if ($use7Zip) {
    Write-Host "Creating 7-Zip archives..." -ForegroundColor White
    New-SevenZipArchive -SourcePath $fullPackagePath -DestinationPath "$archivesFolder\SDLS - Full.7z" -SevenZipExe $SevenZipPath
    New-SevenZipArchive -SourcePath $pluginOnlyPath -DestinationPath "$archivesFolder\SDLS - Plugin Only.7z" -SevenZipExe $SevenZipPath
}
else {
    Write-Host "Skipping 7-Zip archives (7-Zip not available)" -ForegroundColor Yellow
}

# Clean up temporary package folders
Write-Host "`nCleaning up temporary folders..." -ForegroundColor White
Remove-Item $fullPackagePath -Recurse -Force
Remove-Item $pluginOnlyPath -Recurse -Force
Write-Host "Cleaned up package folders" -ForegroundColor Green

Write-Host "`n================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan

# List created files
Write-Host "`nCreated archives in '$archivesFolder' folder:" -ForegroundColor White
Get-ChildItem $archivesFolder | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }

$archiveCount = (Get-ChildItem $archivesFolder).Count
Write-Host "`nTotal archives created: $archiveCount" -ForegroundColor Cyan

# ============================================================
#  OOFManagerX Installer
#  Installs the certificate and MSIX package in one step.
# ============================================================

$ErrorActionPreference = "Stop"

# Ensure running as admin
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host ""
    Write-Host "  OOFManagerX requires administrator privileges to install." -ForegroundColor Yellow
    Write-Host "  Restarting as administrator..." -ForegroundColor Yellow
    Start-Process powershell.exe "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$certFile = Get-ChildItem $scriptDir -Filter "*.cer" | Select-Object -First 1
$msixFile = Get-ChildItem $scriptDir -Filter "*.msix" | Select-Object -First 1

# Extract version from MSIX filename
$version = if ($msixFile.Name -match 'v(\d+\.\d+\.\d+)') { $Matches[1] } else { "latest" }

Write-Host ""
Write-Host "  ==============================================" -ForegroundColor Cyan
Write-Host "    OOFManagerX v$version - Installer" -ForegroundColor Cyan
Write-Host "    Automatic Out-of-Office Manager for M365" -ForegroundColor Cyan
Write-Host "  ==============================================" -ForegroundColor Cyan
Write-Host ""

# Verify files exist
if (-not $certFile) { Write-Host "  ERROR: No .cer certificate file found in $scriptDir" -ForegroundColor Red; pause; exit 1 }
if (-not $msixFile) { Write-Host "  ERROR: No .msix package file found in $scriptDir" -ForegroundColor Red; pause; exit 1 }

# Step 1: Install certificate
Write-Host "  [1/2] Installing certificate..." -ForegroundColor White
try {
    certutil -addstore TrustedPeople $certFile.FullName | Out-Null
    Write-Host "        Certificate installed successfully." -ForegroundColor Green
} catch {
    Write-Host "        Failed to install certificate: $_" -ForegroundColor Red
    pause; exit 1
}

# Step 2: Install MSIX
Write-Host "  [2/2] Installing OOFManagerX..." -ForegroundColor White
try {
    Add-AppxPackage -Path $msixFile.FullName
    Write-Host "        OOFManagerX installed successfully!" -ForegroundColor Green
} catch {
    Write-Host "        Failed to install package: $_" -ForegroundColor Red
    pause; exit 1
}

Write-Host ""
Write-Host "  Installation complete!" -ForegroundColor Green
Write-Host "  You can find OOFManagerX in your Start Menu." -ForegroundColor White
Write-Host ""
pause

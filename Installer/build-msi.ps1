#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Drive Space Checker MSI installer.

.DESCRIPTION
    Publishes the WPF application as a self-contained single-file win-x64 executable,
    packages it with WiX Toolset 5, and copies the finished MSI to .\artifacts.

.PARAMETER Version
    Product version written into the MSI. Defaults to 1.0.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipPublish
    Reuse the existing publish output in the temp folder instead of running dotnet publish.

.PARAMETER SkipVerify
    Skip the administrative-install verification step.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Installer\build-msi.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Installer\build-msi.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipPublish,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'DriveSpaceChecker.csproj'
$icon = Join-Path $repoRoot 'Assets\app.ico'
$tempRoot = Join-Path $env:TEMP 'dsc-msi'
$publishDir = Join-Path $tempRoot 'publish'
$msiPath = Join-Path $tempRoot "DriveSpaceChecker-Setup-$Version.msi"
$artifactsDir = Join-Path $repoRoot 'artifacts'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Write-Step "Preparing folders"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

if (-not $SkipPublish) {
    Write-Step "Publishing self-contained win-x64 build"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $publishArgs = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:BaseOutputPath=$tempRoot\bin\",
        "-p:BaseIntermediateOutputPath=$tempRoot\obj\",
        "-p:PublishDir=$publishDir\"
    )
    Invoke-Native 'dotnet' $publishArgs
}

$exePath = Join-Path $publishDir 'DriveSpaceChecker.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish output not found at $exePath. Run without -SkipPublish first."
}

Write-Step "Checking WiX extensions"
$installed = (& wix extension list -g 2>$null) -join "`n"
foreach ($extension in @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')) {
    if ($installed -notmatch [regex]::Escape($extension)) {
        Write-Host "Installing $extension..."
        Invoke-Native 'wix' @('extension', 'add', '-g', "$extension/5.0.2")
    }
}

Write-Step "Building MSI (version $Version)"
if (Test-Path $msiPath) {
    Remove-Item $msiPath -Force
}

$buildArgs = @(
    'build', (Join-Path $PSScriptRoot 'Product.wxs'),
    '-arch', 'x64',
    '-ext', 'WixToolset.UI.wixext',
    '-ext', 'WixToolset.Util.wixext',
    '-d', "ProductVersion=$Version",
    '-d', "PublishDir=$publishDir",
    '-d', "IconPath=$icon",
    '-o', $msiPath
)
Invoke-Native 'wix' $buildArgs

if (-not (Test-Path $msiPath)) {
    throw "WiX did not produce $msiPath."
}

if (-not $SkipVerify) {
    Write-Step "Verifying MSI by administrative install"
    $verifyDir = Join-Path $tempRoot 'verify'
    if (Test-Path $verifyDir) {
        Remove-Item $verifyDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $verifyDir | Out-Null

    $process = Start-Process -FilePath 'msiexec.exe' -Wait -PassThru -ArgumentList @(
        '/a', "`"$msiPath`"",
        '/qn',
        "TARGETDIR=`"$verifyDir`""
    )
    if ($process.ExitCode -ne 0) {
        throw "Administrative install failed with exit code $($process.ExitCode)."
    }

    $extractedExe = Get-ChildItem $verifyDir -Recurse -Filter 'DriveSpaceChecker.exe' -File |
        Select-Object -First 1
    if (-not $extractedExe) {
        throw "Administrative install did not extract DriveSpaceChecker.exe."
    }

    $fileVersion = $extractedExe.VersionInfo.FileVersion
    Write-Host "Extracted: $($extractedExe.FullName)"
    Write-Host "File version: $fileVersion"
}

Write-Step "Copying MSI to artifacts"
$finalMsi = Join-Path $artifactsDir "DriveSpaceChecker-Setup-$Version.msi"
Copy-Item $msiPath $finalMsi -Force

$hash = (Get-FileHash $finalMsi -Algorithm SHA256).Hash
$size = (Get-Item $finalMsi).Length / 1MB

Write-Host ''
Write-Host "MSI ready: $finalMsi" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB" -f $size)
Write-Host "SHA256: $hash"

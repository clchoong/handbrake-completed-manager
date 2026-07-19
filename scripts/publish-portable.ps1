[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [ValidatePattern("^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$")]
    [string]$Version = "0.1.0",

    [string]$ArtifactsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repositoryRoot "artifacts"
}

$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$publishRoot = Join-Path $artifactsRoot (Join-Path "portable" $RuntimeIdentifier)
$workingRoot = Join-Path $publishRoot "working"
$appPublishDirectory = Join-Path $workingRoot "app"
$receiverPublishDirectory = Join-Path $workingRoot "receiver"
$packageDirectory = Join-Path $publishRoot "HandBrake Completed Manager"
$zipPath = Join-Path $publishRoot "HandBrake-Completed-Manager-$Version-$RuntimeIdentifier.zip"

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $appPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $receiverPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$commonPublishArguments = @(
    "--configuration", "Release",
    "--runtime", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishTrimmed=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:IncludeSourceRevisionInInformationalVersion=false",
    "-p:Version=$Version"
)

$appProject = Join-Path $repositoryRoot "desktop\src\HandBrakeCompletedManager.App\HandBrakeCompletedManager.App.csproj"
$receiverProject = Join-Path $repositoryRoot "desktop\src\HandBrakeCompletedManager.Receiver\HandBrakeCompletedManager.Receiver.csproj"

& dotnet publish $appProject @commonPublishArguments --output $appPublishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Desktop application publishing failed with exit code $LASTEXITCODE."
}

& dotnet publish $receiverProject @commonPublishArguments --output $receiverPublishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Completion receiver publishing failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $appPublishDirectory "HandBrakeCompletedManager.exe") -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $receiverPublishDirectory "HandBrakeCompletedManager.Receiver.exe") -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging\portable.mode") -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging\PORTABLE-README.txt") -Destination $packageDirectory

& (Join-Path $PSScriptRoot "verify-portable-package.ps1") -PackageDirectory $packageDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Portable package verification failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $workingRoot -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $packageDirectory,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

Write-Host "Portable package verified successfully."
Write-Host "Folder: $packageDirectory"
Write-Host "Archive: $zipPath"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"

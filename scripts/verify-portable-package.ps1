[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$appPath = Join-Path $packageRoot "HandBrakeCompletedManager.exe"
$receiverPath = Join-Path $packageRoot "HandBrakeCompletedManager.Receiver.exe"
$markerPath = Join-Path $packageRoot "portable.mode"
$projectLicensePath = Join-Path $packageRoot "LICENSE.txt"
$thirdPartyNoticesPath = Join-Path $packageRoot "THIRD-PARTY-NOTICES.txt"
$dotnetNoticesPath = Join-Path $packageRoot "DOTNET-THIRD-PARTY-NOTICES.txt"
$dataDirectory = Join-Path $packageRoot "data"
$smokeDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("hbcm-portable-smoke-" + [Guid]::NewGuid().ToString("N"))
$appProcess = $null
$ownsDataDirectory = $false
$originalSmokeInstanceId = $env:HBCM_SMOKE_INSTANCE_ID

foreach ($requiredPath in @(
    $appPath,
    $receiverPath,
    $markerPath,
    $projectLicensePath,
    $thirdPartyNoticesPath,
    $dotnetNoticesPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required package file is missing: $requiredPath"
    }
}

if (-not (Select-String -LiteralPath $projectLicensePath -SimpleMatch "MIT License" -Quiet)) {
    throw "The package project licence is not the expected MIT licence."
}

foreach ($requiredNotice in @("Microsoft.Data.Sqlite", "SQLitePCLRaw", "Apache License")) {
    if (-not (Select-String -LiteralPath $thirdPartyNoticesPath -SimpleMatch $requiredNotice -Quiet)) {
        throw "The package third-party notices are incomplete: missing $requiredNotice."
    }
}

if ((Get-Item -LiteralPath $dotnetNoticesPath).Length -lt 10KB) {
    throw "The packaged .NET third-party notice file appears incomplete."
}

foreach ($executablePath in @($appPath, $receiverPath)) {
    if ((Get-Item -LiteralPath $executablePath).Length -lt 1MB) {
        throw "Published executable does not appear to be self-contained: $executablePath"
    }
}

$looseRuntimeFiles = Get-ChildItem -LiteralPath $packageRoot -File | Where-Object {
    $_.Extension -eq ".dll" -or
    $_.Name.EndsWith(".deps.json", [StringComparison]::OrdinalIgnoreCase) -or
    $_.Name.EndsWith(".runtimeconfig.json", [StringComparison]::OrdinalIgnoreCase)
}
if ($looseRuntimeFiles) {
    throw "Portable package contains loose runtime files instead of the expected single-file executables."
}

if (Test-Path -LiteralPath $dataDirectory) {
    throw "Package verification requires a clean package without an existing data directory. Refusing to modify possible user data."
}

try {
    New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null
    $sourcePath = Join-Path $smokeDirectory "source sample.mkv"
    $destinationPath = Join-Path $smokeDirectory "converted sample.mp4"
    [System.IO.File]::WriteAllBytes($sourcePath, [byte[]](1..128))
    [System.IO.File]::WriteAllBytes($destinationPath, [byte[]](1..64))

    $ownsDataDirectory = $true
    & $receiverPath --source $sourcePath --destination $destinationPath --exit-code 0
    if ($LASTEXITCODE -ne 0) {
        throw "Portable receiver smoke test failed with exit code $LASTEXITCODE."
    }

    $databasePath = Join-Path $dataDirectory "history.db"
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw "The receiver did not create data\history.db beside the portable executables."
    }

    $settingsPath = Join-Path $dataDirectory "settings.json"
    @{
        StartMinimized = $true
        CloseToTray = $false
        NotificationsEnabled = $false
        HistoryRefreshSeconds = 3
    } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    $env:HBCM_SMOKE_INSTANCE_ID = [Guid]::NewGuid().ToString("N")
    $appProcess = Start-Process -FilePath $appPath -WorkingDirectory $packageRoot -WindowStyle Hidden -PassThru
    $desktopLogFound = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $appProcess.Refresh()
        if ($appProcess.HasExited) {
            throw "Portable desktop application exited unexpectedly with code $($appProcess.ExitCode)."
        }

        $logsDirectory = Join-Path $dataDirectory "logs"
        $desktopLogFound = [bool](Get-ChildItem -LiteralPath $logsDirectory -Filter "*.log" -File -ErrorAction SilentlyContinue |
            Select-String -SimpleMatch "[Desktop] Desktop application started." -Quiet)
        if ($desktopLogFound) {
            break
        }
    }

    if (-not $desktopLogFound) {
        throw "Portable desktop application did not write its startup diagnostic log."
    }
}
finally {
    $env:HBCM_SMOKE_INSTANCE_ID = $originalSmokeInstanceId

    if ($null -ne $appProcess) {
        $appProcess.Refresh()
        if (-not $appProcess.HasExited) {
            Stop-Process -Id $appProcess.Id -Force
            $appProcess.WaitForExit()
        }
    }

    if ($ownsDataDirectory -and (Test-Path -LiteralPath $dataDirectory)) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $smokeDirectory) {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}

Write-Host "Portable receiver, shared storage, and desktop startup smoke tests passed."

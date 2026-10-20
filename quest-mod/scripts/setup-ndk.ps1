$ErrorActionPreference = 'Stop'

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ndkPathFile = Join-Path $workspaceRoot 'ndkpath.txt'
$ndkPath = $null

function Test-QuestNdk([string] $candidate) {
    $propertiesPath = Join-Path $candidate 'source.properties'
    if (-not (Test-Path -LiteralPath (Join-Path $candidate 'build/cmake/android.toolchain.cmake')) -or
        -not (Test-Path -LiteralPath $propertiesPath)) {
        return $false
    }

    $properties = Get-Content -LiteralPath $propertiesPath -Raw
    return $properties -match '(?m)^Pkg\.Revision\s*=\s*27\.'
}

if (Test-Path -LiteralPath $ndkPathFile) {
    $candidate = (Get-Content -LiteralPath $ndkPathFile -Raw).Trim()
    if (Test-QuestNdk $candidate) {
        $ndkPath = $candidate
    }
}

if (-not $ndkPath) {
    foreach ($environmentVariable in @('ANDROID_NDK_HOME', 'ANDROID_NDK_LATEST_HOME')) {
        $candidate = [Environment]::GetEnvironmentVariable($environmentVariable)
        if ($candidate -and (Test-QuestNdk $candidate)) {
            $ndkPath = $candidate
            break
        }
    }
}

if (-not $ndkPath) {
    Write-Host 'No valid NDK path found. Asking QPM to download the workspace NDK...'
    & qpm ndk resolve --download
    if ($LASTEXITCODE -ne 0) {
        throw "QPM could not install the NDK (exit code $LASTEXITCODE)."
    }

    $qpmNdkRoot = Join-Path $env:APPDATA 'QPM-RS/ndk'
    $ndkPath = Get-ChildItem -LiteralPath $qpmNdkRoot -Directory |
        Where-Object {
            $_.Name -match '^27\.' -and
            (Test-QuestNdk $_.FullName)
        } |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $ndkPath) {
        throw "QPM completed but no installed NDK 27.x was found under '$qpmNdkRoot'. Run 'qpm ndk list' to inspect installed versions."
    }

    [System.IO.File]::WriteAllText($ndkPathFile, $ndkPath)
}

Write-Host "Using Android NDK: $ndkPath"

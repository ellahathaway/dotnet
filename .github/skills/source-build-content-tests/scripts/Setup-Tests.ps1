<#
.SYNOPSIS
    Dot-source this script to set up the Docker environment and import shared helpers
    for running source-build validation tests.

.DESCRIPTION
    When dot-sourced (. .\Setup-Tests.ps1), this script:
    1. Starts Docker Desktop if it is not already running
    2. Pulls the test container image
    3. Resolves the VMR root path
    4. Exports helper functions used by Run-LicenseScanTests.ps1 and Run-SdkContentTests.ps1

    All exported state is prefixed with $SBTest_ to avoid collisions.

.PARAMETER ContainerImage
    Docker image to use for running tests. Defaults to the CI image.

.PARAMETER VmrPath
    Path to the VMR root. Defaults to the git repo root of the skill directory.

.PARAMETER ResultsDir
    Where to collect test results. Defaults to <VmrPath>/artifacts/sb-test-results.

.PARAMETER DockerStartTimeoutSeconds
    How long to wait for Docker Desktop to start. Defaults to 120 seconds.

.EXAMPLE
    . .\Setup-Tests.ps1
    . .\Setup-Tests.ps1 -VmrPath C:\repos\dotnet -ContainerImage my-image:latest
#>

param(
    [Parameter()]
    [string]$ContainerImage = 'mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net11.0-source-build-test-amd64',

    [Parameter()]
    [string]$VmrPath,

    [Parameter()]
    [string]$ResultsDir,

    [Parameter()]
    [int]$DockerStartTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

#region Resolve paths

if (-not $VmrPath) {
    $VmrPath = & git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
    if (-not $VmrPath -or $LASTEXITCODE -ne 0) {
        throw "Could not determine VMR root. Pass -VmrPath explicitly."
    }
    $VmrPath = (Resolve-Path $VmrPath).Path
}

if (-not (Test-Path (Join-Path $VmrPath 'build.proj'))) {
    throw "VmrPath '$VmrPath' does not appear to be the VMR root (build.proj not found)."
}

if (-not $ResultsDir) {
    $ResultsDir = Join-Path $VmrPath 'artifacts' 'sb-test-results'
}

# Export resolved values for run scripts
$script:SBTest_VmrPath        = $VmrPath
$script:SBTest_ResultsDir     = $ResultsDir
$script:SBTest_ContainerImage = $ContainerImage

# Repos whose src/ subdirectories CI scans as separate jobs
$script:SBTest_SplitRepos = @('source-build-assets')

#endregion

#region Helpers (exported by dot-sourcing)

function SBTest-WriteStep {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function SBTest-WriteResult {
    param([string]$Message, [switch]$IsError)
    if ($IsError) {
        Write-Host "  [FAIL] $Message" -ForegroundColor Red
    } else {
        Write-Host "  [OK]   $Message" -ForegroundColor Green
    }
}

function SBTest-ConvertToDockerPath {
    <# Convert a Windows path to Docker-compatible /c/... format. #>
    param([string]$WindowsPath)
    $p = $WindowsPath -replace '\\', '/'
    if ($p -match '^([A-Za-z]):(.*)$') {
        return "/$($Matches[1].ToLower())$($Matches[2])"
    }
    return $p
}

function SBTest-InvokeDockerTest {
    <#
    .SYNOPSIS
        Run a test command inside the source-build test container with the VMR bind-mounted.
    .PARAMETER ScanName
        Human-readable label for this test run.
    .PARAMETER TestCommand
        The bash command to execute inside the container.
    #>
    param(
        [Parameter(Mandatory)][string]$ScanName,
        [Parameter(Mandatory)][string]$TestCommand
    )

    $vmrDockerPath = SBTest-ConvertToDockerPath $script:SBTest_VmrPath

    SBTest-WriteStep "Running test: $ScanName"
    Write-Host "  Container: $script:SBTest_ContainerImage"
    Write-Host "  VMR mount: $vmrDockerPath -> /vmr"

    $dockerArgs = @(
        'run', '--rm',
        '--memory=12g'
    )

    # Force x86_64 emulation when running on ARM64 hosts
    $hostArch = docker info --format '{{.Architecture}}' 2>$null
    if ($hostArch -and $hostArch -match 'aarch64|arm64') {
        $dockerArgs += '--platform'
        $dockerArgs += 'linux/amd64'
        Write-Host "  Platform override: linux/amd64 (host is $hostArch)" -ForegroundColor Yellow
    }

    $dockerArgs += @(
        '-v', "${vmrDockerPath}:/vmr",
        '-w', '/vmr',
        $script:SBTest_ContainerImage,
        'bash', '-c', $TestCommand
    )

    Write-Host "  Command: docker $($dockerArgs -join ' ')" -ForegroundColor DarkGray
    & docker @dockerArgs
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        SBTest-WriteResult "Test '$ScanName' passed"
    } else {
        SBTest-WriteResult "Test '$ScanName' failed (exit code: $exitCode)" -IsError
    }

    return $exitCode
}

function SBTest-CollectResults {
    <#
    .SYNOPSIS
        Copy test result files (updated baselines, binlogs, logs, trx) from the VMR
        artifacts tree into the results directory.
    #>
    param([string]$Label = 'Tests')

    SBTest-WriteStep "Collecting results to: $script:SBTest_ResultsDir"

    if (-not (Test-Path $script:SBTest_ResultsDir)) {
        New-Item -ItemType Directory -Path $script:SBTest_ResultsDir -Force | Out-Null
    }

    $artifactsBase  = Join-Path $script:SBTest_VmrPath 'artifacts'
    $testResultsDir = Join-Path $artifactsBase 'TestResults'
    $logDir         = Join-Path $artifactsBase 'log'
    $collected      = 0

    # Collect updated baselines, scan results, and trx files
    if (Test-Path $testResultsDir) {
        $files = Get-ChildItem -Path $testResultsDir -Recurse -File |
            Where-Object { $_.Name -like 'Updated*' -or $_.Name -like 'scancode-results*' -or $_.Extension -eq '.trx' }

        foreach ($f in $files) {
            $relDir = $f.Directory.FullName.Replace($testResultsDir, '').TrimStart('\', '/')
            $dest = Join-Path $script:SBTest_ResultsDir 'TestResults' $relDir
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
            Copy-Item $f.FullName -Destination $dest -Force
            Write-Host "  Copied: $($f.FullName)" -ForegroundColor DarkGray
            $collected++
        }
    }

    # Collect build logs and binlogs
    if (Test-Path $logDir) {
        $files = Get-ChildItem -Path $logDir -Recurse -File |
            Where-Object { $_.Name -like 'BuildTests*' }

        foreach ($f in $files) {
            $relDir = $f.Directory.FullName.Replace($logDir, '').TrimStart('\', '/')
            $dest = Join-Path $script:SBTest_ResultsDir 'log' $relDir
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
            Copy-Item $f.FullName -Destination $dest -Force
            Write-Host "  Copied: $($f.FullName)" -ForegroundColor DarkGray
            $collected++
        }
    }

    if ($collected -eq 0) {
        Write-Host "  No result files found to collect."
    } else {
        SBTest-WriteResult "$collected file(s) collected"
    }

    # Highlight updated baselines
    $updated = Get-ChildItem -Path $script:SBTest_ResultsDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'Updated*' }
    if ($updated) {
        Write-Host "`n  Updated baseline files (copy to test/Microsoft.DotNet.SourceBuild.Tests/assets/ to update baselines):" -ForegroundColor Yellow
        foreach ($f in $updated) {
            Write-Host "    - $($f.FullName)" -ForegroundColor Yellow
        }
    }
}

#endregion

#region Docker setup

SBTest-WriteStep "Checking Docker availability"

$dockerRunning = $false
try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -eq 0) { $dockerRunning = $true }
} catch { }

if ($dockerRunning) {
    SBTest-WriteResult "Docker is running"
} else {
    Write-Host "  Docker is not running. Attempting to start Docker Desktop..."
    $dockerDesktopPath = "${env:ProgramFiles}\Docker\Docker\Docker Desktop.exe"
    if (-not (Test-Path $dockerDesktopPath)) {
        $dockerDesktopPath = "${env:LOCALAPPDATA}\Docker\Docker Desktop.exe"
    }
    if (Test-Path $dockerDesktopPath) {
        Start-Process $dockerDesktopPath
    } else {
        throw "Docker Desktop not found. Please install Docker Desktop or start it manually."
    }

    $deadline = (Get-Date).AddSeconds($DockerStartTimeoutSeconds)
    $started = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        try {
            $null = docker info 2>&1
            if ($LASTEXITCODE -eq 0) { $started = $true; break }
        } catch { }
        Write-Host "  Waiting for Docker to start..."
    }

    if (-not $started) {
        throw "Docker Desktop did not start within $DockerStartTimeoutSeconds seconds."
    }
    SBTest-WriteResult "Docker is now running"
}

SBTest-WriteStep "Pulling container image (if needed)"
& docker pull $ContainerImage
if ($LASTEXITCODE -ne 0) {
    throw "Failed to pull container image: $ContainerImage"
}
SBTest-WriteResult "Image ready: $ContainerImage"

Write-Host "`n  Setup complete. VMR: $script:SBTest_VmrPath" -ForegroundColor Green
Write-Host "  Results will be collected to: $script:SBTest_ResultsDir" -ForegroundColor Green

#endregion

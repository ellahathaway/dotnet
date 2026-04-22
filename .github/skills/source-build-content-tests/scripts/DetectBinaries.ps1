<#
.SYNOPSIS
    Run source-build binary detection tests inside the test container.

.DESCRIPTION
    Runs binary detection for a repo root and then executes the BinaryScanTest test.
    Dot-sources Setup-Tests.ps1 to prepare Docker and import helpers.

.PARAMETER RepoRootPath
    Host path to the repo root to scan. Defaults to the current VMR root.
    The path must be under the VMR root so it is available in the /vmr container mount.

.PARAMETER AllowedBinariesFile
    Optional path to an allowed-binaries list file. Defaults to <RepoRootPath>/eng/allowed-vmr-binaries.txt.

.PARAMETER BinariesReportFile
    Optional path to write/read binary scan output. Defaults to <RepoRootPath>/artifacts/log/binary-report/NewBinaries.txt.

.PARAMETER ContainerImage
    Docker image override (passed through to Setup-Tests.ps1).

.PARAMETER VmrPath
    VMR root override (passed through to Setup-Tests.ps1).

.PARAMETER ResultsDir
    Results directory override (passed through to Setup-Tests.ps1).

.PARAMETER DockerStartTimeoutSeconds
    Docker start timeout override (passed through to Setup-Tests.ps1).

.EXAMPLE
    .\DetectBinaries.ps1

.EXAMPLE
    .\DetectBinaries.ps1 -RepoRootPath C:\repos\dotnet\artifacts\tmp\dotnet-source-10.0.100
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepoRootPath,

    [Parameter()]
    [string]$AllowedBinariesFile,

    [Parameter()]
    [string]$BinariesReportFile,

    [Parameter()]
    [string]$ContainerImage,

    [Parameter()]
    [string]$VmrPath,

    [Parameter()]
    [string]$ResultsDir,

    [Parameter()]
    [int]$DockerStartTimeoutSeconds
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

#region Setup

$setupParams = @{}
if ($ContainerImage)            { $setupParams['ContainerImage']            = $ContainerImage }
if ($VmrPath)                   { $setupParams['VmrPath']                   = $VmrPath }
if ($ResultsDir)                { $setupParams['ResultsDir']                = $ResultsDir }
if ($DockerStartTimeoutSeconds) { $setupParams['DockerStartTimeoutSeconds'] = $DockerStartTimeoutSeconds }

. "$PSScriptRoot\Setup-Tests.ps1" @setupParams

#endregion

#region Validate & resolve paths

if (-not $RepoRootPath) {
    $RepoRootPath = $script:SBTest_VmrPath
}

$RepoRootPath = (Resolve-Path $RepoRootPath).Path
if (-not (Test-Path (Join-Path $RepoRootPath 'build.proj'))) {
    throw "Repo root '$RepoRootPath' does not appear to be a dotnet VMR root (build.proj not found)."
}

if (-not $AllowedBinariesFile) {
    $AllowedBinariesFile = Join-Path $RepoRootPath 'eng' 'allowed-vmr-binaries.txt'
}
if (-not $BinariesReportFile) {
    $BinariesReportFile = Join-Path $RepoRootPath 'artifacts' 'log' 'binary-report' 'NewBinaries.txt'
}

$BinariesReportFile = [System.IO.Path]::GetFullPath($BinariesReportFile)

if (-not (Test-Path $AllowedBinariesFile)) {
    throw "Allowed binaries file not found: $AllowedBinariesFile"
}

function ConvertToContainerPath {
    param([string]$HostPath)

    $resolved = (Resolve-Path $HostPath).Path
    if ($resolved.StartsWith($script:SBTest_VmrPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $resolved.Substring($script:SBTest_VmrPath.Length)
        return "/vmr" + (SBTest-ConvertToDockerPath $rel)
    }

    throw "Path '$resolved' is outside the VMR ('$($script:SBTest_VmrPath)'). Place files under the VMR directory."
}

$repoRootContainer = ConvertToContainerPath $RepoRootPath
$allowedContainer = ConvertToContainerPath $AllowedBinariesFile
if (-not $BinariesReportFile.StartsWith($script:SBTest_VmrPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "BinariesReportFile '$BinariesReportFile' is outside the VMR ('$($script:SBTest_VmrPath)'). Place report output under the VMR directory."
}
$reportRel = $BinariesReportFile.Substring($script:SBTest_VmrPath.Length)
$reportContainer = "/vmr" + (SBTest-ConvertToDockerPath $reportRel)

$testsProject = "$repoRootContainer/test/Microsoft.DotNet.Tests/Microsoft.DotNet.Tests.csproj"
$detectProj = "$repoRootContainer/eng/init-detect-binaries.proj"

#endregion

#region Run

$timestamp = Get-Date -Format 'MMddHHmmss'
$binaryResultsDir = "/vmr/artifacts/TestResults/BinaryDetection"

$parts = @(
    "dotnet build $detectProj"
    "-t:DetectBinaries"
    "-c Release"
    "/p:RepoRoot=$repoRootContainer"
    "/p:AllowedBinariesFile=$allowedContainer"
    "/p:BinariesMode=Validate"
    '&&'
    "dotnet test $testsProject"
    '--filter "FullyQualifiedName=Microsoft.DotNet.Tests.BinaryScanTest.ScanForBinaries"'
    "--logger:console`;verbosity=detailed"
    "-c Release"
    "-bl:/vmr/artifacts/log/Debug/BuildTests_BinaryDetection_${timestamp}.binlog"
    "-flp:LogFile=/vmr/artifacts/log/Debug/BuildTests_BinaryDetection_${timestamp}.log"
    "-clp:v=m"
    "/p:BinariesReportFile=$reportContainer"
    "/p:RepoRoot=$repoRootContainer"
    "/p:TargetRid=linux-x64"
    "/p:PortableTargetRid=linux-x64"
    "/p:SkipPrepareSdkArchive=true"
    "--results-directory $binaryResultsDir"
)

$cmd = $parts -join ' '
$exitCode = SBTest-InvokeDockerTest -ScanName 'BinaryDetection' -TestCommand $cmd

#endregion

#region Collect & report

SBTest-CollectResults -Label 'BinaryDetection'

SBTest-WriteStep "Done"
if ($exitCode -ne 0) {
    if (Test-Path $BinariesReportFile) {
        $detectedBinaries = (Get-Content -Path $BinariesReportFile -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($detectedBinaries)) {
            Write-Host "`n  Detected binaries:" -ForegroundColor Yellow
            foreach ($line in ($detectedBinaries -split "`r?`n")) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    Write-Host "    - $($line.Trim())" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "  Binary report file exists but is empty: $BinariesReportFile" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Binary report file was not found: $BinariesReportFile" -ForegroundColor Yellow
    }

    SBTest-WriteResult "Binary detection tests failed. Check results in: $script:SBTest_ResultsDir" -IsError
} else {
    SBTest-WriteResult "Binary detection tests passed. Results in: $script:SBTest_ResultsDir"
}

exit $exitCode

#endregion

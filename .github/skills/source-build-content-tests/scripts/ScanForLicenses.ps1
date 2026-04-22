<#
.SYNOPSIS
    Run source-build license scan tests inside the test container.

.DESCRIPTION
    Scans a VMR repo (under src/) for license compliance using scancode inside the CI
    container image. Dot-sources Setup-Tests.ps1 to prepare Docker and import helpers.

    For repos that CI splits into sub-scans (e.g., source-build-assets), subdirectories
    under src/<repo>/src/ are discovered automatically and scanned as separate jobs.
    Use -NoAutoSplit to disable this and scan the root directory only.

.PARAMETER RepoName
    Name of the repo under src/ to scan (e.g., 'runtime', 'source-build-assets').

.PARAMETER ScanIgnorePatterns
    Space-separated subdirectory names to ignore during scanning.
    When -NoAutoSplit is used on a split repo, pass the subdirectory names here.

.PARAMETER NoAutoSplit
    Disables automatic sub-directory splitting. The root directory is scanned as-is.

.PARAMETER ContainerImage
    Docker image override (passed through to Setup-Tests.ps1).

.PARAMETER VmrPath
    VMR root override (passed through to Setup-Tests.ps1).

.PARAMETER ResultsDir
    Results directory override (passed through to Setup-Tests.ps1).

.PARAMETER DockerStartTimeoutSeconds
    Docker start timeout override (passed through to Setup-Tests.ps1).

.EXAMPLE
    # Scan source-build-assets root only, ignoring all src/ subdirectories
    .\Run-LicenseScanTests.ps1 -RepoName source-build-assets -NoAutoSplit `
        -ScanIgnorePatterns "externalPackages packageSourceGenerator referencePackages targetPacks textOnlyPackages"

.EXAMPLE
    # Scan source-build-assets with CI-matching auto-split
    .\Run-LicenseScanTests.ps1 -RepoName source-build-assets

.EXAMPLE
    # Scan a simple repo
    .\Run-LicenseScanTests.ps1 -RepoName runtime
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoName,

    [Parameter()]
    [string]$ScanIgnorePatterns,

    [Parameter()]
    [switch]$NoAutoSplit,

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

# Build pass-through parameters for Setup-Tests.ps1
$setupParams = @{}
if ($ContainerImage)            { $setupParams['ContainerImage']            = $ContainerImage }
if ($VmrPath)                   { $setupParams['VmrPath']                   = $VmrPath }
if ($ResultsDir)                { $setupParams['ResultsDir']                = $ResultsDir }
if ($DockerStartTimeoutSeconds) { $setupParams['DockerStartTimeoutSeconds'] = $DockerStartTimeoutSeconds }

. "$PSScriptRoot\Setup-Tests.ps1" @setupParams

#endregion

#region Validate

$repoSrcPath = Join-Path $script:SBTest_VmrPath 'src' $RepoName
if (-not (Test-Path $repoSrcPath)) {
    throw "Repository source not found: $repoSrcPath"
}

#endregion

#region Build scan jobs

function Build-LicenseScanCommand {
    param(
        [string]$ScanPath,
        [string]$ScanName,
        [string]$IgnorePatterns = ''
    )

    $timestamp = Get-Date -Format 'MMddHHmmss'
    $safeName  = $ScanName -replace '[^a-zA-Z0-9_-]', '_'

    $parts = @(
        "dotnet test"
        "/vmr/test/Microsoft.DotNet.SourceBuild.Tests/Microsoft.DotNet.SourceBuild.Tests.csproj"
        '--filter "FullyQualifiedName=Microsoft.DotNet.SourceBuild.Tests.LicenseScanTests.ScanForLicenses"'
        "--logger:console`;verbosity=detailed"
        "-c Release"
        "-bl:/vmr/artifacts/log/Debug/BuildTests_${safeName}_${timestamp}.binlog"
        "-flp:LogFile=/vmr/artifacts/log/Debug/BuildTests_${safeName}_${timestamp}.log"
        "-clp:v=m"
        "/p:SourceBuildTestsLicenseScanPath=$ScanPath"
        "/p:TargetRid=linux-x64"
        "/p:PortableTargetRid=linux-x64"
        "/p:SkipPrepareSdkArchive=true"
        "--results-directory /vmr/artifacts/TestResults/${safeName}"
    )

    if ($IgnorePatterns) {
        $parts += "/p:SourceBuildTestsLicenseScanIgnorePatterns=`"$IgnorePatterns`""
    }

    return ($parts -join ' ')
}

$isSplitRepo = $RepoName -in $script:SBTest_SplitRepos
$scanJobs = @()

if ($isSplitRepo -and -not $NoAutoSplit) {
    # Discover subdirectories dynamically (matching CI behavior)
    $srcSubDir = Join-Path $script:SBTest_VmrPath 'src' $RepoName 'src'
    $subDirs = @()
    if (Test-Path $srcSubDir) {
        $subDirs = Get-ChildItem -Path $srcSubDir -Directory | Select-Object -ExpandProperty Name
    }

    if ($subDirs.Count -gt 0) {
        foreach ($sub in $subDirs) {
            $scanJobs += @{
                Name           = "${RepoName}_${sub}"
                Path           = "/vmr/src/$RepoName/src/$sub"
                IgnorePatterns = ''
            }
        }

        # Root scan ignoring all subdirectories
        $autoIgnore = $subDirs -join ' '
        $scanJobs += @{
            Name           = $RepoName
            Path           = "/vmr/src/$RepoName"
            IgnorePatterns = $autoIgnore
        }

        Write-Host "  Auto-split: $($subDirs.Count) subdirectory scans + root (ignoring: $autoIgnore)"
    } else {
        $scanJobs += @{ Name = $RepoName; Path = "/vmr/src/$RepoName"; IgnorePatterns = $ScanIgnorePatterns }
    }
} else {
    $scanJobs += @{ Name = $RepoName; Path = "/vmr/src/$RepoName"; IgnorePatterns = $ScanIgnorePatterns }
}

#endregion

#region Run

SBTest-WriteStep "License scan plan: $($scanJobs.Count) job(s)"
foreach ($job in $scanJobs) {
    $detail = if ($job.IgnorePatterns) { " (ignoring: $($job.IgnorePatterns))" } else { '' }
    Write-Host "  - $($job.Name): $($job.Path)$detail"
}

$overallExitCode = 0

foreach ($job in $scanJobs) {
    $cmd = Build-LicenseScanCommand `
        -ScanPath $job.Path `
        -ScanName $job.Name `
        -IgnorePatterns $job.IgnorePatterns

    $exitCode = SBTest-InvokeDockerTest -ScanName $job.Name -TestCommand $cmd
    if ($exitCode -ne 0) {
        $overallExitCode = $exitCode
    }
}

#endregion

#region Collect & report

SBTest-CollectResults -Label 'LicenseScan'

SBTest-WriteStep "Done"
if ($overallExitCode -ne 0) {
    SBTest-WriteResult "One or more license scan tests failed. Check results in: $script:SBTest_ResultsDir" -IsError
} else {
    SBTest-WriteResult "All license scan tests passed. Results in: $script:SBTest_ResultsDir"
}

exit $overallExitCode

#endregion

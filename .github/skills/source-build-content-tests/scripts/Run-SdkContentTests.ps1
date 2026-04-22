<#
.SYNOPSIS
    Run source-build SDK content diff tests inside the test container.

.DESCRIPTION
    Compares a source-built SDK tarball against a Microsoft-built SDK tarball to detect
    unexpected differences. Dot-sources Setup-Tests.ps1 to prepare Docker and import helpers.

.PARAMETER MsftSdkTarballPath
    Path to the Microsoft-built SDK tarball (e.g., dotnet-sdk-*-linux-x64.tar.gz).
    If omitted (together with -SdkTarballPath), the script runs against current VMR artifacts.

.PARAMETER SdkTarballPath
    Path to the source-built SDK tarball (e.g., dotnet-sdk-*-centos.10-x64.tar.gz).
    If omitted (together with -MsftSdkTarballPath), the script runs against current VMR artifacts.

.PARAMETER SourceBuiltArtifactsPath
    Path to the source-built artifacts tarball (Private.SourceBuilt.Artifacts.*.tar.gz). Optional.

.PARAMETER TargetRid
    RID of the source-built SDK (e.g., 'centos.10-x64'). Must match the source-built tarball.

.PARAMETER PortableTargetRid
    RID of the Microsoft SDK (e.g., 'linux-x64'). Must match the MSFT tarball.

.PARAMETER ContainerImage
    Docker image override (passed through to Setup-Tests.ps1).

.PARAMETER VmrPath
    VMR root override (passed through to Setup-Tests.ps1).

.PARAMETER ResultsDir
    Results directory override (passed through to Setup-Tests.ps1).

.PARAMETER DockerStartTimeoutSeconds
    Docker start timeout override (passed through to Setup-Tests.ps1).

.EXAMPLE
    .\Run-SdkContentTests.ps1 `
        -MsftSdkTarballPath C:\artifacts\dotnet-sdk-11.0.100-linux-x64.tar.gz `
        -SdkTarballPath C:\artifacts\dotnet-sdk-11.0.100-centos.10-x64.tar.gz `
        -SourceBuiltArtifactsPath C:\artifacts\Private.SourceBuilt.Artifacts.11.0.100-centos.10-x64.tar.gz `
        -TargetRid centos.10-x64 -PortableTargetRid linux-x64
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$MsftSdkTarballPath,

    [Parameter()]
    [string]$SdkTarballPath,

    [Parameter()]
    [string]$SourceBuiltArtifactsPath,

    [Parameter(Mandatory)]
    [string]$TargetRid,

    [Parameter(Mandatory)]
    [string]$PortableTargetRid,

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

$hasAnyTarballInput = -not [string]::IsNullOrWhiteSpace($MsftSdkTarballPath) -or `
    -not [string]::IsNullOrWhiteSpace($SdkTarballPath) -or `
    -not [string]::IsNullOrWhiteSpace($SourceBuiltArtifactsPath)

$usingBuildArtifacts = $hasAnyTarballInput

if ($usingBuildArtifacts -and ([string]::IsNullOrWhiteSpace($MsftSdkTarballPath) -or [string]::IsNullOrWhiteSpace($SdkTarballPath))) {
    throw "When supplying tarball inputs, both -MsftSdkTarballPath and -SdkTarballPath are required. Omit both to use current VMR mode."
}

if ($usingBuildArtifacts) {
    SBTest-WriteStep "SDK content mode: build-artifact input (SkipPrepareSdkArchive=true)"
    foreach ($p in @($MsftSdkTarballPath, $SdkTarballPath)) {
        if (-not (Test-Path $p)) {
            throw "Tarball not found: $p"
        }
    }
    if ($SourceBuiltArtifactsPath -and -not (Test-Path $SourceBuiltArtifactsPath)) {
        throw "Artifacts tarball not found: $SourceBuiltArtifactsPath"
    }
}

# Resolve to absolute paths
if ($usingBuildArtifacts) {
    $MsftSdkTarballPath = (Resolve-Path $MsftSdkTarballPath).Path
    $SdkTarballPath     = (Resolve-Path $SdkTarballPath).Path
    if ($SourceBuiltArtifactsPath) {
        $SourceBuiltArtifactsPath = (Resolve-Path $SourceBuiltArtifactsPath).Path
    }
}

# Convert Windows paths to container paths. Files under the VMR are accessible via /vmr mount.
# Files outside the VMR need an additional -v mount — for simplicity we require them under VmrPath.
function ConvertToContainerPath {
    param([string]$HostPath)
    if ($HostPath.StartsWith($script:SBTest_VmrPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $HostPath.Substring($script:SBTest_VmrPath.Length)
        return "/vmr" + (SBTest-ConvertToDockerPath $rel)
    }
    throw "Tarball path '$HostPath' is outside the VMR ('$($script:SBTest_VmrPath)'). Place tarballs under the VMR directory or provide container-relative paths."
}

$msftContainer = if ($usingBuildArtifacts) { ConvertToContainerPath $MsftSdkTarballPath } else { '' }
$sbContainer = if ($usingBuildArtifacts) { ConvertToContainerPath $SdkTarballPath } else { '' }
$sbArtifactsContainer = if ($usingBuildArtifacts -and $SourceBuiltArtifactsPath) { ConvertToContainerPath $SourceBuiltArtifactsPath } else { '' }

if (-not $usingBuildArtifacts) {
    SBTest-WriteStep "SDK content mode: current VMR artifacts (SkipPrepareSdkArchive=false)"
}

#endregion

#region Run

$timestamp = Get-Date -Format 'MMddHHmmss'

$parts = @(
    "dotnet test"
    "/vmr/test/Microsoft.DotNet.SourceBuild.Tests/Microsoft.DotNet.SourceBuild.Tests.csproj"
    '--filter "Category=SdkContent"'
    "--logger:console`;verbosity=detailed"
    "-c Release"
    "-bl:/vmr/artifacts/log/Debug/BuildTests_SdkContent_${timestamp}.binlog"
    "-flp:LogFile=/vmr/artifacts/log/Debug/BuildTests_SdkContent_${timestamp}.log"
    "-clp:v=m"
    "/p:TargetRid=$TargetRid"
    "/p:PortableTargetRid=$PortableTargetRid"
    "--results-directory /vmr/artifacts/TestResults/SdkContent"
)

if ($usingBuildArtifacts) {
    $parts += "/p:MsftSdkTarballPath=$msftContainer"
    $parts += "/p:SdkTarballPath=$sbContainer"
    $parts += "/p:SkipPrepareSdkArchive=true"
} else {
    $parts += "/p:SkipPrepareSdkArchive=false"
}

if ($sbArtifactsContainer) {
    $parts += "/p:SourceBuiltArtifactsPath=$sbArtifactsContainer"
}

$cmd = $parts -join ' '
$exitCode = SBTest-InvokeDockerTest -ScanName 'SdkContent' -TestCommand $cmd

#endregion

#region Collect & report

SBTest-CollectResults -Label 'SdkContent'

SBTest-WriteStep "Done"
if ($exitCode -ne 0) {
    SBTest-WriteResult "SDK content tests failed. Check results in: $script:SBTest_ResultsDir" -IsError
} else {
    SBTest-WriteResult "All SDK content tests passed. Results in: $script:SBTest_ResultsDir"
}

exit $exitCode

#endregion

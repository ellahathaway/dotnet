---
name: source-build-content-tests
description: >
  Run source-build content tests (SDK diff tests, license scan tests, and binary detection tests) locally.
  Use when asked "run sdk content tests", "run license scan", "source-build tests",
  "sdk diff tests", "compare sdk tarballs", "scan licenses", "run SdkContentTests",
  "run LicenseScanTests", "scan for binaries".
  DO NOT USE FOR: running other test suites (scenario tests, watch tests, etc.).
---

# Source-Build Content Tests

Run SDK content diff tests, license scan tests, and binary detection tests from `test/Microsoft.DotNet.SourceBuild.Tests/` locally. These tests compare source-built SDK artifacts against Microsoft-built ones, scan for license compliance, and detect any unexpected binaries.

> ⚠️ **These tests run inside a Linux container.** The recommended workflow uses PowerShell scripts (in `scripts/`) that manage Docker automatically. Docker is required on all platforms.

## Prerequisites

- The Azure DevOps MCP server must be configured for the `dnceng` organization with the `pipelines` toolset enabled.

## Reference Docs

- [running-tests.md](references/running-tests.md) — Full test commands for SDK content tests and license scan tests, RID pairs
- [configuration-and-output.md](references/configuration-and-output.md) — MSBuild property reference, test output locations, baseline update workflow, resolving failures, CI pipeline links

### Common RID Pairs

SDK content tests require a `TargetRid` and a `PortableRid`. The target rid should match the source-built SDK's RID, and the `PortableTargetRid` should match the Microsoft SDK's RID.

| Source-built RID | Microsoft RID |
|---|---|
| `centos.10-x64` | `linux-x64` |
| `almalinux.8-x64` | `linux-x64` |
| `alpine.3.23-x64` | `linux-musl-x64` |
| `fedora.43-x64` | `linux-x64` |
| `ubuntu.24.04-x64` | `linux-x64` |
| `ubuntu.24.04-arm64` | `linux-arm64` |

## Workflow

### Step 1: Build Selection

A build ID from the `dotnet-unified-build` pipeline (definition ID 1330, org `dnceng`, project `internal`) is only required when you need to download build artifacts.

1. **If the user provides a build ID** — use it directly.
2. **If no build ID is provided** — assume the current VMR checkout is the test input.
3. **If no build ID is provided and the user explicitly asks for tests against a pipeline build** — ask which branch they want (e.g., `main`, `release/10.0.1xx`), then query and present the top 3 completed builds so they can pick one.

### Step 2: Download Artifacts

If a build ID is provided, pipeline artifacts should be downloaded from the build. Refer to the source-build-investigation skill for artifact download details.

- SDK content tests:
  - **No build ID (default local VMR flow):** no artifact download required; use current VMR repo artifacts. Required SDK/artifact tarballs should already exist under `artifacts/assets/<Configuration>/`.
  - **Build ID provided (pipeline artifact flow):** download tarballs selected by RID:
    - Microsoft-built SDK tarball for `PortableTargetRid`: `dotnet-sdk-*-${PortableTargetRid}.tar.gz`.
    - Source-built SDK tarball for `TargetRid`: `dotnet-sdk-*-${TargetRid}.tar.gz`.
    - Optional source-built artifacts tarball for `TargetRid`: `Private.SourceBuilt.Artifacts.*.${TargetRid}.tar.gz`.
- License scan tests:
  - **No build ID:** no artifact download required. Use the existing source in the VMR directory.
  - **Build ID provided:** download `dotnet-source-*.tar.gz` and unpack it into a temp directory.
- Binary scan tests:
  - **No build ID:** no artifact download required. Use the existing source in the VMR directory.
  - **Build ID provided:** download `dotnet-source-*.tar.gz` and unpack it into a temp directory.

### Step 3: Run Tests

Scripts in `scripts/` handle `SkipPrepareSdkArchive` automatically.

- SDK content tests:
  - With build artifacts: `\.\scripts\Run-SdkContentTests.ps1 -TargetRid <TargetRid> -PortableTargetRid <PortableTargetRid> -SdkTarballPath <PathToSourceBuiltSdkTarball> -MsftSdkTarballPath <PathToMicrosoftBuiltSdkTarball>`
  - Without build artifacts: `.\scripts\Run-SdkContentTests.ps1 -TargetRid <TargetRid> -PortableTargetRid <PortableTargetRid>`

- License scan tests: run `\.\scripts\ScanForLicenses.ps1`:
  - With build artifacts: `\.\scripts\ScanForLicenses.ps1 -RepoName <RepoName>`
  - Without build artifacts: `\.\scripts\ScanForLicenses.ps1 -RepoName <RepoName>`

- Binary scan tests: run `\.\scripts\DetectBinaries.ps1`:
  - With build artifacts: `\.\scripts\DetectBinaries.ps1 -RepoRootPath <PathToSourceBuiltRepo>`
  - Without build artifacts: `\.\scripts\DetectBinaries.ps1`

### Step 4: Interpret Results

- SDK content tests:
- License scan tests:
- Binary scan tests:

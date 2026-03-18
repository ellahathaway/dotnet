---
name: backport-rebootstrap
description: Backport a merged 1xx rebootstrap PR to non-1xx feature band branches in dotnet/dotnet and create the required backport PRs.
---

# Backport 1xx Rebootstrap to Non-1xx Feature Band Branches

After a .NET servicing release, the `release/{major.minor}.1xx` branch in `dotnet/dotnet` receives a "rebootstrap" PR that updates the bootstrap SDK version and Arcade toolset references to the just-released versions. This rebootstrap must then be backported to other feature band branches (2xx, 3xx, etc.) — but **only** to bands that were **not** themselves released, since released bands receive their own dedicated rebootstrap PRs.

**Workflow**: Gather inputs (Step 0) → determine target branches (Step 1) → create backport PRs (Step 2) → verify results (Step 3).

## When to Use This Skill

- A 1xx rebootstrap PR has been merged and needs to be ported to other feature band branches
- You need to determine which feature band branches require the rebootstrap backport
- Questions like "backport the rebootstrap", "which branches need the 1xx rebootstrap", "port rebootstrap to 3xx"

**Not for**: Creating the initial 1xx rebootstrap PR, backflow/dependency-flow operations, Arcade SDK testing, or non-rebootstrap backports.

## Prerequisites

- **`gh` CLI**: Authenticated with access to `dotnet/dotnet`
- **`git`**: With push access to `dotnet/dotnet` (or a fork)
- **`curl`**: For fetching release metadata
- **`jq`**: For parsing JSON responses
- **Local clone**: The `dotnet/dotnet` repo must be cloned locally

## Quick Start

```bash
# List which branches need the backport (safe, no side effects)
./scripts/backport-rebootstrap.sh --pr 5417 --dotnet-repo ~/repos/dotnet --list-targets

# Create backport PRs for all target branches
./scripts/backport-rebootstrap.sh --pr 5417 --dotnet-repo ~/repos/dotnet

# Dry run — show what would be done without making changes
./scripts/backport-rebootstrap.sh --pr 5417 --dotnet-repo ~/repos/dotnet --dry-run
```

## Step 0: Understand the Inputs

The script requires:

| Input | Description | Example |
|-------|-------------|---------|
| `--pr` | The merged 1xx rebootstrap PR number | `5417` |
| `--dotnet-repo` | Path to the local `dotnet/dotnet` clone | *(required)* |
| `--repo` | GitHub `owner/repo` (optional, default: `dotnet/dotnet`) | `dotnet/dotnet` |
| `--upstream-remote` | Git remote name for dotnet/dotnet (auto-detected if omitted) | `upstream` |
| `--list-targets` | Only list target branches, don't create PRs | (flag) |
| `--dry-run` | Show what would be done without making changes | (flag) |
| `--fork` | Push to a fork instead of origin (specify `owner/repo`) | `myuser/dotnet` |

Before running, verify:
1. The 1xx rebootstrap PR is **merged** (not just opened)
2. Your `gh` CLI is authenticated: `gh auth status`
3. Your local `dotnet/dotnet` clone is clean: `git status` shows no uncommitted changes

## Step 1: Determine Target Branches

The script automatically determines which branches need the backport:

1. **Reads the PR** to find the base branch (e.g., `release/10.0.1xx`) and extract the major.minor version (e.g., `10.0`)
2. **Fetches release metadata** from `https://builds.dotnet.microsoft.com/dotnet/release-metadata/{major.minor}/releases.json`
3. **Identifies released feature bands** by examining the `sdks` array in the latest release entry — each SDK version like `10.0.105` maps to band `1xx`, `10.0.201` maps to `2xx`
4. **Lists all feature band branches** in `dotnet/dotnet` matching `release/{major.minor}.*xx`
5. **Computes targets** = all feature band branches − source branch − branches whose band was released

### Example

Given PR #5417 targeting `release/10.0.1xx`, with the latest 10.0.5 release shipping SDKs `10.0.105` (1xx) and `10.0.201` (2xx), and branches `release/10.0.1xx`, `release/10.0.2xx`, `release/10.0.3xx` existing:

- **Source branch**: `release/10.0.1xx` → excluded (it's the source)
- **Released bands**: 1xx (from 10.0.105), 2xx (from 10.0.201) → excluded
- **Target branches**: `release/10.0.3xx` ✅

If only `10.0.105` (1xx) had been released, the targets would be both `release/10.0.2xx` and `release/10.0.3xx`.

## Step 2: Create Backport PRs

For each target branch, the script:

1. Fetches the latest from origin
2. Creates a new branch: `backport/pr-{PR#}-to-{target-branch}`
3. Cherry-picks the commits from the rebootstrap PR
4. Pushes the branch
5. Creates a PR targeting the feature band branch

### What Gets Cherry-Picked

A rebootstrap PR typically has one content commit plus a merge commit. The script automatically filters out merge commits (which cannot be cherry-picked directly) and only cherry-picks the non-merge commits.

The content commit modifies these files:

| File | What Changes |
|------|-------------|
| `global.json` | `tools.dotnet` version (bootstrap SDK) and `Microsoft.DotNet.Arcade.Sdk` version |
| `eng/Versions.props` | `PrivateSourceBuiltSdkVersion` and `PrivateSourceBuiltArtifactsVersion` |
| `eng/Version.Details.xml` | Arcade SDK and Build Manifest dependency versions and SHAs |
| `eng/Version.Details.props` | Arcade SDK and Build Manifest package version properties |

These values are the same across all feature band branches (they all bootstrap from the 1xx SDK), but the surrounding file content may differ — especially in non-1xx branches that have additional properties.

### Handling Cherry-Pick Conflicts

Cherry-pick conflicts are **common** because non-1xx branches typically have extra properties and different base versions. When conflicts occur:

- The script will report the conflicted files and provide manual resolution instructions
- You must create the branch manually, cherry-pick, resolve conflicts, and push

**Common conflict patterns and resolutions:**

#### `global.json`
Usually straightforward — take the incoming (rebootstrap) values for `tools.dotnet` and `Microsoft.DotNet.Arcade.Sdk`:
```json
{
  "tools": { "dotnet": "10.0.105" },       ← take incoming
  "msbuild-sdks": {
    "Microsoft.DotNet.Arcade.Sdk": "10.0.0-beta.26153.111"  ← take incoming
  }
}
```

#### `eng/Versions.props`
Take incoming `PrivateSourceBuiltSdkVersion` and `PrivateSourceBuiltArtifactsVersion`, but **preserve non-1xx-specific properties** that the target branch has but the source branch does not:
```xml
<!-- Take these from incoming (rebootstrap values): -->
<PrivateSourceBuiltSdkVersion>10.0.105-servicing.26153.111</PrivateSourceBuiltSdkVersion>
<PrivateSourceBuiltArtifactsVersion>10.0.105-servicing.26153.111</PrivateSourceBuiltArtifactsVersion>
<!-- KEEP these from the target branch (non-1xx specific): -->
<DotNet1xxWorkloadManifestVersion>...</DotNet1xxWorkloadManifestVersion>
<MicrosoftDotNetDarcLibVersion>...</MicrosoftDotNetDarcLibVersion>
```

#### `eng/Version.Details.xml` and `eng/Version.Details.props`
Take incoming Arcade SDK and Build Manifest versions/SHAs, but **preserve any extra dependencies** that exist in the target branch (e.g., `Microsoft.NET.Sdk`, `Microsoft.NETCore.App.Ref`, `Microsoft.NETCore.Platforms`, `MicrosoftDotNetDarcLibPackageVersion`).

### Remote Configuration

The script auto-detects the remote name for `dotnet/dotnet` by scanning `git remote -v`. Use `--upstream-remote` to override if auto-detection fails. Use `--fork owner/repo` to push to your fork instead of the upstream remote.

## Step 3: Verify Results

After the script completes, verify:

1. **PRs were created**: Check the script output for PR URLs
2. **Changes are correct**: Review each PR to confirm the bootstrap SDK version, Arcade versions, and SHAs match the source rebootstrap PR

### Expected PR Content

Each backport PR should have the same diff as the original rebootstrap PR — the same version bumps in the same files. The only difference is the target branch.

Example output format:

```
## Backport Rebootstrap Results

**Source PR**: #5417 (release/10.0.1xx)
**Released bands**: 1xx, 2xx
**Target branches**: release/10.0.3xx

| # | Target Branch | Status | PR |
|---|---------------|--------|-----|
| 1 | release/10.0.3xx | ✅ Created | #5520 |
```

The backport PR title mirrors the source PR title with a branch prefix. For example, if the source PR is titled `.NET 10.0.105 March 2026 Updates`, the backport PR will be titled `[release/10.0.3xx] .NET 10.0.105 March 2026 Updates`.

## Anti-Patterns

> ❌ **Don't backport to released feature bands.** Released bands get their own rebootstrap PRs with band-specific version numbers. Backporting the 1xx rebootstrap to a released band would set incorrect bootstrap versions.

> ❌ **Don't backport before the source PR is merged.** The cherry-pick uses the merge commit. Wait for the 1xx rebootstrap PR to be merged first.

> ❌ **Don't skip the release metadata check.** Always verify which bands were released — don't assume only the 1xx was released. Multiple bands can ship simultaneously.

> ❌ **Don't manually edit the cherry-picked files** unless resolving a genuine conflict. The rebootstrap values must exactly match the source PR.

> ❌ **Don't forget to check for existing backport PRs.** Search for open PRs with `backport/pr-{PR#}` in the branch name before creating duplicates.

## References

- **Rebootstrap overview**: [references/rebootstrap-overview.md](references/rebootstrap-overview.md)
- **Release metadata**: [references/release-metadata.md](references/release-metadata.md)
- **Feature band branches**: [references/feature-band-branches.md](references/feature-band-branches.md)

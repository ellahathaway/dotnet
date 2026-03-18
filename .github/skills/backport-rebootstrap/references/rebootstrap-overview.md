# Rebootstrap Overview

A **rebootstrap** is the process of updating the bootstrap SDK and toolset references in `dotnet/dotnet` after a .NET servicing release. It ensures that source-build uses the just-released SDK to bootstrap itself, rather than the previous release's SDK.

## What is Bootstrapping?

The `dotnet/dotnet` VMR (Virtual Monolithic Repository) builds the .NET SDK from source. To compile the SDK, it needs an already-built SDK — this is the **bootstrap SDK**. The bootstrap SDK is specified in several files and is typically the most recently released SDK from the same major.minor version.

## What a Rebootstrap PR Changes

A rebootstrap PR updates these files:

### `global.json`

```json
{
  "tools": {
    "dotnet": "10.0.105"        // ← bootstrap SDK version
  },
  "msbuild-sdks": {
    "Microsoft.DotNet.Arcade.Sdk": "10.0.0-beta.26153.111"  // ← Arcade SDK version
  }
}
```

- `tools.dotnet`: Updated to the just-released SDK version (e.g., `10.0.104` → `10.0.105`)
- `Microsoft.DotNet.Arcade.Sdk`: Updated to the Arcade SDK version from the release

### `eng/Versions.props`

```xml
<PrivateSourceBuiltSdkVersion>10.0.105-servicing.26153.111</PrivateSourceBuiltSdkVersion>
<PrivateSourceBuiltArtifactsVersion>10.0.105-servicing.26153.111</PrivateSourceBuiltArtifactsVersion>
```

These properties specify the exact SDK and artifacts versions used for source-build bootstrapping, including the servicing build ID suffix.

### `eng/Version.Details.xml`

```xml
<Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="10.0.0-beta.26153.111">
  <Uri>https://dev.azure.com/dnceng/internal/_git/dotnet-dotnet</Uri>
  <Sha>a612c2a1056fe3265387ae3ff7c94eba1505caf9</Sha>
</Dependency>
<Dependency Name="Microsoft.DotNet.Build.Manifest" Version="10.0.0-beta.26153.111">
  <Uri>https://dev.azure.com/dnceng/internal/_git/dotnet-dotnet</Uri>
  <Sha>a612c2a1056fe3265387ae3ff7c94eba1505caf9</Sha>
</Dependency>
```

Updates dependency versions and commit SHAs for the Arcade SDK and Build Manifest packages.

### `eng/Version.Details.props`

```xml
<MicrosoftDotNetArcadeSdkPackageVersion>10.0.0-beta.26153.111</MicrosoftDotNetArcadeSdkPackageVersion>
<MicrosoftDotNetBuildManifestPackageVersion>10.0.0-beta.26153.111</MicrosoftDotNetBuildManifestPackageVersion>
```

Updates the NuGet package version properties for the Arcade SDK and Build Manifest.

## Why Backport?

All feature band branches (1xx, 2xx, 3xx, etc.) bootstrap from the 1xx SDK. When a new 1xx SDK is released, all branches need to update their bootstrap references. However:

- **Released bands** get their own rebootstrap PRs (since they were part of the release and have their own release-specific commit SHAs)
- **Unreleased bands** can receive a cherry-pick of the 1xx rebootstrap, since the bootstrap values are the same

## Release-Specific vs Cherry-Picked Rebootstraps

| Scenario | Action |
|----------|--------|
| Band was released (e.g., 1xx released SDK 10.0.105) | Gets its own rebootstrap PR (already done — this is the source PR) |
| Band was released (e.g., 2xx released SDK 10.0.201) | Gets its own rebootstrap PR (not our concern) |
| Band was NOT released (e.g., 3xx has no 10.0.3xx SDK yet) | Cherry-pick the 1xx rebootstrap PR |

The 1xx rebootstrap always happens first (it's the primary development band), and then its changes are backported to unreleased bands.

## Non-1xx Branch Differences

Non-1xx feature band branches (2xx, 3xx, etc.) have the same rebootstrap-relevant files but often contain **additional properties** not present in the 1xx branch. When cherry-picking, these must be preserved:

### `eng/Versions.props` extras

```xml
<!-- Only in 2xx+ branches: workload manifest version from the 1xx SDK band -->
<DotNet1xxWorkloadManifestVersion>$([System.Text.RegularExpressions.Regex]::Match($(MicrosoftNETSdkVersion), '^(\d+\.\d+\.\d+)').Value)</DotNet1xxWorkloadManifestVersion>
<!-- May have different DarcLib version than 1xx -->
<MicrosoftDotNetDarcLibVersion>1.1.0-beta.26062.6</MicrosoftDotNetDarcLibVersion>
```

### `eng/Version.Details.props` extras

```xml
<!-- Only in 2xx+ branches: SDK, runtime, and platforms references -->
<MicrosoftNETSdkPackageVersion>10.0.105-servicing.26153.111</MicrosoftNETSdkPackageVersion>
<MicrosoftNETCoreAppRefPackageVersion>10.0.5</MicrosoftNETCoreAppRefPackageVersion>
<MicrosoftNETCorePlatformsPackageVersion>10.0.5-servicing.26153.111</MicrosoftNETCorePlatformsPackageVersion>
```

### `eng/Version.Details.xml` extras

```xml
<!-- Only in 2xx+ branches: additional dependencies -->
<Dependency Name="Microsoft.NET.Sdk" Version="10.0.105-servicing.26153.111">...</Dependency>
<Dependency Name="Microsoft.NETCore.App.Ref" Version="10.0.5">...</Dependency>
<Dependency Name="Microsoft.NETCore.Platforms" Version="10.0.5-servicing.26153.111">...</Dependency>
```

These extra properties are why cherry-picks often conflict — the 1xx rebootstrap PR does not have these lines, so git cannot cleanly merge the surrounding context.

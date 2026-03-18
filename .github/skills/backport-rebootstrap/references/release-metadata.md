# Release Metadata

How to use .NET release metadata to determine which SDK feature bands were released, which drives which branches need backporting.

## Data Sources

### releases-index.json

**URL**: `https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json`

Lists all .NET release channels with their latest versions and links to detailed release data.

```json
{
  "releases-index": [
    {
      "channel-version": "10.0",
      "latest-release": "10.0.5",
      "latest-sdk": "10.0.201",
      "support-phase": "active",
      "releases.json": "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"
    }
  ]
}
```

Key fields:
- `channel-version`: The major.minor version (e.g., `10.0`)
- `latest-release`: The latest runtime release version
- `releases.json`: URL to the detailed release metadata

### releases.json

**URL pattern**: `https://builds.dotnet.microsoft.com/dotnet/release-metadata/{major.minor}/releases.json`

Contains detailed information about every release in a channel, including all SDKs shipped.

```json
{
  "channel-version": "10.0",
  "latest-release": "10.0.5",
  "releases": [
    {
      "release-date": "2026-03-12",
      "release-version": "10.0.5",
      "sdk": {
        "version": "10.0.201"
      },
      "sdks": [
        { "version": "10.0.201" },
        { "version": "10.0.105" }
      ]
    }
  ]
}
```

Key fields:
- `releases[0]`: The latest release (sorted newest first)
- `releases[0].sdk`: The primary SDK for this release
- `releases[0].sdks`: **All SDKs shipped in this release** — this is what we use

## Determining Released Feature Bands

The `sdks` array in the latest release entry lists all SDK versions that were shipped. Each SDK version encodes its feature band in the version number.

### SDK Version Format

```
{major}.{minor}.{band}{patch}

Examples:
  10.0.105  → band digit = 1 → 1xx
  10.0.201  → band digit = 2 → 2xx
  10.0.312  → band digit = 3 → 3xx
```

The **hundreds digit** of the third version segment is the feature band number.

### Extraction Logic

```bash
# Given SDK version "10.0.201"
patch="$(echo "10.0.201" | cut -d. -f3)"   # "201"
band="${patch:0:1}"                          # "2"
# → This SDK is in the 2xx feature band
```

### Example Scenarios

**Scenario 1**: Latest release shipped SDKs `10.0.105` and `10.0.201`
- Released bands: 1xx, 2xx
- Unreleased bands (assuming 3xx branch exists): 3xx
- Backport targets: `release/10.0.3xx`

**Scenario 2**: Latest release shipped only SDK `10.0.105`
- Released bands: 1xx
- Unreleased bands: 2xx, 3xx
- Backport targets: `release/10.0.2xx`, `release/10.0.3xx`

**Scenario 3**: Latest release shipped SDKs `10.0.105`, `10.0.201`, `10.0.302`
- Released bands: 1xx, 2xx, 3xx
- Unreleased bands: none
- Backport targets: none (all bands were released)

## Important Notes

- The `sdks` array may not exist in older release data — fall back to the singular `sdk` field
- Only the **latest** (first) release entry matters for determining current released bands
- Preview/RC releases may have different SDK version formats — this process only applies to stable releases

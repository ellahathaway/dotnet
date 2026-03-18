# Feature Band Branches

How feature band branches work in `dotnet/dotnet` and how they relate to SDK versioning and rebootstrapping.

## Branch Naming Convention

Feature band branches follow the pattern:

```
release/{major.minor}.{band}xx
```

Examples for .NET 10.0:
- `release/10.0.1xx` — the primary development/servicing branch (band 1)
- `release/10.0.2xx` — band 2 (typically ships with a specific Visual Studio version)
- `release/10.0.3xx` — band 3

## What is a Feature Band?

A feature band is a grouping of .NET SDK releases. Each band can ship independently with different SDK features while sharing the same runtime. The band number corresponds to the hundreds digit of the SDK version:

| SDK Version | Band | Branch |
|-------------|------|--------|
| 10.0.100-105 | 1xx | `release/10.0.1xx` |
| 10.0.200-205 | 2xx | `release/10.0.2xx` |
| 10.0.300-305 | 3xx | `release/10.0.3xx` |

## Discovering Feature Band Branches

### Via GitHub API

```bash
gh api "repos/dotnet/dotnet/branches" --paginate --jq '.[].name' \
  | grep -E '^release/10\.0\.[0-9]+xx$' \
  | sort
```

### Via git

```bash
git ls-remote --heads origin 'refs/heads/release/10.0.*xx' \
  | awk '{print $2}' \
  | sed 's|refs/heads/||'
```

### Expected output

```
release/10.0.1xx
release/10.0.2xx
release/10.0.3xx
```

## Branch Relationships

All feature band branches share the same runtime but may have different SDK content:

```
release/10.0.1xx  ─── SDK 10.0.1xx (primary, ships with .NET installer)
release/10.0.2xx  ─── SDK 10.0.2xx (ships with VS 17.x)
release/10.0.3xx  ─── SDK 10.0.3xx (ships with VS 17.y)
```

### Bootstrapping

All feature bands bootstrap from the **1xx SDK**. This means:
- `PrivateSourceBuiltSdkVersion` always references a 1xx version (e.g., `10.0.105`)
- `global.json` `tools.dotnet` always references a 1xx version
- When the 1xx SDK is released, ALL branches need to update their bootstrap references

### Specific Release Branches

In addition to `Nxx` branches, there are specific release branches like:
- `release/10.0.101`
- `release/10.0.102`
- `release/10.0.103`

These are for specific patch releases and are **not** targets for rebootstrap backporting. The script filters for `*xx` pattern branches only.

## Determining Which Bands Exist

Not all bands exist for every .NET version. Early in a release cycle, only `1xx` may exist. Additional bands are created as Visual Studio releases are planned.

To determine what bands exist, list the branches:

```bash
# Using the backport-rebootstrap script
./backport-rebootstrap.sh --pr <NUMBER> --dotnet-repo <PATH> --list-targets

# Or manually
gh api "repos/dotnet/dotnet/branches" --paginate --jq '.[].name' \
  | grep -E '^release/[0-9]+\.[0-9]+\.[0-9]+xx$' \
  | sort
```

## Band Lifecycle

1. **Creation**: A new band branch is created when a new Visual Studio version starts development
2. **Development**: The band receives dependency flow updates and may diverge from other bands
3. **Release**: The band ships an SDK (e.g., `10.0.201` for the 2xx band)
4. **Servicing**: The band continues to receive servicing updates after release
5. **EOL**: The band stops receiving updates when the .NET version reaches end of life

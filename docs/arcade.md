# Arcade Build System

This repository uses the [dotnet/arcade](https://github.com/dotnet/arcade) build system for versioning, signing, packaging, publishing, and CI/CD.

## Key Files

| File | Purpose |
|------|---------|
| `global.json` | Pins Arcade SDK version |
| `Directory.Build.props` | Imports Arcade Sdk.props, sets common properties |
| `Directory.Build.targets` | Imports Arcade Sdk.targets |
| `eng/Versions.props` | Version prefix, pre-release label, all dependency versions |
| `eng/Version.Details.xml` | Tracks dependency source repos and SHAs for maestro |
| `eng/Publishing.props` | Publishing configuration (V3) |
| `eng/common/` | Shared arcade build scripts (do not edit manually) |
| `Directory.Packages.props` | Central Package Management — versions reference `eng/Versions.props` properties |

## Versioning

Arcade manages versions automatically:
- **VersionPrefix**: `0.23.1` (from `eng/Versions.props`)
- **PreReleaseVersionLabel**: `preview`
- Official builds append build number: `0.23.1-preview.26166.3`

## Building

```bash
# Local build
dotnet build

# CI-style build (uses arcade scripts)
./eng/common/cibuild.sh --configuration Release --prepareMachine
```

## Shipping Packages

These projects have `IsShipping=true` and will be published to NuGet.org:
- Microsoft.Maui.DevFlow.Logging
- Microsoft.Maui.DevFlow.Agent
- Microsoft.Maui.DevFlow.Driver
- Microsoft.Maui.DevFlow.CLI

All other packages are non-shipping (internal dev feeds only).

## Dependency Flow

Dependencies are tracked in `eng/Version.Details.xml` and updated automatically by maestro via darc subscriptions. See the PR description for setup commands.

## Updating eng/common/

The `eng/common/` directory is managed by arcade and updated automatically via darc. Do not edit files in this directory manually.

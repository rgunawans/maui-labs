# Agent Instructions

Instructions for GitHub Copilot and other AI coding agents working with the maui-labs repository.

## Repository Overview

This repository hosts experimental .NET MAUI packages. It is a **multi-product mono-repo** — each product lives under `src/{Product}/` with its own version, solution filter, and CI workflow.

### Products

| Product | Package / Tool | Description |
|---------|---------------|-------------|
| **DevFlow** | `Microsoft.Maui.DevFlow.*` (9 packages), `maui-devflow` CLI | Runtime MAUI automation toolkit. In-app agent with HTTP API, visual tree inspection, CDP bridge for Blazor WebViews, MCP server for AI agents, cross-platform driver library. |
| **Maui.Client** | `Microsoft.Maui.Client`, `maui` CLI | Environment setup CLI. Android SDK management, JDK installation, emulator creation, `doctor` diagnostics. |

### Technology Stack

- **.NET 10** (SDK version pinned in `global.json`, `rollForward: latestMinor`)
- **C#** with `LangVersion: latest`, file-scoped namespaces
- **Microsoft.DotNet.Arcade.Sdk** for build infrastructure
- **Central Package Management** — all versions in `Directory.Packages.props`
- **xUnit** v2.9.3 for testing, **coverlet** for coverage
- **System.CommandLine** for CLI tooling (beta4 for DevFlow, 2.0.5 stable for Client)

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- MAUI workload: `dotnet workload install maui`

### Build Commands

```bash
# Build everything
dotnet build MauiLabs.sln

# Build a single product (recommended for focused development)
dotnet build src/DevFlow/DevFlow.slnf
dotnet build src/Client/Client.slnf

# Build via Arcade CI scripts (matches what CI runs)
# macOS/Linux:
./eng/common/cibuild.sh --configuration Release --prepareMachine
# Windows:
eng\common\cibuild.cmd -configuration Release -prepareMachine
```

### Build Troubleshooting

- If restore fails, check `NuGet.config` — feeds are internal dnceng proxies, not nuget.org
- If workload errors occur: `dotnet workload install maui macos maui-tizen`
- SDK version mismatch: check `global.json` vs `dotnet --version`

## Testing

```bash
# All tests
dotnet test MauiLabs.sln

# Per-product
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
dotnet test src/Client/Microsoft.Maui.Client.UnitTests/
```

- Tests run in CI on **macOS and Windows** (matrix build)
- Test results: `artifacts/TestResults/**/*.xml`
- No quarantine or outerloop test attributes are used in this repo

## Code Conventions

- **ImplicitUsings**: enabled repo-wide
- **Nullable**: enabled repo-wide (`#nullable enable` is implicit)
- **File-scoped namespaces**: all files use `namespace X.Y.Z;` (not block-scoped)
- **No strong naming**: `SignAssembly: false`
- **Namespace pattern**: `Microsoft.Maui.DevFlow.{Component}.{SubComponent}` or `Microsoft.Maui.Client.{Component}`
- **No .editorconfig**: relies on Arcade SDK defaults
- **TreatWarningsAsErrors**: false (not enforced)

## Project Layout

```
maui-labs/
├── src/
│   ├── DevFlow/                          # DevFlow product
│   │   ├── Microsoft.Maui.DevFlow.Agent.Core/   # Platform-agnostic agent (HTTP server, visual tree)
│   │   ├── Microsoft.Maui.DevFlow.Agent/         # Platform-specific overrides (iOS/Android/macOS/Windows)
│   │   ├── Microsoft.Maui.DevFlow.Agent.Gtk/     # GTK/Linux agent
│   │   ├── Microsoft.Maui.DevFlow.Blazor/        # Blazor WebView CDP bridge
│   │   ├── Microsoft.Maui.DevFlow.Blazor.Gtk/    # WebKitGTK CDP bridge
│   │   ├── Microsoft.Maui.DevFlow.CLI/           # CLI global tool (maui-devflow)
│   │   │   ├── Broker/                           # Connection management
│   │   │   └── Mcp/Tools/                        # 17 MCP tool implementations
│   │   ├── Microsoft.Maui.DevFlow.Driver/        # Cross-platform driver (AgentClient)
│   │   ├── Microsoft.Maui.DevFlow.Logging/       # JSONL file logger
│   │   ├── Microsoft.Maui.DevFlow.Tests/         # xUnit tests
│   │   └── DevFlow.slnf                          # Solution filter
│   └── Client/                           # Maui.Client product
│       ├── Microsoft.Maui.Client/                # CLI tool (maui)
│       ├── Microsoft.Maui.Client.UnitTests/      # xUnit tests
│       └── Client.slnf                           # Solution filter
├── samples/                              # Sample MAUI apps (not shipped)
├── playground/                           # Manual test/scratch apps
├── eng/                                  # Shared build infrastructure
│   ├── pipelines/                        # Azure DevOps pipeline definitions
│   ├── Versions.props                    # Central version definitions
│   ├── Signing.props                     # Code signing configuration
│   ├── Publishing.props                  # NuGet publishing config
│   └── common/                           # Arcade SDK (DO NOT MODIFY)
├── Directory.Build.props                 # Global MSBuild properties
├── Directory.Build.targets               # Global MSBuild targets
├── Directory.Packages.props              # Central Package Management
├── global.json                           # SDK version pinning
├── NuGet.config                          # NuGet feed configuration
└── MauiLabs.sln                          # Full solution
```

### Key Configuration Files

| File | Purpose |
|------|---------|
| `global.json` | .NET SDK version and Arcade SDK version |
| `Directory.Build.props` | Global properties: TFMs, nullable, implicit usings, platform versions |
| `Directory.Packages.props` | All NuGet package versions (Central Package Management) |
| `eng/Versions.props` | Product version (`0.1.0-preview`), dependency versions |
| `eng/Signing.props` | Code signing: Microsoft cert for first-party, 3PartySHA2 for third-party |
| `eng/Publishing.props` | Arcade publishing version |
| `src/{Product}/Version.props` | Per-product version override |

## Packaging and Signing

- Packages are built by the Arcade SDK's `Pack` target
- **PackAsTool**: Both CLIs (`maui-devflow`, `maui`) set `PackAsTool=true`
- **IsShipping/IsPackable**: Default `false` in `Directory.Build.props`; shipped projects override to `true`
- **Signing**: `eng/Signing.props` configures Microsoft .NET certificate for first-party DLLs, `3PartySHA2` for third-party dependencies, `NuGet` certificate for `.nupkg` files
- **Version flow**: `eng/Versions.props` defines `VersionPrefix`/`VersionSuffix`, Arcade SDK applies them

## CI/CD

### GitHub Actions (PR validation)

- Workflow: `.github/workflows/ci-devflow.yml` → calls `_build.yml`
- **Matrix**: macOS + Windows
- **Path-filtered**: only triggers for changed product paths
- Steps: restore → build → test → upload test results + packages

### Azure DevOps (official builds)

- Pipeline: `eng/pipelines/devflow-official.yml`
- Builds, signs, and publishes to internal feeds via Maestro/DARC
- **MicroBuild signing** enabled (`enableMicrobuild: true`) — this enforces CFS network isolation
- NuGet.org publishing: separate pipeline (`eng/pipelines/release-publish-nuget.yml`) with `networkIsolationPolicy: Permissive`

### NuGet Feed Configuration

NuGet.config uses **internal dnceng proxy feeds only** — no direct nuget.org reference:
- `dotnet-public`, `dotnet-tools`, `dotnet-eng`, `dotnet10`, `dotnet11`

**Do not** add `nuget.org` as a direct feed source. Package versions flow via Dependency Flow (Maestro/DARC).

## Adding a New Product

1. Create `src/{NewProduct}/` with `Version.props`, project folders, test project, `{NewProduct}.slnf`
2. Add projects to `MauiLabs.sln`
3. Add package versions to `Directory.Packages.props`
4. Add path filter in `.github/workflows/ci.yml`
5. Add signing entries in `eng/Signing.props` for any new third-party DLLs

## DevFlow MCP Tools

DevFlow exposes 17 MCP tools for AI agent integration (in `src/DevFlow/Microsoft.Maui.DevFlow.CLI/Mcp/Tools/`):

| Tool | Purpose |
|------|---------|
| `maui_agents` | List connected MAUI DevFlow agents (running apps) |
| `maui_tree` | Inspect visual tree — structured JSON hierarchy with IDs, types, bounds |
| `maui_query` | Query elements by type, AutomationId, or text |
| `maui_element` | Get full element details |
| `maui_tap` | Tap a UI element |
| `maui_fill` | Fill text into Entry/Editor |
| `maui_scroll` | Scroll by delta, item index, or into view |
| `maui_navigate` | Shell navigation to a route |
| `maui_screenshot` | Capture screenshot (page, element, or fullscreen) |
| `maui_assert` | Assert element property equals expected value |
| `maui_property` | Read any element property |
| `maui_set_property` | Live-edit element properties |
| `maui_logs` | Retrieve app logs (ILogger + WebView console) |
| `maui_network` | List captured HTTP requests |
| `maui_cdp` | Execute JavaScript in Blazor WebView via CDP |
| `maui_cdp_screenshot` | WebView screenshot via CDP |
| `maui_recording` | Start/stop screen recording |
| `maui_sensors` | List and stream device sensor data |
| `maui_preferences` | Read/write app preferences |
| `maui_secure_storage` | Read/write secure storage |
| `maui_platform` | App info, device info, display, battery, connectivity |

## Important Notes

- **`eng/common/` is auto-generated by Arcade SDK** — never modify files in this directory manually.
- **`AgentClient`** (in `Microsoft.Maui.DevFlow.Driver`) is the public API consumed by NuGet users. Method signature changes are **binary and source breaking** for consumers.
- The repo is at version **0.1.0-preview** — breaking changes are acceptable but should be documented.
- **Platform conditionals**: Use `#if IOS`, `#if ANDROID`, `#if MACCATALYST`, `#if MACOS`, `#if WINDOWS` for platform-specific code in multi-targeting projects.

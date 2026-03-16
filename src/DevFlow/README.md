# Microsoft.Maui.DevFlow

A comprehensive testing, automation, and debugging toolkit for .NET MAUI applications.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## Packages

| Package | Description |
|---------|-------------|
| **Microsoft.Maui.DevFlow.Agent** | In-app agent for .NET MAUI apps. Exposes visual tree, element interactions, screenshots, and profiling via HTTP/JSON API. |
| **Microsoft.Maui.DevFlow.Agent.Core** | Platform-agnostic core: HTTP server, visual tree walker, CSS selector engine, network capture, profiling. |
| **Microsoft.Maui.DevFlow.Agent.Gtk** | GTK/Linux agent for Maui.Gtk apps. |
| **Microsoft.Maui.DevFlow.Blazor** | Blazor WebView CDP bridge. Enables Chrome DevTools Protocol access for Blazor Hybrid content via Chobitsu. |
| **Microsoft.Maui.DevFlow.Blazor.Gtk** | Blazor CDP bridge for WebKitGTK on Linux. |
| **Microsoft.Maui.DevFlow.CLI** | Global CLI tool (`maui-devflow`) with 50+ commands for automation, debugging, and MCP server support. |
| **Microsoft.Maui.DevFlow.Driver** | Platform-aware app driver for iOS, Android, Mac Catalyst, Windows, and Linux. |
| **Microsoft.Maui.DevFlow.Logging** | Buffered rotating JSONL file logger. No MAUI dependency. |

## Quick Start

### 1. Install the NuGet packages

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" />
<PackageReference Include="Microsoft.Maui.DevFlow.Blazor" />  <!-- If using Blazor Hybrid -->
```

### 2. Register in MauiProgram.cs

```csharp
using Microsoft.Maui.DevFlow.Agent;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

    #if DEBUG
    builder.AddMauiDevFlowAgent();
    #endif

    return builder.Build();
}
```

### 3. Install the CLI tool

```bash
dotnet tool install -g Microsoft.Maui.DevFlow.CLI
```

### 4. Interact with your running app

```bash
# Visual tree
maui-devflow agent tree

# Take a screenshot
maui-devflow agent screenshot -o screenshot.png

# Tap an element
maui-devflow agent interact tap --automationid "MyButton"

# Start MCP server for AI agent integration
maui-devflow mcp
```

## Features

- **Visual Tree Inspection** — query the full MAUI visual tree via HTTP API or CLI
- **Element Interaction** — tap, fill, scroll, navigate, and mutate properties
- **Screenshots** — capture PNG screenshots from any platform
- **Network Monitoring** — intercept and inspect HTTP requests/responses
- **Performance Profiling** — CPU, memory, GC, and jank detection
- **Blazor CDP Bridge** — Chrome DevTools Protocol for Blazor WebViews
- **MCP Server** — 50+ structured tools for AI agent integration (Claude, etc.)
- **Logging** — buffered JSONL file logging with WebView JS console capture
- **Multi-Platform** — iOS, Android, Mac Catalyst, Windows, Linux/GTK

## Platform Support

| Platform | Status |
|----------|--------|
| Mac Catalyst | ✅ |
| iOS Simulator | ✅ |
| Linux/GTK | ✅ |
| Android | 🔄 In progress |
| Windows | 🔄 In progress |

## Documentation

- [Broker Architecture](../../docs/DevFlow/broker.md)
- [Android Setup](../../docs/DevFlow/setup-guides/android-setup.md)
- [Apple Platforms Setup](../../docs/DevFlow/setup-guides/apple-platforms-setup.md)
- [Windows Setup](../../docs/DevFlow/setup-guides/windows-setup.md)

## Development

```bash
# Open just DevFlow in your IDE
open src/DevFlow/DevFlow.slnf

# Build
dotnet build src/DevFlow/DevFlow.slnf

# Run tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

## Version

Current version is managed in [`Version.props`](Version.props).

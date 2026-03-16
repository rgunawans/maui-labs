# .NET MAUI Labs

Experimental packages and tooling for .NET MAUI. This repository hosts pre-release projects that are in active development and may ship independently.

> ⚠️ **These packages are experimental.** APIs may change between releases. These packages are not covered by the [Microsoft Support Policy](https://dotnet.microsoft.com/platform/support-policy) and are provided as-is.

## Products

### DevFlow

A comprehensive MAUI testing, automation, and debugging toolkit.

- **In-app HTTP agent** for visual tree inspection, element interaction, and screenshots
- **Blazor CDP bridge** for Chrome DevTools Protocol on Blazor WebViews
- **CLI global tool** (`maui-devflow`) with 50+ commands
- **MCP server** for AI agent integration
- **Platform drivers** for iOS, Android, Mac Catalyst, Windows, and Linux/GTK
- **Network monitoring** and **performance profiling**

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.DevFlow.Agent` | In-app agent for MAUI automation |
| `Microsoft.Maui.DevFlow.Agent.Core` | Platform-agnostic agent core |
| `Microsoft.Maui.DevFlow.Agent.Gtk` | GTK/Linux agent |
| `Microsoft.Maui.DevFlow.Blazor` | Blazor WebView CDP bridge |
| `Microsoft.Maui.DevFlow.Blazor.Gtk` | WebKitGTK CDP bridge |
| `Microsoft.Maui.DevFlow.CLI` | CLI global tool |
| `Microsoft.Maui.DevFlow.Driver` | Platform driver library |
| `Microsoft.Maui.DevFlow.Logging` | Buffered JSONL file logger |

## Getting Started

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and development setup.

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

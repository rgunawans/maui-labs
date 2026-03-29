---
applyTo: "src/DevFlow/Microsoft.Maui.DevFlow.CLI/Mcp/**"
---

# MCP Tool Development Guide

## Adding a New MCP Tool

### Step 1: Create the Tool File

Create `src/DevFlow/Microsoft.Maui.DevFlow.CLI/Mcp/Tools/MyNewTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.DevFlow.CLI.Mcp;

namespace Microsoft.Maui.DevFlow.CLI.Mcp.Tools;

[McpServerToolType]
public sealed class MyNewTool
{
    [McpServerTool(Name = "maui_my_action"),
     Description("Clear, complete description of what this tool does. Mention when an AI agent should use it.")]
    public static async Task<string> MyAction(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Describe what this parameter controls")] string requiredParam,
        [Description("Describe this optional parameter and its default behavior")] bool optionalFlag = false)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        // Call agent.* methods to interact with the running app
        var result = await agent.SomeMethodAsync(requiredParam);
        return result ?? "No result";
    }
}
```

### Step 2: Register the Tool

Add to `src/DevFlow/Microsoft.Maui.DevFlow.CLI/Mcp/McpServerHost.cs`:

```csharp
.WithTools<MyNewTool>()
```

### Step 3: Add the Corresponding AgentClient Method (if needed)

If the tool calls a new agent endpoint, add the client method in `Microsoft.Maui.DevFlow.Driver/AgentClient.cs`.

## Naming Conventions

- Tool names: `maui_` prefix + snake_case action: `maui_screenshot`, `maui_tap`, `maui_network`
- Class names: PascalCase + `Tool` suffix: `ScreenshotTool`, `InteractionTools`, `NetworkTool`
- One file can contain multiple related tools (e.g., `InteractionTools` has tap, fill, clear)

## Parameter Rules

- **Every parameter must have `[Description]`** — AI agents only see the description to decide how to use the tool
- Descriptions should explain: what the parameter does, valid values, default behavior
- `McpAgentSession session` is always the first parameter (injected by the MCP framework)
- `int? agentPort = null` should be the second parameter for most tools (enables multi-app scenarios)
- Use nullable types with defaults for optional parameters

## Return Types

- `Task<string>` — simple text result (most tools)
- `Task<ContentBlock[]>` — when returning images (screenshot tools)
- Throw `McpException` for errors that should be reported to the AI agent

## Existing Tools Reference

| File | Tools | Pattern |
|------|-------|---------|
| `AgentTools.cs` | `maui_agents` | List/discovery |
| `TreeTool.cs` | `maui_tree` | Visual tree inspection |
| `QueryTools.cs` | `maui_query` | Element search |
| `InteractionTools.cs` | `maui_tap`, `maui_fill`, `maui_clear`, `maui_scroll`, `maui_focus` | User interactions |
| `NavigationTools.cs` | `maui_navigate` | Navigation |
| `ScreenshotTool.cs` | `maui_screenshot` | Image capture |
| `AssertTool.cs` | `maui_assert` | Property assertions |
| `PropertyTools.cs` | `maui_property`, `maui_set_property` | Property read/write |
| `LogsTool.cs` | `maui_logs` | Log retrieval |
| `NetworkTool.cs` | `maui_network`, `maui_network_detail` | Network inspection |
| `CdpTools.cs` | `maui_cdp`, `maui_cdp_screenshot` | Blazor WebView CDP |
| `RecordingTools.cs` | `maui_recording_start`, `maui_recording_stop`, `maui_recording_status` | Screen recording |
| `SensorTools.cs` | `maui_sensors` | Device sensors |
| `PlatformTools.cs` | `maui_platform_*` | Device/app info |
| `PreferencesTools.cs` | `maui_preferences_*`, `maui_secure_storage_*` | Storage |

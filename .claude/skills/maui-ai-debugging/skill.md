# MAUI AI Debugging Skill

Use this skill when debugging, testing, or automating .NET MAUI applications with the DevFlow toolkit.

## Overview

DevFlow (`maui-devflow`) is a CLI tool and MCP server for inspecting, interacting with, and debugging running .NET MAUI apps. It connects to an in-app HTTP agent embedded in the MAUI application.

## Prerequisites

The target MAUI app must include the DevFlow agent. In `MauiProgram.cs`:

```csharp
#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

Install the CLI globally:

```bash
dotnet tool install -g Microsoft.Maui.DevFlow.CLI
```

## Agent Connection

The CLI connects to the in-app agent via HTTP. Use the broker for automatic discovery:

```bash
# Check connected agents
maui-devflow list

# Wait for an agent to connect (useful after app launch)
maui-devflow wait

# Check a specific agent's status
maui-devflow MAUI status
```

If multiple agents are connected, specify the port explicitly:

```bash
maui-devflow MAUI status --agent-port 10223
```

## Debugging Workflow

### 1. Inspect the Visual Tree

```bash
# Full visual tree (default depth 3)
maui-devflow MAUI tree

# Deeper inspection
maui-devflow MAUI tree --depth 10

# Include specific fields
maui-devflow MAUI tree --include-fields Opacity,BackgroundColor,IsEnabled
```

### 2. Query Elements

```bash
# By type
maui-devflow MAUI query --type Button

# By AutomationId
maui-devflow MAUI query --automation-id LoginButton

# By text content
maui-devflow MAUI query --text "Submit"

# CSS selector (Blazor WebViews)
maui-devflow MAUI query --css "button.primary"

# Hit test — find elements at screen coordinates
maui-devflow MAUI hittest 150 300
```

### 3. Get Element Details

```bash
# Full element info by query
maui-devflow MAUI element --automation-id LoginButton

# Get a specific property
maui-devflow MAUI property --automation-id LoginButton --property Text
```

### 4. Take Screenshots

```bash
# Save screenshot
maui-devflow MAUI screenshot --output screenshot.png
```

### 5. Interact with Elements

```bash
# Tap an element
maui-devflow MAUI tap --automation-id LoginButton

# Fill text into an entry
maui-devflow MAUI fill --automation-id UsernameEntry --text "user@example.com"

# Clear text
maui-devflow MAUI clear --automation-id UsernameEntry

# Navigate to a Shell route
maui-devflow MAUI navigate --route "//settings"

# Scroll
maui-devflow MAUI scroll --automation-id MyList --delta-y -200

# Set a property at runtime
maui-devflow MAUI set-property StatusLabel Text "Debug Mode"

# Resize app window
maui-devflow MAUI resize 800 600
```

### 6. Check Logs

```bash
# Fetch recent logs
maui-devflow MAUI logs

# Filter by level
maui-devflow MAUI logs --level Error
```

### 7. Monitor Network Requests

```bash
# Live TUI monitor
maui-devflow MAUI network

# List recent HTTP requests (one-shot)
maui-devflow MAUI network list

# Get full details of a specific request
maui-devflow MAUI network detail <request-id>

# Clear the request buffer
maui-devflow MAUI network clear
```

### 8. Inspect Platform Info

```bash
maui-devflow MAUI platform app-info
maui-devflow MAUI platform device-info
maui-devflow MAUI platform display
maui-devflow MAUI platform battery
maui-devflow MAUI platform connectivity
```

### 9. Manage Preferences and Secure Storage

```bash
# Preferences
maui-devflow MAUI preferences list
maui-devflow MAUI preferences get --key "theme"
maui-devflow MAUI preferences set --key "theme" --value "dark"

# Secure storage
maui-devflow MAUI secure-storage get --key "auth_token"
```

### 10. Assert Element State

```bash
# Assert a property value (useful in automated testing)
maui-devflow MAUI assert --automation-id StatusLabel --property Text --expected "Connected"
```

## Blazor WebView Debugging (CDP)

For apps using Blazor WebViews, use CDP commands:

```bash
# Check CDP status
maui-devflow cdp status

# List available WebViews
maui-devflow cdp webviews

# Evaluate JavaScript
maui-devflow cdp Runtime evaluate --expression "document.title"

# Get page source
maui-devflow cdp source

# Take a CDP screenshot
maui-devflow cdp Page captureScreenshot
```

## MCP Server Mode

DevFlow can run as an MCP server for AI agent integration:

```bash
maui-devflow mcp serve
```

This exposes all DevFlow capabilities as structured MCP tools (e.g., `maui_tree`, `maui_tap`, `maui_screenshot`, `maui_query`, `maui_logs`, `maui_network`, etc.) that AI agents can call directly.

## JSON Output

Most commands support `--json` for machine-readable output:

```bash
maui-devflow MAUI tree --json
maui-devflow MAUI query --type Button --json
maui-devflow MAUI platform app-info --json
```

## Common Debugging Patterns

### Find why a button isn't responding

1. Query the element: `maui-devflow MAUI query --automation-id MyButton --json`
2. Check `IsEnabled`, `IsVisible`, `Opacity`, and `InputTransparent` properties
3. Use hit test to check for overlapping elements: `maui-devflow MAUI hittest 150 300`
4. Check the visual tree for parent visibility: `maui-devflow MAUI tree --depth 10`

### Debug layout issues

1. Take a screenshot: `maui-devflow MAUI screenshot --output debug.png`
2. Inspect the tree with layout properties: `maui-devflow MAUI tree --include-fields Width,Height,X,Y,Bounds`
3. Query specific elements to check their bounds and properties

### Investigate network failures

1. List recent requests: `maui-devflow MAUI network list --json`
2. Check details of failed requests: `maui-devflow MAUI network detail <id>`
3. Review status codes and response bodies

### Modify UI at runtime

```bash
maui-devflow MAUI set-property StatusLabel Text "Debug Mode"
maui-devflow MAUI set-property DebugPanel IsVisible true
maui-devflow MAUI resize 800 600
```

## Broker

The broker daemon manages multi-app scenarios. It auto-starts when needed:

```bash
maui-devflow broker status   # Check broker
maui-devflow broker start    # Manual start
maui-devflow broker stop     # Stop
maui-devflow broker log      # View logs
```

## iOS-Specific Features

```bash
# Detect and dismiss alerts/dialogs
maui-devflow MAUI alert detect
maui-devflow MAUI alert dismiss

# Manage permissions (iOS Simulator)
maui-devflow MAUI permission grant --permission camera
maui-devflow MAUI permission revoke --permission location
```

## Screen Recording

```bash
maui-devflow MAUI recording start
# ... perform actions ...
maui-devflow MAUI recording stop --output recording.mp4
maui-devflow MAUI recording status
```

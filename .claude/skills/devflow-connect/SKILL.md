---
name: devflow-connect
description: >-
  Diagnose and fix DevFlow agent connectivity issues between the maui CLI and
  running .NET MAUI apps. USE FOR: "maui devflow" connection failures, agent not
  found, port conflicts, adb forwarding issues on Android, broker discovery
  problems, and validating an already-onboarded app. DO NOT USE FOR: first-time
  DevFlow package installation or MauiProgram.cs registration (use
  devflow-onboard), visual tree/CDP debugging after connection works (use
  devflow-debug), or generic app build failures. INVOKES: maui devflow CLI,
  dotnet CLI, adb, and Apple simctl tools.
---

# DevFlow Connect

Diagnose connectivity between the `maui` CLI and running .NET MAUI apps that already have the DevFlow agent integrated.

## When to Use

- `maui devflow list` shows no agents
- `maui devflow wait` times out
- `maui devflow ui tree` fails with "Cannot connect to agent"
- Port conflicts on 9223 or the broker port
- Android emulator connectivity issues (`adb reverse` or `adb forward`)
- App is running but DevFlow commands fail

## When Not to Use

- First-time DevFlow integration (use `devflow-onboard`)
- Build or deployment failures (use standard build diagnostics)
- Visual tree queries after connection works (use `devflow-debug`)
- CDP/Blazor WebView debugging after connection works (use `devflow-debug`)

## Prerequisites

- The `maui` CLI tool installed (`dotnet tool install -g Microsoft.Maui.Cli`)
- A .NET MAUI app with `Microsoft.Maui.DevFlow.Agent` NuGet package added
- `MauiProgram.cs` calls `builder.AddMauiDevFlowAgent()` in Debug builds
- The app must be running on a target device or emulator

## Workflow

### 1. Run DevFlow diagnostics

Start with the CLI's built-in health check:

```bash
maui devflow diagnose
```

Use the output to decide whether the problem is broker startup, missing project integration, no running app, or target-device networking.

### 2. Verify Agent Integration

Confirm the app has the DevFlow agent NuGet package:

```bash
grep -R --include="*.csproj" "Microsoft.Maui.DevFlow.Agent" .
```

The agent must be initialized in `MauiProgram.cs`:

```csharp
#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

If either the package reference or registration is missing, switch to `devflow-onboard`.

### 3. Check Broker Status

```bash
maui devflow broker status
```

If the broker is not running, start it:

```bash
maui devflow broker start
```

### 4. Platform-Specific Connectivity

#### Android Emulator

Android emulators run in a network namespace. Broker access and agent access use opposite directions. Use `maui devflow list` to find the assigned agent port first.

```bash
maui devflow list                # note the agent port
adb reverse tcp:19223 tcp:19223  # lets the app in the emulator reach the host broker
adb forward tcp:<port> tcp:<port> # lets the host CLI reach the app agent
```

Verify with:

```bash
adb reverse --list
adb forward --list
```

#### iOS Simulator

No port forwarding needed — simulators share the host network. Verify the app is running:

```bash
xcrun simctl list devices booted
```

#### Mac Catalyst / macOS

Direct localhost access. For Mac Catalyst, ensure Debug entitlements include `com.apple.security.network.server`. Check if the port is in use:

```bash
lsof -i :9223
```

#### Windows

Direct localhost access. Check firewall rules if connection fails.

#### Linux (GTK)

Direct localhost access. Verify the app process is running:

```bash
pgrep -f "YourApp"
```

### 5. List Connected Agents

```bash
maui devflow list
```

Expected output shows agent ID, app name, platform, and port. If empty, the agent in the app is not reaching the broker.

### 6. Wait and Test Connection

Wait for an agent from the running app:

```bash
maui devflow wait
```

Then query the visual tree:

```bash
maui devflow ui tree --depth 1
```

A successful response returns the top-level visual tree. If this fails with a timeout, the agent HTTP server inside the app may not be responding.

### 7. Common Fixes

| Symptom | Fix |
|---------|-----|
| No agents listed | Check agent NuGet package + `AddMauiDevFlowAgent()` call |
| Android: connection refused | Run `adb reverse tcp:19223 tcp:19223` for broker and `adb forward tcp:<port> tcp:<port>` for the agent port from `maui devflow list` |
| Port already in use | Kill stale process: `lsof -i :9223` then `kill <PID>` |
| Broker not running | `maui devflow broker start` |
| App crashes on startup | Check that DevFlow agent version matches MAUI version |

## Stop Signals

- Stop and switch to `devflow-onboard` if package references or `AddMauiDevFlowAgent()` are missing.
- Stop and switch to `devflow-debug` once `maui devflow wait` succeeds and `ui tree` returns a tree.
- Stop and ask which agent/device to target if multiple connected agents or devices match the app.
- Stop after confirming a generic build/deploy failure; do not keep debugging broker connectivity until the app launches.

## Critical Anti-patterns

- Do not assume an empty `maui devflow list` means DevFlow is not installed; it only means no runtime agent is connected.
- Do not use `adb reverse` for the agent HTTP port when the host CLI must connect into the emulator; use `adb forward tcp:<port> tcp:<port>` for that direction.
- Do not kill random processes by name when resolving port conflicts. Identify the owning PID first.

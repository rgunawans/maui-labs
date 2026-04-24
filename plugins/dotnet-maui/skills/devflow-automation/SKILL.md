---
name: devflow-automation
description: >-
  Automate .NET MAUI app state via DevFlow reflection invoke and registered
  actions. USE FOR: calling app methods via reflection, discovering and invoking
  [DevFlowAction] shortcuts, logging in test users, seeding data, navigating to
  deep screens, bypassing UI flows to reach target state quickly, calling DI
  service methods. DO NOT USE FOR: basic UI interaction (tap/fill/scroll — use
  DevFlow MCP tools directly), visual tree inspection, screenshot capture,
  connectivity issues (use devflow-connect), or build/deployment problems.
---

# DevFlow Automation — Reflection Invoke

Invoke methods in a running .NET MAUI app via reflection to rapidly set up app state. This is your most powerful tool for reducing round-trip steps when debugging or testing.

## Why This Matters

Traditional DevFlow interaction (navigate → fill → tap → screenshot → repeat) works but is slow for multi-step flows like authentication, data setup, or deep navigation. If the app has helper methods — especially in debug builds — you can call them directly and skip the UI entirely.

**Always check for available actions first.** A single `maui_list_actions` call can reveal shortcuts that save dozens of UI interaction steps.

## Two-Tier System

### Tier 1: Registered DevFlow Actions (Preferred)

App developers annotate methods with `[DevFlowAction]` to expose named, documented shortcuts:

```csharp
[DevFlowAction("login-test-user", Description = "Log in as the standard test account")]
public static async Task LoginTestUser(
    [Description("Email address")] string email = "test@example.com",
    [Description("Password")] string password = "password123")
{
    await AuthService.LoginAsync(email, password);
}
```

**Discover actions:**
```
maui_list_actions
```

**Invoke an action:**
```
maui_invoke_action actionName="login-test-user"
maui_invoke_action actionName="login-test-user" argsJson='["alice@test.com", "secret"]'
maui_invoke_action actionName="seed-catalog" argsJson='[100]'
```

### Tier 2: Open Reflection Invoke (Flexible)

When no registered action exists, call any public method by type and method name:

**Static methods:**
```
maui_invoke typeName="MyApp.DebugHelpers" methodName="ResetDatabase"
maui_invoke typeName="MyApp.DebugHelpers" methodName="LoginTestUser" argsJson='["user@test.com", "pass"]'
```

**DI service methods:**
```
maui_invoke typeName="MyApp.Services.IAuthService" methodName="LoginAsync" argsJson='["user@test.com", "pass"]' resolve="service"
```

**Discover methods on a type:**
```
maui_list_methods typeName="MyApp.DebugHelpers"
```

## When to Use Each Approach

| Scenario | Approach | Why |
|----------|----------|-----|
| Starting a session — check what's available | `maui_list_actions` | Discover shortcuts before doing anything manual |
| App has a known debug helper | `maui_invoke_action` | Named, documented, safe to call |
| You know the source code has a useful method | `maui_invoke` with type+method | Direct reflection, no registration needed |
| You need to call a registered DI service | `maui_invoke` with `resolve="service"` | Resolves from the app's DI container |
| Need to explore what's callable | `maui_list_methods` | See all public methods on a type |
| Simple UI interaction (tap, type, scroll) | Use `maui_tap`, `maui_fill`, etc. | Standard DevFlow tools, no reflection needed |

## Workflow: Efficient App State Setup

### Step 1: Check for Registered Actions

```
maui_list_actions
```

Look for actions that match your goal. Common patterns:
- `login-*` — authentication shortcuts
- `seed-*` — data population
- `navigate-*` — deep navigation
- `set-*` — feature flags, configuration
- `reset-*` — state cleanup

### Step 2: Use Actions or Fall Back to Invoke

If an action exists, invoke it. If not, check the app source for helper methods and use `maui_invoke`.

### Step 3: Verify with Screenshot

After invoking, take a screenshot to confirm the app reached the expected state:

```
maui_screenshot
```

### Step 4: Continue with Standard Tools

Once the app is in the right state, use standard DevFlow tools (tree, tap, fill, etc.) for fine-grained interaction.

## Supported Parameter Types

Arguments are passed as a JSON array. These types are auto-converted:

| Type | JSON Example |
|------|-------------|
| `string` | `"hello"` |
| `bool` | `true` or `false` |
| `int`, `long`, `short`, `byte` | `42` |
| `float`, `double`, `decimal` | `3.14` |
| `enum` | `"MemberName"` (case-insensitive) |
| `string[]`, `int[]`, etc. | `["a", "b", "c"]` or `[1, 2, 3]` |
| `List<T>` | Same as arrays |
| Nullable types | `null` or the value |

## Batch Support

Invoke actions as part of a batch for complex setup sequences:

```json
{
  "actions": [
    {"action": "invoke-action", "name": "login-test-user"},
    {"action": "invoke", "typeName": "MyApp.Debug", "methodName": "NavigateTo", "args": ["settings"]},
    {"action": "tap", "elementId": "btn-advanced"}
  ]
}
```

## For App Developers: Adding DevFlow Actions

### 1. Add the Attribute

```csharp
using System.ComponentModel;
using Microsoft.Maui.DevFlow.Agent.Core;

public static class DebugHelpers
{
    [DevFlowAction("login-test-user", Description = "Log in as the standard test account")]
    public static async Task LoginTestUser(
        [Description("Email address for the test account")] string email = "test@example.com",
        [Description("Password for the test account")] string password = "password123")
    {
        await AuthService.LoginAsync(email, password);
    }
}
```

### 2. Rules

- Methods **must be `public static`** (enforced by analyzer: MAUI_DFA002)
- Parameter types must be supported primitives, enums, or arrays/lists of these (MAUI_DFA001)
- Add `[Description]` to parameters so AI agents know what to pass (MAUI_DFA004)
- Return `void`, `Task`, or `Task<T>` with a simple type (MAUI_DFA003 warns on complex returns)

### 3. Roslyn Analyzer

The `Microsoft.Maui.DevFlow.Agent.Core` NuGet package includes a Roslyn analyzer that validates `[DevFlowAction]` methods at compile time:

| Diagnostic | Severity | Description |
|-----------|----------|-------------|
| MAUI_DFA001 | Error | Unsupported parameter type |
| MAUI_DFA002 | Error | Method must be public static |
| MAUI_DFA003 | Warning | Return type may not serialize cleanly |
| MAUI_DFA004 | Info | Missing `[Description]` on parameter |

## Capabilities Detection

Check if the connected agent supports invoke:

```
maui_status
```

The capabilities response includes an `invoke` section when supported. This handles version mismatches gracefully — if the app uses an older DevFlow agent without invoke support, the tools will report this clearly.

## Common Patterns

### Authentication Bypass

```
maui_list_actions                                    # Check for login actions
maui_invoke_action actionName="login-test-user"      # Use the shortcut
maui_screenshot                                      # Verify logged-in state
```

### Data Seeding

```
maui_invoke_action actionName="seed-catalog" argsJson='[200]'
maui_invoke_action actionName="seed-orders" argsJson='[50, true]'
```

### Feature Flag Override

```
maui_invoke typeName="MyApp.FeatureFlags" methodName="Enable" argsJson='["dark-mode"]'
maui_invoke typeName="MyApp.FeatureFlags" methodName="Enable" argsJson='["experimental-ui"]'
```

### Navigate to Deep Screen

```
maui_invoke_action actionName="navigate-to" argsJson='["//settings/advanced/network"]'
```

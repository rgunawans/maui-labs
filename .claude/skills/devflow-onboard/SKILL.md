---
name: devflow-onboard
description: >-
  Add MAUI DevFlow to a .NET MAUI project with agent package references,
  MauiProgram.cs registration, Blazor WebView support, GTK variants, Central
  Package Management guidance, and verification commands. USE FOR: first-time
  DevFlow setup, reviewing what files to edit, choosing DevFlow packages, or
  continuing after `maui devflow init` installs skills. DO NOT USE FOR:
  troubleshooting an already-integrated app that cannot connect (use
  devflow-connect), generic MAUI build failures, or visual tree inspection after
  the agent is connected.
---

# DevFlow Onboard

Use this skill to add MAUI DevFlow to a project after `maui devflow init` has installed the DevFlow skills.

## Workflow

1. Find MAUI app projects in the workspace. Prefer app projects with `UseMaui`, platform TFMs such as `net*-android`/`net*-ios`/`net*-maccatalyst`/`net*-windows`, or GTK MAUI package references.
2. Determine whether each target project is standard MAUI, MAUI + Blazor WebView, GTK, or GTK + Blazor.
3. Add the correct DevFlow package references. Respect Central Package Management if `Directory.Packages.props` is present.
4. Register DevFlow in `MauiProgram.cs` inside `#if DEBUG`.
5. Build and run the app.
6. Verify with:

   ```bash
   maui devflow diagnose
   maui devflow wait
   maui devflow ui tree --depth 1
   ```

If verification fails after integration, switch to `devflow-connect`.

## Package Selection

| Project flavor | Required packages |
| --- | --- |
| Standard MAUI | `Microsoft.Maui.DevFlow.Agent` |
| MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent`, `Microsoft.Maui.DevFlow.Blazor` |
| GTK MAUI | `Microsoft.Maui.DevFlow.Agent.Gtk` |
| GTK MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent.Gtk`, `Microsoft.Maui.DevFlow.Blazor.Gtk` |

Blazor WebView indicators include a `Microsoft.AspNetCore.Components.WebView.Maui` package reference or `AddMauiBlazorWebView()` in `MauiProgram.cs`.

GTK indicators include package references such as `Maui.Gtk`, `Platform.Maui.Linux.Gtk4`, `GirCore.Gtk-4.0`, or `Platform.Maui.Linux.Gtk4.BlazorWebView`.

## Central Package Management

If the repo uses `Directory.Packages.props`, put versions there and leave project `PackageReference` entries versionless.

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview" />
```

```xml
<!-- App.csproj -->
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" />
```

If the repo does not use Central Package Management, put the version on the `PackageReference`.

## MauiProgram.cs Registration

For standard MAUI:

```csharp
using Microsoft.Maui.DevFlow.Agent;

// inside CreateMauiApp(), before return builder.Build();
#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

For MAUI + Blazor WebView:

```csharp
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

// inside CreateMauiApp(), before return builder.Build();
#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools();
#endif
```

For GTK, use the `.Gtk` namespaces and packages.

## Validation Checklist

- `MauiProgram.cs` registers DevFlow only in Debug builds.
- The app project references the package flavor that matches the target platform.
- Blazor DevFlow tools are added only when the app uses Blazor WebView.
- `dotnet build` succeeds.
- A running app appears in `maui devflow list`.
- `maui devflow ui tree --depth 1` returns a visual tree.

## References

- See `references/package-selection.md` for package/flavor details.
- See `references/mauiprogram-registration.md` for registration patterns.

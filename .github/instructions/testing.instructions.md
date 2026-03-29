---
applyTo: "**/*Tests*/**,**/*.Tests.*"
---

# Testing Guide

## Test Framework

- **xUnit** v2.9.3 with `Microsoft.NET.Test.Sdk`
- **coverlet.collector** for code coverage
- No quarantine, outerloop, or flaky test infrastructure (unlike dotnet/aspire or dotnet/maui)

## Test Projects

| Product | Test Project | Target |
|---------|-------------|--------|
| DevFlow | `src/DevFlow/Microsoft.Maui.DevFlow.Tests/` | `net10.0` |
| Maui.Client | `src/Client/Microsoft.Maui.Client.UnitTests/` | `net10.0` |

## Running Tests

```bash
# All tests
dotnet test MauiLabs.sln

# Per-product
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
dotnet test src/Client/Microsoft.Maui.Client.UnitTests/

# Specific test
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTestMethod"

# With verbose output
dotnet test --logger "console;verbosity=detailed"
```

## CI Matrix

Tests run on **macOS and Windows** in CI (`.github/workflows/_build.yml`):

- **macOS**: `./eng/common/cibuild.sh --configuration Release --prepareMachine`
- **Windows**: `eng\common\cibuild.cmd -configuration Release -prepareMachine`

Test results are uploaded as artifacts: `artifacts/TestResults/**/*.xml`

## Test Patterns

### DevFlow Tests

DevFlow tests use **real Agent.Core code** — they instantiate actual services and test behavior:

```csharp
[Fact]
public void VisualTreeWalker_FindsElementById()
{
    var walker = new VisualTreeWalker();
    // Test with real MAUI types where possible
}
```

### Maui.Client Tests

Client tests use **fake providers** to avoid real filesystem/SDK dependencies:

```csharp
[Fact]
public async Task Doctor_DetectsMissingJdk()
{
    var fakeJdk = new FakeJdkManager(installed: false);
    var doctor = new DoctorService(fakeJdk, ...);
    var result = await doctor.RunAsync();
    Assert.Contains(result.Issues, i => i.Component == "JDK");
}
```

### Naming Convention

Use descriptive names that communicate the scenario:

- `MethodName_Condition_ExpectedResult` — e.g., `ParseVersion_InvalidInput_ThrowsArgumentException`
- Or descriptive `[Fact]` — e.g., `Should_return_all_connected_agents_when_multiple_registered`

### What to Test

- **Do test**: Public API methods, edge cases, error handling, serialization/deserialization
- **Do test**: AgentClient methods (they're the public NuGet API surface)
- **Don't test**: Platform-specific overrides (require actual devices/simulators)
- **Don't test**: MCP tool registration (covered by integration at runtime)

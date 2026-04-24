using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class InvokeTests
{
	[Fact]
	public async Task ListActions_DiscoversMethods_WithDevFlowActionAttribute()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var actions = await harness.Client.ListActionsAsync();

		var json = actions;
		Assert.Equal(JsonValueKind.Object, json.ValueKind);

		var actionsArray = json.GetProperty("actions");
		Assert.Equal(JsonValueKind.Array, actionsArray.ValueKind);

		// Find our test action
		var testAction = actionsArray.EnumerateArray()
			.FirstOrDefault(a => a.GetProperty("name").GetString() == "test-greet");
		Assert.NotEqual(default, testAction);
		Assert.Equal("Returns a greeting for the given name", testAction.GetProperty("description").GetString());

		// Verify parameter metadata
		var parameters = testAction.GetProperty("parameters");
		Assert.Equal(JsonValueKind.Array, parameters.ValueKind);

		var nameParam = parameters.EnumerateArray().First();
		Assert.Equal("name", nameParam.GetProperty("name").GetString());
		Assert.Equal("string", nameParam.GetProperty("type").GetString());
		Assert.Equal("The name to greet", nameParam.GetProperty("description").GetString());
	}

	[Fact]
	public async Task InvokeAction_CallsRegisteredAction_ReturnsResult()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("test-greet",
			JsonArray(JsonElement("World")));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("Hello, World!", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithDefaultParameters_UsesDefaults()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("test-greet");

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("Hello, Friend!", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_CallsStaticMethod_ByTypeName()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.Add),
			JsonArray(JsonElement(3), JsonElement(4)));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("7", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_CallsAsyncMethod_AwaitsResult()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.GetValueAsync),
			JsonArray(JsonElement("test-value")));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("async:test-value", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_CallsVoidMethod_ReturnsOk()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		TestInvokeHelpers.LastSideEffect = null;
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.DoSideEffect),
			JsonArray(JsonElement("done")));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("done", TestInvokeHelpers.LastSideEffect);
	}

	[Fact]
	public async Task Invoke_WithBoolParameter_ConvertsCorrectly()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.IsEnabled),
			JsonArray(JsonElement(true)));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("True", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_MethodNotFound_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			"NonExistentMethod");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Invoke_TypeNotFound_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			"Some.Nonexistent.Type",
			"SomeMethod");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ListMethods_ReturnsPublicMethods_ForType()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.ListMethodsAsync(typeof(TestInvokeHelpers).FullName!);

		Assert.NotEqual(default, result);
		Assert.Equal(JsonValueKind.Object, result.ValueKind);

		var methods = result.GetProperty("methods");
		Assert.Equal(JsonValueKind.Array, methods.ValueKind);

		var methodNames = methods.EnumerateArray()
			.Select(m => m.GetProperty("name").GetString())
			.ToList();

		Assert.Contains("Greet", methodNames);
		Assert.Contains("Add", methodNames);
		Assert.Contains("GetValueAsync", methodNames);
		Assert.Contains("DoSideEffect", methodNames);
	}

	[Fact]
	public async Task InvokeAction_NotFound_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("nonexistent-action");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Invoke_WithArrayParameter_ConvertsJsonArray()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.JoinNumbers),
			JsonArray(JsonElement(new[] { 1, 2, 3 })));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("1,2,3", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithEnumParameter_ConvertsStringToEnum()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.GetPriority),
			JsonArray(JsonElement("High")));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("High", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithEnumParameter_CaseInsensitive()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.GetPriority),
			JsonArray(JsonElement("medium")));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("Medium", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithNullableParameter_PassesValue()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.FormatNullable),
			JsonArray(JsonElement(42)));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("42", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithNullableParameter_PassesNull()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.FormatNullable),
			JsonArray(JsonElement<int?>(null)));

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("null", result.ReturnValue);
	}

	// ── MCP-style integration tests ──
	// These tests exercise the AgentClient methods using the same parameter patterns
	// that the MCP InvokeTools pass (JSON string → JsonArray parsing, explicit resolve, etc.)

	[Fact]
	public async Task InvokeAction_WithMcpStyleJsonArgs_ParsesAndInvokes()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP tools receive argsJson as a raw JSON string and parse it
		var args = ParseMcpArgsJson("[\"World\"]");
		var result = await harness.Client.InvokeActionAsync("test-greet", args);

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("Hello, World!", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithMcpStyleMixedTypeArgs_ParsesCorrectly()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP tools pass mixed-type args as a JSON array string
		var args = ParseMcpArgsJson("[10, 20]");
		var result = await harness.Client.InvokeActionAsync("test-add", args);

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("30", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithMcpStyleNullArgs_UsesDefaults()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP tools pass null when argsJson is empty/whitespace
		var args = ParseMcpArgsJson(null);
		var result = await harness.Client.InvokeActionAsync("test-greet", args);

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("Hello, Friend!", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithExplicitStaticResolve_CallsStaticMethod()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP maui_invoke passes resolve: "static" explicitly
		var args = ParseMcpArgsJson("[5, 7]");
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.Add),
			args,
			resolve: "static");

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("12", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithServiceResolve_NoContainer_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP maui_invoke with resolve: "service" when no DI container is available.
		// Uses a type with an instance method to reach the DI resolution path.
		var result = await harness.Client.InvokeAsync(
			typeof(TestServiceClass).FullName!,
			nameof(TestServiceClass.GetValue),
			resolve: "service");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("DI container", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ListMethods_WithOverloadedMethods_ReturnsAllOverloads()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.ListMethodsAsync(typeof(TestInvokeHelpersWithOverloads).FullName!);

		Assert.NotEqual(default, result);
		var methods = result.GetProperty("methods");
		var concatMethods = methods.EnumerateArray()
			.Where(m => m.GetProperty("name").GetString() == "Concat")
			.ToList();

		// Should list both overloads
		Assert.Equal(2, concatMethods.Count);

		// Verify different parameter counts
		var paramCounts = concatMethods
			.Select(m => m.GetProperty("parameters").GetArrayLength())
			.OrderBy(c => c)
			.ToList();
		Assert.Equal(2, paramCounts[0]);
		Assert.Equal(3, paramCounts[1]);
	}

	[Fact]
	public async Task Invoke_OverloadedMethod_ResolvesBy2ArgCount()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// Call Concat with 2 args — should resolve to Concat(string, string)
		var args = ParseMcpArgsJson("[\"hello\", \"world\"]");
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpersWithOverloads).FullName!,
			"Concat",
			args,
			resolve: "static");

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("hello world", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_OverloadedMethod_ResolvesBy3ArgCount()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// Call Concat with 3 args — should resolve to Concat(string, string, string)
		var args = ParseMcpArgsJson("[\"a\", \"b\", \"c\"]");
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpersWithOverloads).FullName!,
			"Concat",
			args,
			resolve: "static");

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("a-b-c", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithMcpStyleComplexArgs_ConvertsTypes()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP tools pass all args as JSON — including booleans, numbers, strings mixed
		var args = ParseMcpArgsJson("[true]");
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.IsEnabled),
			args);

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("True", result.ReturnValue);
	}

	[Fact]
	public async Task Invoke_WithMcpStyleArrayArg_ConvertsJsonArray()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		// MCP tools pass arrays nested inside the outer args array
		var args = ParseMcpArgsJson("[[1, 2, 3]]");
		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.JoinNumbers),
			args);

		Assert.NotNull(result);
		Assert.True(result.Success);
		Assert.Equal("1,2,3", result.ReturnValue);
	}

	// ── Element Invoke Tests ──

	[Fact]
	public async Task InvokeElement_CallsMethodOnTreeElement_ReturnsResult()
	{
		var view = new TestInvokeView { AutomationId = "test-invoke-view" };
		using var harness = await InvokeTestHarness.CreateAsync(view);

		var result = await harness.Client.InvokeElementMethodAsync(
			"test-invoke-view",
			nameof(TestInvokeView.TestMethod),
			JsonArray(JsonElement("hello")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("result:hello", result.ReturnValue);
		Assert.Equal("test-invoke-view", result.ElementId);
	}

	[Fact]
	public async Task InvokeElement_WithMultipleArgs_ConvertsCorrectly()
	{
		var view = new TestInvokeView { AutomationId = "test-invoke-view" };
		using var harness = await InvokeTestHarness.CreateAsync(view);

		var result = await harness.Client.InvokeElementMethodAsync(
			"test-invoke-view",
			nameof(TestInvokeView.AddNumbers),
			JsonArray(JsonElement(5), JsonElement(7)));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("12", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeElement_AsyncMethod_AwaitsAndReturnsResult()
	{
		var view = new TestInvokeView { AutomationId = "test-invoke-view" };
		using var harness = await InvokeTestHarness.CreateAsync(view);

		var result = await harness.Client.InvokeElementMethodAsync(
			"test-invoke-view",
			nameof(TestInvokeView.GetValueAsync),
			JsonArray(JsonElement("test")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("async:test", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeElement_ElementNotFound_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeElementMethodAsync(
			"nonexistent-element",
			"SomeMethod");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task InvokeElement_MethodNotFoundOnElement_ReturnsError()
	{
		var view = new TestInvokeView { AutomationId = "test-invoke-view" };
		using var harness = await InvokeTestHarness.CreateAsync(view);

		var result = await harness.Client.InvokeElementMethodAsync(
			"test-invoke-view",
			"NonExistentMethod");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	// ── DI Service Resolution Tests ──

	[Fact]
	public async Task Invoke_WithServiceResolve_InstanceMethodExists_NoHandler_ReturnsContainerError()
	{
		// TestService has public instance methods, so the method resolution succeeds,
		// but DI resolution fails because TestApplication has no Handler/MauiContext.
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestService).FullName!,
			nameof(TestService.GetGreeting),
			JsonArray(JsonElement("World")),
			resolve: "service");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("Could not resolve type", result.Error, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("DI container", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Invoke_WithServiceResolve_StaticMethodNotVisible_ReturnsMethodNotFound()
	{
		// When resolve is "service", only instance methods are searched (BindingFlags.Instance).
		// Static-only methods should yield "method not found".
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeAsync(
			typeof(TestInvokeHelpers).FullName!,
			nameof(TestInvokeHelpers.Add),
			JsonArray(JsonElement(1), JsonElement(2)),
			resolve: "service");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	// TODO: Full DI service resolution test (success path) requires mocking
	// IElementHandler, IMauiContext, and IServiceProvider on TestApplication.Handler.
	// This would need: app.Handler = mockHandler where mockHandler.MauiContext.Services
	// returns an IServiceProvider that resolves the target type. Currently only the
	// error path is tested since TestApplication has no handler infrastructure.

	#region Helpers

	private static System.Text.Json.Nodes.JsonArray JsonArray(params JsonElement[] elements)
	{
		var arr = new System.Text.Json.Nodes.JsonArray();
		foreach (var e in elements)
			arr.Add(System.Text.Json.Nodes.JsonNode.Parse(e.GetRawText()));
		return arr;
	}

	private static JsonElement JsonElement(object value)
	{
		var json = JsonSerializer.Serialize(value);
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	private static JsonElement JsonElement<T>(T value)
	{
		var json = JsonSerializer.Serialize(value);
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	/// <summary>
	/// Mimics the MCP InvokeTools argsJson parsing: takes a raw JSON string
	/// and parses it into a JsonArray, exactly as the MCP tools do.
	/// </summary>
	private static System.Text.Json.Nodes.JsonArray? ParseMcpArgsJson(string? argsJson)
	{
		if (string.IsNullOrWhiteSpace(argsJson))
			return null;
		var node = System.Text.Json.Nodes.JsonNode.Parse(argsJson);
		return node as System.Text.Json.Nodes.JsonArray;
	}

	#endregion

	#region Test Harness

	private sealed class InvokeTestHarness : IDisposable
	{
		private readonly DevFlowAgentService _service;
		public AgentClient Client { get; }

		private InvokeTestHarness(DevFlowAgentService service, AgentClient client)
		{
			_service = service;
			Client = client;
		}

		public static async Task<InvokeTestHarness> CreateAsync()
			=> await CreateAsync(Array.Empty<View>());

		public static async Task<InvokeTestHarness> CreateAsync(params View[] views)
		{
			var app = new TestApplication(views);
			var service = new DevFlowAgentService(new AgentOptions { Port = GetFreePort() });
			var client = new AgentClient("localhost", service.Port);

			service.StartServerOnly(new ImmediateDispatcher());
			service.BindApp(app);

			for (var i = 0; i < 10; i++)
			{
				var status = await client.GetStatusAsync();
				if (status != null)
					return new InvokeTestHarness(service, client);
				await Task.Delay(100);
			}

			throw new InvalidOperationException("Agent did not start in time");
		}

		public void Dispose()
		{
			Client.Dispose();
			_service.Dispose();
		}

		private static int GetFreePort()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
	}

	private sealed class ImmediateDispatcher : IDispatcher
	{
		public bool IsDispatchRequired => false;
		public bool Dispatch(Action action) { action(); return true; }
		public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
		public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
	}

	private sealed class ImmediateDispatcherTimer : IDispatcherTimer
	{
		public bool IsRepeating { get; set; }
		public TimeSpan Interval { get; set; }
		public bool IsRunning { get; private set; }
		public event EventHandler? Tick { add { } remove { } }
		public void Start() => IsRunning = true;
		public void Stop() => IsRunning = false;
	}

	private sealed class TestApplication : Application, IVisualTreeElement
	{
		private readonly IReadOnlyList<IVisualTreeElement> _children;
		public TestApplication(IEnumerable<View> views) => _children = views.Cast<IVisualTreeElement>().ToArray();
		IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;
		IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
	}

	#endregion
}

#region Test Fixture Classes

/// <summary>
/// Test helper class with [DevFlowAction]-annotated methods for invoke tests.
/// These methods are discovered via assembly scanning during tests.
/// </summary>
public static class TestInvokeHelpers
{
	public static string? LastSideEffect { get; set; }

	[DevFlowAction("test-greet", Description = "Returns a greeting for the given name")]
	public static string Greet(
		[Description("The name to greet")] string name = "Friend")
		=> $"Hello, {name}!";

	[DevFlowAction("test-add", Description = "Adds two numbers")]
	public static int Add(
		[Description("First number")] int a,
		[Description("Second number")] int b)
		=> a + b;

	public static Task<string> GetValueAsync(string key)
		=> Task.FromResult($"async:{key}");

	public static void DoSideEffect(string value)
		=> LastSideEffect = value;

	public static string IsEnabled(bool enabled)
		=> enabled.ToString();

	public static string JoinNumbers(int[] numbers)
		=> string.Join(",", numbers);

	public static string GetPriority(Priority p)
		=> p.ToString();

	public static string FormatNullable(int? value)
		=> value.HasValue ? value.Value.ToString() : "null";
}

public enum Priority
{
	Low,
	Medium,
	High
}

/// <summary>
/// Test helper class with overloaded methods for overload resolution tests.
/// </summary>
public static class TestInvokeHelpersWithOverloads
{
	public static string Concat(string a, string b)
		=> $"{a} {b}";

	public static string Concat(string a, string b, string c)
		=> $"{a}-{b}-{c}";
}

/// <summary>
/// Non-static test class with instance methods for DI service resolution tests.
/// </summary>
public class TestServiceClass
{
	public string GetValue() => "service-value";
}

/// <summary>
/// Test View subclass with public instance methods for element invoke tests.
/// Added as a child of TestApplication so the tree walker can find it by AutomationId.
/// </summary>
public class TestInvokeView : View
{
	public string TestMethod(string input) => $"result:{input}";
	public int AddNumbers(int a, int b) => a + b;
	public Task<string> GetValueAsync(string key) => Task.FromResult($"async:{key}");
}

/// <summary>
/// Test service class with public instance methods for DI service resolution tests.
/// Used to verify the "resolve: service" error path when no DI container is available.
/// </summary>
public class TestService
{
	public string GetGreeting(string name) => $"Hello from service, {name}!";
}

#endregion

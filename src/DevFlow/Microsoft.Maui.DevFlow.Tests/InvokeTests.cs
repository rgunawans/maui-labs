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
		{
			var app = new TestApplication([]);
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
}

#endregion

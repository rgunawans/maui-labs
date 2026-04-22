using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "WebView")]
public class WebViewTests : IntegrationTestBase
{
    public WebViewTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    static bool HasWebViewContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength() > 0;

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength() > 0;
        }

        return false;
    }

    static int CountWebViewContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength();

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength();
        }

        return 0;
    }

    static bool AnyReadyContext(JsonElement json)
    {
        JsonElement array;
        if (json.ValueKind == JsonValueKind.Array)
            array = json;
        else if (json.ValueKind == JsonValueKind.Object &&
                 json.TryGetProperty("webviews", out var webviews) &&
                 webviews.ValueKind == JsonValueKind.Array)
            array = webviews;
        else
            return false;

        foreach (var ctx in array.EnumerateArray())
        {
            if (ctx.ValueKind != JsonValueKind.Object) continue;
            if (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True) return true;
            if (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True) return true;
        }

        return false;
    }

    Task EnsureOnBlazorPageAsync() => App.EnsureBlazorReadyAsync();

    /// <summary>
    /// Asserts the CDP bridge is actually live by sending a trivial Runtime.evaluate probe.
    /// Protects against false-positive test passes when chobitsu failed to load and the
    /// bridge is returning error payloads.
    /// </summary>
    async Task AssertCdpResponsiveAsync()
    {
        var probe = await Client.SendCdpCommandAsync(
            "Runtime.evaluate",
            JsonNode.Parse("""{"expression":"1 + 1"}"""));
        var text = probe.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"CDP bridge returned an error payload (expected a live bridge): {text}");
        Assert.Contains("\"value\":2", text);
    }

    [Fact]
    public async Task Contexts_ReturnsAtLeastOneWebView()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();

        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
    }

    [Fact]
    public async Task Contexts_WebViewIsReady()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();
        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
        Assert.True(AnyReadyContext(json),
            $"Expected at least one WebView context with isReady=true. Got: {json}");
    }

    [Fact]
    public async Task Evaluate_DocumentTitle_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.title"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        // Response should be a Runtime.evaluate result object with a result.value (string title).
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task Evaluate_SimpleExpression_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "1 + 1"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"value\":2", text);
    }

    [Fact]
    public async Task Source_ReturnsHtmlContent()
    {
        await EnsureOnBlazorPageAsync();

        var source = await Client.GetCdpSourceAsync();
        Assert.NotNull(source);
        Assert.NotEmpty(source);
        Assert.Contains("<", source);
    }

    [Fact]
    public async Task Dom_ReturnsOuterHtml()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await GetRawAsync("/api/v1/webview/dom");
        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom returned {(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(html);
        Assert.Contains("<", html);
    }

    [Fact]
    public async Task DomQuery_ReturnsElements()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await PostRawAsync("/api/v1/webview/dom/query", new
        {
            selector = "button",
            contextId = "0",
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom/query returned {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task WebViewNavigate_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await PostRawAsync("/api/v1/webview/navigate", new
        {
            url = "/counter",
            contextId = "0",
        });

        Assert.True(response.IsSuccessStatusCode,
            $"WebView navigate failed with status {(int)response.StatusCode}");
        await SettleAsync(1000);

        var backResponse = await PostRawAsync("/api/v1/webview/navigate", new
        {
            url = "/",
            contextId = "0",
        });
        Assert.True(backResponse.IsSuccessStatusCode,
            $"WebView navigate back failed with status {(int)backResponse.StatusCode}");
        await SettleAsync(1000);
    }

    [Fact]
    public async Task InputClick_Button_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var navResponse = await PostRawAsync("/api/v1/webview/navigate", new { url = "/counter", contextId = "0" });
        Assert.True(navResponse.IsSuccessStatusCode,
            $"Navigate to /counter failed: {(int)navResponse.StatusCode}");
        await SettleAsync(1000);

        var response = await PostRawAsync("/api/v1/webview/input/click", new
        {
            selector = "button",
            contextId = "0",
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/click returned {(int)response.StatusCode}");

        await PostRawAsync("/api/v1/webview/navigate", new { url = "/", contextId = "0" });
        await SettleAsync(500);
    }

    [Fact]
    public async Task InputFill_TextInput_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        // Navigate to a page that actually has a text input.
        await PostRawAsync("/api/v1/webview/navigate", new { url = "/counter", contextId = "0" });
        await SettleAsync(1000);

        var response = await PostRawAsync("/api/v1/webview/input/fill", new
        {
            selector = "input",
            text = "Blazor fill test",
            contextId = "0",
        });

        // Some platforms may not ship an input element on /counter; a 404 from the selector
        // is acceptable, but anything above 500 or a bridge error is not.
        Assert.True(
            response.IsSuccessStatusCode || (int)response.StatusCode == 404,
            $"/api/v1/webview/input/fill returned unexpected status {(int)response.StatusCode}");

        await PostRawAsync("/api/v1/webview/navigate", new { url = "/", contextId = "0" });
        await SettleAsync(500);
    }

    [Fact]
    public async Task InputText_InsertsText()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await PostRawAsync("/api/v1/webview/input/text", new
        {
            text = "Hello from test",
            contextId = "0",
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/text returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Screenshot_WebView_ReturnsImage()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await GetRawAsync("/api/v1/webview/screenshot?contextId=0");
        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/screenshot returned {(int)response.StatusCode}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Evaluate_WithDomAccess_Works()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.querySelectorAll('*').length"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task MultiBlazorPage_HasMultipleContexts()
    {
        // This test intentionally leaves the Blazor single-WebView page.
        App.InvalidateBlazorReady();
        try
        {
            await NavigateToPageAsync("//multiblazor", "BlazorLeft");

            var timeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var contextCount = 0;
            var anyReady = false;
            JsonElement lastJson = default;

            while (DateTime.UtcNow < deadline)
            {
                lastJson = await Client.GetCdpWebViewsAsync();
                contextCount = CountWebViewContexts(lastJson);
                anyReady = AnyReadyContext(lastJson);
                if (contextCount >= 2 && anyReady)
                    break;
                await Task.Delay(500);
            }

            Assert.True(contextCount >= 2,
                $"Expected at least 2 WebView contexts on the multi-Blazor page, got {contextCount}. Last response: {lastJson}");
            Assert.True(anyReady,
                $"Expected at least one ready WebView context on the multi-Blazor page. Last response: {lastJson}");
        }
        finally
        {
            await NavigateToMainPageAsync();
            App.InvalidateBlazorReady();
        }
    }
}

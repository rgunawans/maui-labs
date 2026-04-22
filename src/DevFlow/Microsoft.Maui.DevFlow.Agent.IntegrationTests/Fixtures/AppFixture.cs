using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection fixture wrapper that selects a platform-specific
/// fixture based on DEVFLOW_TEST_PLATFORM.
/// </summary>
public sealed class AppFixture : IAppFixture, IAsyncLifetime
{
    readonly IAppFixture _inner;
    readonly SemaphoreSlim _blazorReadyGate = new(1, 1);
    bool _blazorReady;

    public AppFixture()
    {
        _inner = AppFixtureFactory.Create();
    }

    public AgentClient Client => _inner.Client;
    public HttpClient Http => _inner.Http;
    public int AgentPort => _inner.AgentPort;
    public string AgentBaseUrl => _inner.AgentBaseUrl;
    public string Platform => _inner.Platform;

    public Task InitializeAsync() => _inner.InitializeAsync();
    public Task DisposeAsync() => _inner.DisposeAsync();

    /// <summary>
    /// Navigates to the //blazor Shell route once per fixture lifetime, waits for the
    /// BlazorWebView element to appear, and confirms the CDP bridge can answer a
    /// Runtime.evaluate probe. Subsequent calls return immediately after ensuring we are
    /// still on the Blazor page.
    /// </summary>
    public async Task EnsureBlazorReadyAsync()
    {
        if (_blazorReady && await IsOnBlazorPageAsync())
            return;

        await _blazorReadyGate.WaitAsync();
        try
        {
            if (!await IsOnBlazorPageAsync())
            {
                await Client.NavigateAsync("//blazor");
                await WaitForAutomationIdAsync("BlazorWebView", timeoutMs: 15000);
            }

            if (_blazorReady)
                return;

            var timeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            // Probe CDP until it answers cleanly. The bridge self-heals via SendCdpCommandAsync
            // retry, so a 1+1 probe is the simplest confirmation the chobitsu bridge is live.
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var probe = await Client.SendCdpCommandAsync(
                        "Runtime.evaluate",
                        JsonNode.Parse("""{"expression":"1 + 1"}"""));
                    var text = probe.ToString();
                    if (!text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) &&
                        text.Contains("\"value\":2", StringComparison.Ordinal))
                    {
                        _blazorReady = true;
                        return;
                    }
                }
                catch
                {
                    // Not ready yet.
                }

                await Task.Delay(500);
            }
        }
        finally
        {
            _blazorReadyGate.Release();
        }
    }

    async Task<bool> IsOnBlazorPageAsync()
    {
        try
        {
            var results = await Client.QueryAsync(automationId: "BlazorWebView");
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    async Task WaitForAutomationIdAsync(string automationId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var results = await Client.QueryAsync(automationId: automationId);
            if (results.Count > 0)
                return;
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Element with AutomationId '{automationId}' did not appear within {timeoutMs}ms.");
    }

    /// <summary>
    /// Marks the fixture as no-longer-on-Blazor-page so the next EnsureBlazorReadyAsync
    /// call will re-navigate. Call this from tests that intentionally navigate away
    /// (e.g. MultiBlazorPage_HasMultipleContexts).
    /// </summary>
    public void InvalidateBlazorReady() => _blazorReady = false;
}

[CollectionDefinition("AgentIntegration")]
public class AgentIntegrationCollection : ICollectionFixture<AppFixture>;

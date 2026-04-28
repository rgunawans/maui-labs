using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class BleMonitorTests
{
    [Fact]
    public void GetEvents_NegativeLimit_ReturnsEmpty()
    {
        var monitor = new BleMonitor();
        monitor.RecordScanResult("device-1", "Device 1", -42);

        var events = monitor.GetEvents(-1);

        Assert.Empty(events);
    }

    [Fact]
    public void GetEvents_FiltersByTypeAndAppliesLimit()
    {
        var monitor = new BleMonitor();
        monitor.RecordScanResult("scan-1", "Scan 1", -70);
        monitor.RecordConnectionStateChanged("connected-1", "Connected 1", "connected");
        monitor.RecordConnectionStateChanged("connected-2", "Connected 2", "connected");

        var events = monitor.GetEvents(1, "connected");

        var evt = Assert.Single(events);
        Assert.Equal("connected", evt.Type);
        Assert.Equal("connected-2", evt.DeviceId);
    }

    [Fact]
    public void RecordEvent_WhenBufferExceedsCapacity_TrimsOldEventsAndReportsBufferedCount()
    {
        var monitor = new BleMonitor(maxEvents: 2);
        monitor.RecordScanResult("device-1", "Device 1", -71);
        monitor.RecordScanResult("device-2", "Device 2", -72);
        monitor.RecordScanResult("device-3", "Device 3", -73);

        var events = monitor.GetEvents(10);
        var status = JsonSerializer.SerializeToElement(monitor.GetStatus());

        Assert.Equal(new[] { "device-2", "device-3" }, events.Select(e => e.DeviceId));
        Assert.Equal(2, status.GetProperty("eventCount").GetInt32());
        Assert.False(status.GetProperty("supportsScanning").GetBoolean());
    }

    [Fact]
    public void Dispose_CallsPlatformDisposeHook()
    {
        var monitor = new DisposableBleMonitor();

        monitor.Dispose();

        Assert.True(monitor.PlatformDisposed);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-an-int")]
    public async Task HandleBleEvents_InvalidLimit_ReturnsBadRequest(string limit)
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });
        var request = new HttpRequest
        {
            QueryParams = new Dictionary<string, string> { ["limit"] = limit }
        };

        var response = await InvokeHandlerAsync(service, "HandleBleEvents", request);

        Assert.Equal(400, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_limit", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task HandleBleEvents_ValidLimit_ReturnsFilteredEvents()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });
        service.Ble.RecordScanResult("scan-1", "Scan 1", -80);
        service.Ble.RecordConnectionStateChanged("connected-1", "Connected 1", "connected");
        service.Ble.RecordConnectionStateChanged("connected-2", "Connected 2", "connected");

        var request = new HttpRequest
        {
            QueryParams = new Dictionary<string, string>
            {
                ["limit"] = "1",
                ["type"] = "connected"
            }
        };

        var response = await InvokeHandlerAsync(service, "HandleBleEvents", request);

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        var evt = Assert.Single(json.GetProperty("events").EnumerateArray());
        Assert.Equal("connected", evt.GetProperty("type").GetString());
        Assert.Equal("connected-2", evt.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task HandleCapabilities_BaseBleMonitor_DoesNotAdvertiseScan()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });

        var response = await InvokeHandlerAsync(service, "HandleCapabilities", new HttpRequest());

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        var ble = json.GetProperty("ble");
        var features = ble.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetString())
            .ToArray();

        Assert.True(ble.GetProperty("supported").GetBoolean());
        Assert.Contains("status", features);
        Assert.Contains("events", features);
        Assert.Contains("stream", features);
        Assert.DoesNotContain("scan", features);
    }

    private static Task<HttpResponse> InvokeHandlerAsync(DevFlowAgentService service, string methodName, HttpRequest request)
    {
        var method = typeof(DevFlowAgentService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<HttpResponse>)method.Invoke(service, [request])!;
    }

    private sealed class DisposableBleMonitor : BleMonitor
    {
        public bool PlatformDisposed { get; private set; }

        protected override void DisposePlatform()
        {
            PlatformDisposed = true;
        }
    }
}

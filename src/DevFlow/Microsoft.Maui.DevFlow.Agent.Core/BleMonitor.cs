using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Platform-agnostic BLE event monitor with ring buffer and WebSocket subscriber support.
/// Platform-specific agents override StartScanning/StopScanning and push events via RecordEvent.
/// Apps and BLE libraries can also push events directly via the static <see cref="Instance"/> singleton.
/// </summary>
public class BleMonitor : IDisposable
{
    /// <summary>
    /// Global singleton so that app code / BLE libraries can push events without a reference to the agent.
    /// </summary>
    public static BleMonitor? Instance { get; internal set; }

    private readonly ConcurrentQueue<BleEvent> _events = new();
    private readonly List<ConcurrentQueue<string>> _subscribers = new();
    private readonly object _gate = new();
    private readonly int _maxEvents;
    private int _eventCount;
    private bool _scanning;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BleMonitor(int maxEvents = 2000)
    {
        _maxEvents = maxEvents;
    }

    public bool IsScanning
    {
        get { lock (_gate) return _scanning; }
    }

    public object GetStatus()
    {
        lock (_gate)
        {
            return new
            {
                scanning = _scanning,
                eventCount = _eventCount,
                subscribers = _subscribers.Count
            };
        }
    }

    public List<BleEvent> GetEvents(int limit = 100, string? type = null)
    {
        var all = _events.ToArray();
        IEnumerable<BleEvent> filtered = all;
        if (!string.IsNullOrEmpty(type))
            filtered = filtered.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        return filtered.TakeLast(limit).ToList();
    }

    public void ClearEvents()
    {
        while (_events.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _eventCount, 0);
    }

    /// <summary>
    /// Record a BLE event. Called by platform-specific code or app BLE libraries.
    /// </summary>
    public void RecordEvent(BleEvent evt)
    {
        evt.Timestamp ??= DateTimeOffset.UtcNow.ToString("O");

        _events.Enqueue(evt);
        Interlocked.Increment(ref _eventCount);

        // Trim ring buffer
        while (_events.Count > _maxEvents)
            _events.TryDequeue(out _);

        // Broadcast to WebSocket subscribers
        var json = JsonSerializer.Serialize(new
        {
            type = "ble_event",
            timestamp = evt.Timestamp,
            @event = evt
        }, JsonOpts);

        List<ConcurrentQueue<string>> snapshot;
        lock (_gate) { snapshot = new List<ConcurrentQueue<string>>(_subscribers); }
        foreach (var q in snapshot)
            q.Enqueue(json);
    }

    // Convenience methods for common event types

    public void RecordScanResult(string deviceId, string? deviceName, int? rssi, string? advertisementData = null)
        => RecordEvent(new BleEvent
        {
            Type = "scan_result",
            DeviceId = deviceId,
            DeviceName = deviceName,
            Rssi = rssi,
            Data = advertisementData
        });

    public void RecordConnectionStateChanged(string deviceId, string? deviceName, string state)
        => RecordEvent(new BleEvent
        {
            Type = state switch
            {
                "connected" => "connected",
                "disconnected" => "disconnected",
                _ => "connection_state_changed"
            },
            DeviceId = deviceId,
            DeviceName = deviceName,
            Data = state
        });

    public void RecordCharacteristicRead(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "characteristic_read",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordCharacteristicWrite(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex, bool withResponse = true)
        => RecordEvent(new BleEvent
        {
            Type = withResponse ? "characteristic_write" : "characteristic_write_no_response",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordNotification(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "notification",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordServiceDiscovered(string deviceId, string? deviceName, string serviceUuid)
        => RecordEvent(new BleEvent
        {
            Type = "service_discovered",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid
        });

    public void RecordDescriptorWrite(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string descriptorUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "descriptor_write",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            DescriptorUuid = descriptorUuid,
            Data = valueHex
        });

    public void RecordMtuChanged(string deviceId, string? deviceName, int mtu)
        => RecordEvent(new BleEvent
        {
            Type = "mtu_changed",
            DeviceId = deviceId,
            DeviceName = deviceName,
            Data = mtu.ToString()
        });

    // Scanning lifecycle — platform agents override StartPlatformScan/StopPlatformScan

    public string? StartScanning()
    {
        lock (_gate)
        {
            if (_scanning) return null;
            var error = StartPlatformScan();
            if (error != null) return error;
            _scanning = true;
            return null;
        }
    }

    public string? StopScanning()
    {
        lock (_gate)
        {
            if (!_scanning) return null;
            StopPlatformScan();
            _scanning = false;
            return null;
        }
    }

    /// <summary>Override in platform subclass to start native BLE scan.</summary>
    protected virtual string? StartPlatformScan() => "BLE scanning not supported on this platform";

    /// <summary>Override in platform subclass to stop native BLE scan.</summary>
    protected virtual void StopPlatformScan() { }

    // WebSocket subscriber management

    public ConcurrentQueue<string> Subscribe()
    {
        var queue = new ConcurrentQueue<string>();
        lock (_gate) { _subscribers.Add(queue); }
        return queue;
    }

    public void Unsubscribe(ConcurrentQueue<string> queue)
    {
        lock (_gate) { _subscribers.Remove(queue); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopScanning();
        if (Instance == this)
            Instance = null;
    }
}

public class BleEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("rssi")]
    public int? Rssi { get; set; }

    [JsonPropertyName("serviceUuid")]
    public string? ServiceUuid { get; set; }

    [JsonPropertyName("characteristicUuid")]
    public string? CharacteristicUuid { get; set; }

    [JsonPropertyName("descriptorUuid")]
    public string? DescriptorUuid { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

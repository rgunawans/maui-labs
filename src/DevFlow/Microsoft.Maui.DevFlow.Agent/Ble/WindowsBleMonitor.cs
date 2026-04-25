#if WINDOWS
using Microsoft.Maui.DevFlow.Agent.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace Microsoft.Maui.DevFlow.Agent.Ble;

internal sealed class WindowsBleMonitor : BleMonitor
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    private DeviceWatcher? _deviceWatcher;

    public WindowsBleMonitor() : base()
    {
        StartConnectionWatcher();
        SnapshotConnectedDevices();
    }

    protected override string? StartPlatformScan()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;
        _watcher.Start();
        return null;
    }

    protected override void StopPlatformScan()
    {
        if (_watcher != null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            try { _watcher.Stop(); }
            catch { /* may already be stopped */ }
            _watcher = null;
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var serviceUuids = args.Advertisement.ServiceUuids;
        var dataString = serviceUuids.Count > 0
            ? string.Join(",", serviceUuids)
            : null;

        RecordScanResult(
            args.BluetoothAddress.ToString("X12"),
            args.Advertisement.LocalName,
            args.RawSignalStrengthInDBm,
            dataString
        );
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            RecordEvent(new BleEvent
            {
                Type = "scan_failed",
                Data = args.Error.ToString()
            });
        }
    }

    private void StartConnectionWatcher()
    {
        try
        {
            // Watch for BLE device connection/disconnection
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _deviceWatcher = DeviceInformation.CreateWatcher(selector);
            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Removed += OnDeviceRemoved;
            _deviceWatcher.Start();
        }
        catch { /* Bluetooth may not be available */ }
    }

    private async void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device != null)
            {
                RecordConnectionStateChanged(
                    device.BluetoothAddress.ToString("X12"),
                    device.Name,
                    "connected"
                );
            }
        }
        catch { }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate info)
    {
        RecordConnectionStateChanged(info.Id, null, "disconnected");
    }

    private async void SnapshotConnectedDevices()
    {
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var deviceInfo in devices)
            {
                try
                {
                    using var device = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id);
                    if (device != null)
                    {
                        RecordConnectionStateChanged(
                            device.BluetoothAddress.ToString("X12"),
                            device.Name,
                            "connected"
                        );
                    }
                }
                catch { }
            }
        }
        catch { /* Bluetooth may not be available */ }
    }
}
#endif

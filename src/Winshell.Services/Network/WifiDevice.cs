namespace Winshell.Network;

/// <summary>A Wi-Fi adapter.</summary>
public sealed class WifiDevice : NetworkDevice
{
    private readonly DeviceAction _scan;
    private string _error = "";

    internal WifiDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
        : base(info, loop, backend)
    {
        _scan = new DeviceAction(loop.Post);
    }

    /// <summary>A scan failed.</summary>
    public event Action<string>? OperationFailed;

    /// <summary>The adapter's 802.11 mode.</summary>
    public WifiDeviceMode Mode => Info.Mode;

    /// <summary>Whether a scan is running. <see cref="Scan"/> does nothing until it ends.</summary>
    public bool Scanning => _scan.Running;

    /// <summary>Why the last scan failed. Empty while a scan runs and after one succeeds.</summary>
    public string Error => _error;

    /// <summary>Scans once and updates <see cref="NetworkDevice.Networks"/>. Needs <see cref="NetworkingService.RequestAccess"/> first.</summary>
    public void Scan()
    {
        _ = ScanAsync();
    }

    internal Task ScanAsync()
    {
        if (Removed)
            return Task.CompletedTask;
        return _scan.RunAsync(() =>
        {
            Changed(nameof(Scanning));
            Set(ref _error, "", nameof(Error));
        }, async () =>
        {
            await Backend.ScanAsync(NativeId).ConfigureAwait(false);
            return null;
        }, error =>
        {
            if (Removed)
                return;
            Changed(nameof(Scanning));
            Set(ref _error, error, nameof(Error));
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }

    internal override void Update(NetworkDeviceInfo info)
    {
        var old = Info;
        base.Update(info);
        Changed(old.Mode != info.Mode, nameof(Mode));
    }
}

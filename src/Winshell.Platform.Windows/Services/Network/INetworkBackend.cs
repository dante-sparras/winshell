namespace Winshell.Platform.Windows.Services.Network;

internal interface INetworkBackend
{
    NetworkSnapshot Current { get; }
    IDisposable Subscribe(Action<NetworkSnapshot> listener);
    void Refresh();
    Task RequestAccessAsync();
    Task ScanAsync(Guid device);
    Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password);
    bool Disconnect(Guid device);
    uint Forget(Guid device, string profileName);
}

namespace Winshell.Platform.Windows.Services.Network;

internal interface INetworkBackend
{
    NetworkSnapshot Current { get; }
    IDisposable Subscribe(Action<NetworkSnapshot> listener);
    void Refresh();
    Task RequestAccessAsync();

    /// <summary>Asks Windows to turn every Wi-Fi radio on or off. Null when they report <paramref name="enabled"/>.</summary>
    Task<string?> SetWifiEnabledAsync(bool enabled);
    Task ScanAsync(Guid device);
    Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password);
    bool Disconnect(Guid device);
    uint Forget(Guid device, string profileName);
}

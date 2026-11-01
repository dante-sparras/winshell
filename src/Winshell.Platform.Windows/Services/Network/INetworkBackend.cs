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

    /// <summary>Sets the profile's connection mode. Null when Windows accepted it or it was already that mode.</summary>
    Task<string?> SetProfileAutoConnectAsync(Guid device, string profileName, bool autoConnect);

    /// <summary>Removes the profile's key element. Null when Windows accepted it or there was no key.</summary>
    Task<string?> ClearProfilePasswordAsync(Guid device, string profileName);

    /// <summary>Connects with a saved profile. Null when Windows accepts the request.</summary>
    Task<string?> ConnectProfileAsync(Guid device, string profileName, bool hidden);
}

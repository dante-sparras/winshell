namespace Winshell.Platform.Windows.Services.Bluetooth;

internal interface IBluetoothBackend : IDisposable
{
    Task<BluetoothSnapshot> ReadAsync();

    /// <summary>The same read, stopping when <paramref name="cancellation"/> is cancelled. The default ignores the token.</summary>
    Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation) => ReadAsync();
    void StartDiscovery();
    void StopDiscovery();
    Task<string?> PairAsync(string id, CancellationToken cancellation);
    Task<string?> UnpairAsync(string id);
    Task<bool> SetAudioConnectionAsync(Guid container, bool connect);

    /// <summary>Asks Windows to turn the adapter's radio on or off. Null when it reports <paramref name="enabled"/>.</summary>
    Task<string?> SetRadioEnabledAsync(bool enabled) =>
        throw new NotSupportedException("This Bluetooth backend does not change radios.");

    /// <summary>Asks Windows to change discovery. Null when it reports <paramref name="discoverable"/>. Tests use a fake.</summary>
    Task<string?> SetDiscoverableAsync(bool discoverable) =>
        throw new NotSupportedException("This Bluetooth backend does not change discovery.");

    /// <summary>Asks Windows to change incoming connections, exposed as pairable. Null when it reports <paramref name="pairable"/>. Tests use a fake.</summary>
    Task<string?> SetPairableAsync(bool pairable) =>
        throw new NotSupportedException("This Bluetooth backend does not change incoming connections.");
}

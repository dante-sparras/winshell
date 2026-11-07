namespace Winshell.Bluetooth;

internal interface IBluetoothBackend : IDisposable
{
    /// <summary>Windows reported a change to the adapter, the radio or a device. Raised on any thread, while watching or discovering.</summary>
    event Action? Changed;

    /// <summary>Starts raising <see cref="Changed"/>. Does nothing when it already does.</summary>
    void StartWatching();

    /// <summary>Stops raising <see cref="Changed"/> for the adapter, the radio and the paired devices.</summary>
    void StopWatching();

    Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation);

    void StartDiscovery();
    void StopDiscovery();
    Task<string?> PairAsync(string id, CancellationToken cancellation);
    Task<string?> UnpairAsync(string id);
    Task<bool> SetAudioConnectionAsync(Guid container, bool connect);

    /// <summary>Asks Windows to change discovery. Null when it reports <paramref name="discoverable"/>.</summary>
    Task<string?> SetDiscoverableAsync(bool discoverable);

    /// <summary>Asks Windows to change incoming connections, exposed as pairable. Null when it reports <paramref name="pairable"/>.</summary>
    Task<string?> SetPairableAsync(bool pairable);
}

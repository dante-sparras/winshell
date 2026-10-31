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
}

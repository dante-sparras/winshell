namespace Winshell.Bluetooth;

/// <summary>
/// The default Windows Bluetooth adapter. Setting <see cref="Enabled"/> asks Windows to change its radio.
/// Setting <see cref="Discoverable"/> or <see cref="Pairable"/> asks Windows to change discovery or incoming connections.
/// </summary>
public sealed class BluetoothAdapter : ObservableObject
{
    private readonly BluetoothService _service;
    private BluetoothAdapterInfo _info;
    private string _error = "";

    internal BluetoothAdapter(BluetoothService service, BluetoothAdapterInfo info)
    {
        _service = service;
        _info = info;
    }

    /// <summary>A radio, discoverable or incoming-connection change failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    /// <summary>The adapter's Windows device id.</summary>
    public string Id => _info.Id;

    /// <summary>The adapter's name as Windows gives it, or <c>Bluetooth</c> when it gives none.</summary>
    public string Name => _info.Name;

    /// <summary>The adapter's Bluetooth address, such as <c>00:1A:7D:DA:71:13</c>.</summary>
    public string Address => _info.Address;

    /// <summary>The state of the adapter's radio.</summary>
    public BluetoothAdapterState State => _info.State;

    /// <summary>
    /// Whether the radio is on: <see cref="State"/> is <see cref="BluetoothAdapterState.On"/>. Setting it asks
    /// Windows to turn the radio on or off; the value changes when a later read sees the radio did.
    /// </summary>
    public bool Enabled
    {
        get => ReportsOn;
        set => _ = _service.SetAdapterEnabledAsync(this, value);
    }

    /// <summary>
    /// Whether other devices can find this PC. Setting it to true also turns on <see cref="Pairable"/>. The value
    /// changes when a later read sees Windows did.
    /// </summary>
    public bool Discoverable
    {
        get => _info.Discoverable;
        set => _ = _service.SetDiscoverableAsync(this, value);
    }

    /// <summary>
    /// Whether Windows accepts incoming connections. Setting it to false also turns off <see cref="Discoverable"/>. The
    /// value changes when a later read sees Windows did.
    /// </summary>
    public bool Pairable
    {
        get => _info.Pairable;
        set => _ = _service.SetPairableAsync(this, value);
    }

    /// <summary>
    /// Why the last radio, discoverable or incoming-connection change failed. Empty after one succeeds, and when the
    /// adapter's state changes.
    /// </summary>
    public string Error => _error;

    internal bool ReportsOn => _info.State == BluetoothAdapterState.On;

    internal void Report(string error)
    {
        Set(ref _error, error, nameof(Error));
        if (error.Length > 0)
            OperationFailed?.Invoke(error);
    }

    internal void Update(BluetoothAdapterInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.Discoverable != info.Discoverable, nameof(Discoverable));
        Changed(old.Pairable != info.Pairable, nameof(Pairable));
        Changed(old.State != info.State, nameof(State));
        Changed((old.State == BluetoothAdapterState.On) != ReportsOn, nameof(Enabled));
        if (old.State != info.State || old.Discoverable != info.Discoverable || old.Pairable != info.Pairable)
            Set(ref _error, "", nameof(Error));
    }
}

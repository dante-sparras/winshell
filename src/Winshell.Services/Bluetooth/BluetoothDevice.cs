namespace Winshell.Bluetooth;

/// <summary>A paired or nearby device. Endpoints that Windows groups in one device container are one device.</summary>
public sealed class BluetoothDevice : ObservableObject
{
    private readonly BluetoothService _service;
    private readonly object _pairingLock = new();
    private BluetoothDeviceInfo _info;
    private BluetoothOperation _operation;
    private string _error = "";
    private CancellationTokenSource? _pairing;
    private bool _removed;

    internal BluetoothDevice(BluetoothService service, BluetoothDeviceInfo info, BluetoothAdapter adapter)
    {
        _service = service;
        _info = info;
        Adapter = adapter;
    }

    /// <summary>An action failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    internal string Key => _info.Key;

    /// <summary>The Windows id of the device's endpoint, a paired one when there is one.</summary>
    public string Id => _info.Id;

    /// <summary>The adapter that found the device.</summary>
    public BluetoothAdapter Adapter { get; }

    /// <summary>The device's Bluetooth address, in capital letters. Empty when Windows gives none.</summary>
    public string Address => _info.Address;

    /// <summary>The device's name, or its <see cref="Address"/> when Windows gives no name.</summary>
    public string Name => _info.Name;

    /// <summary>A device-type name, such as <c>audio-headset</c> or <c>input-keyboard</c>. Empty when Windows reports no class, or a class that has no name.</summary>
    public string Icon => _info.Icon;

    /// <summary>Whether the device is paired with this PC.</summary>
    public bool Paired => _info.Paired;

    /// <summary>Whether Windows sees the device now. A paired device that is off or out of range is not.</summary>
    public bool Present => _info.Present;

    /// <summary>Whether the device is not paired and Windows says it can be, so <see cref="Pair"/> can work.</summary>
    public bool CanPair => !_info.Paired && _info.CanPair;

    /// <summary>Whether the device is connected.</summary>
    public bool Connected => _info.Connected;

    /// <summary><see cref="Connected"/>, as a state.</summary>
    public BluetoothDeviceState State =>
        _info.Connected ? BluetoothDeviceState.Connected : BluetoothDeviceState.Disconnected;

    /// <summary>Whether Windows reports the device's battery level. Only a connected device can have one.</summary>
    public bool BatteryAvailable => _info.Battery.HasValue;

    /// <summary>The battery level, from 0 to 1. 0 when <see cref="BatteryAvailable"/> is false.</summary>
    public double Battery => _info.Battery ?? 0;

    /// <summary>The action running on the device, or <see cref="BluetoothOperation.None"/>.</summary>
    public BluetoothOperation Operation => _operation;

    /// <summary>An action is running. Other actions are ignored until it ends.</summary>
    public bool Busy => _operation != BluetoothOperation.None;

    /// <summary>Whether <see cref="Pair"/> is running. <see cref="CancelPairing"/> stops it.</summary>
    public bool Pairing => _operation == BluetoothOperation.Pairing;

    /// <summary>
    /// Why the last action failed, or <c>Cancelled</c> after <see cref="CancelPairing"/>. Empty while an action runs
    /// and after one succeeds.
    /// </summary>
    public string Error => _error;

    /// <summary>Asks Windows to connect a paired audio device. Fails for other device types, which reconnect through Windows.</summary>
    public void Connect()
    {
        _ = SetConnectionAsync(true);
    }

    /// <summary>Asks Windows to disconnect a paired audio device. Fails for other device types.</summary>
    public void Disconnect()
    {
        _ = SetConnectionAsync(false);
    }

    /// <summary>Pairs the device with basic Windows pairing, which may show consent or PIN UI.</summary>
    public void Pair()
    {
        _ = PairAsync();
    }

    /// <summary>Cancels a running <see cref="Pair"/>. Its <see cref="Error"/> becomes <c>Cancelled</c>.</summary>
    public void CancelPairing()
    {
        lock (_pairingLock)
        {
            _pairing?.Cancel();
        }
    }

    /// <summary>Unpairs every paired endpoint of the device, stopping at the first failure.</summary>
    public void Unpair()
    {
        _ = UnpairAsync();
    }

    internal Task SetConnectionAsync(bool connect)
    {
        return RunAsync(connect ? BluetoothOperation.Connecting : BluetoothOperation.Disconnecting, async info =>
        {
            if (!info.Paired || info.Container is not { } container)
                return "Windows connection requests require a paired Bluetooth audio device with a device container.";
            return await _service.Backend.SetAudioConnectionAsync(container, connect).ConfigureAwait(false)
                ? null
                : "Windows accepted no Bluetooth audio connection request. Other device types reconnect through Windows.";
        });
    }

    internal Task PairAsync()
    {
        return RunAsync(BluetoothOperation.Pairing, async info =>
        {
            if (info.Paired)
                return null;
            if (!info.CanPair)
                return "Windows reports that this device cannot be paired.";
            var cancellation = new CancellationTokenSource();
            lock (_pairingLock)
            {
                _pairing = cancellation;
            }

            try
            {
                return await _service.Backend.PairAsync(info.Id, cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_pairingLock)
                {
                    _pairing = null;
                    cancellation.Dispose();
                }
            }
        });
    }

    internal Task UnpairAsync()
    {
        return RunAsync(BluetoothOperation.Unpairing, async info =>
        {
            foreach (var id in info.PairedIds)
            {
                var error = await _service.Backend.UnpairAsync(id).ConfigureAwait(false);
                if (error is not null)
                    return error;
            }

            return null;
        });
    }

    private async Task RunAsync(BluetoothOperation operation, Func<BluetoothDeviceInfo, Task<string?>> action)
    {
        if (Busy || _removed)
            return;
        SetOperation(operation);
        Set(ref _error, "", nameof(Error));
        _service.Invalidate();
        string? error = null;
        try
        {
            error = await action(_info).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error = "Cancelled";
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        finally
        {
            _service.Post(() =>
            {
                _service.Invalidate();
                if (!_removed)
                {
                    SetOperation(BluetoothOperation.None);
                    Set(ref _error, error ?? "", nameof(Error));
                    if (error is not null)
                        OperationFailed?.Invoke(error);
                }

                _service.Refresh();
            });
        }
    }

    private void SetOperation(BluetoothOperation operation)
    {
        var wasPairing = Pairing;
        var wasBusy = Busy;
        if (!Set(ref _operation, operation, nameof(Operation)))
            return;
        Changed(wasBusy != Busy, nameof(Busy));
        Changed(wasPairing != Pairing, nameof(Pairing));
    }

    internal void Update(BluetoothDeviceInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Id != info.Id, nameof(Id));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Icon != info.Icon, nameof(Icon));
        Changed(old.Paired != info.Paired, nameof(Paired));
        Changed((!old.Paired && old.CanPair) != CanPair, nameof(CanPair));
        Changed(old.Present != info.Present, nameof(Present));
        Changed(old.Connected != info.Connected, nameof(Connected));
        Changed(old.Connected != info.Connected, nameof(State));
        Changed(old.Battery.HasValue != info.Battery.HasValue, nameof(BatteryAvailable));
        Changed((old.Battery ?? 0) != (info.Battery ?? 0), nameof(Battery));
    }

    internal void Remove()
    {
        _removed = true;
        CancelPairing();
    }
}

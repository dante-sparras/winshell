using System.ComponentModel;

namespace Winshell.Network;

/// <summary>A saved WLAN profile for one network. It is not a Windows connection profile, and it never holds a key.</summary>
public sealed class WifiProfile : ObservableObject
{
    private readonly Network _network;
    private readonly DeviceAction _write;
    private WifiProfileInfo _info;
    private string _actionError = "";

    internal WifiProfile(Network network, WifiProfileInfo info)
    {
        _network = network;
        _info = info;
        _write = new DeviceAction(network.Device.Loop.Post);
    }

    /// <summary>Setting <see cref="AutoConnect"/>, <see cref="ClearPassword"/> or <see cref="Forget"/> failed.</summary>
    public event Action<string>? OperationFailed;

    internal Network Network => _network;

    internal bool Hidden => _info.Hidden;

    internal bool Removed { get; private set; }

    /// <summary>The adapter's ID and the profile's name, joined by a colon.</summary>
    public string Id => _info.Id;

    /// <summary>The profile's name in Windows. Usually the SSID, but it can differ.</summary>
    public string Name => _info.Name;

    /// <summary>The SSID the profile connects to. Empty when <see cref="Loaded"/> is false.</summary>
    public string Ssid => _info.Ssid;

    /// <summary>
    /// Whether the connection mode is automatic. Setting it changes only that element, and the value stays what the
    /// last read reported until a later read shows the change.
    /// </summary>
    public bool AutoConnect
    {
        get => _info.AutoConnect;
        set
        {
            if (Removed || _write.Running || value == _info.AutoConnect)
                return;
            _ = SetAutoConnectAsync(value);
        }
    }

    /// <summary>
    /// How the profile's network is secured. <see cref="WifiSecurityType.Unknown"/> when <see cref="Loaded"/> is
    /// false.
    /// </summary>
    public WifiSecurityType Security => _info.Security;

    /// <summary>Whether the profile has a key element. The key text is not read.</summary>
    public bool HasPassword => _info.HasPassword;

    /// <summary>
    /// Whether the profile could be read. When false, only <see cref="Id"/> and <see cref="Name"/> are set.
    /// </summary>
    public bool Loaded => _info.Loaded;

    /// <summary>Why the last change failed, or else why the profile can't be read. Empty when neither did.</summary>
    public string Error => ShownError;

    private string ShownError => _actionError.Length > 0 ? _actionError : _info.Error;

    /// <summary>The profile's fields. There is no key in the result.</summary>
    public Dictionary<string, object?> Read()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = _info.Id,
            ["name"] = _info.Name,
            ["ssid"] = _info.Ssid,
            ["autoConnect"] = _info.AutoConnect,
            ["security"] = _info.Security.ToString(),
            ["hasPassword"] = _info.HasPassword,
            ["loaded"] = _info.Loaded
        };
    }

    /// <summary>Removes every key element and writes the profile back. The key text is not read.</summary>
    public void ClearPassword()
    {
        _ = ClearPasswordAsync();
    }

    /// <summary>Deletes this saved profile by its profile name.</summary>
    public void Forget()
    {
        _ = ForgetAsync();
    }

    internal Task SetAutoConnectAsync(bool value)
    {
        return WriteAsync(() =>
            _network.Device.Backend.SetProfileAutoConnectAsync(_network.Device.NativeId, _info.Name, value));
    }

    internal Task ClearPasswordAsync()
    {
        return WriteAsync(() =>
            _network.Device.Backend.ClearProfilePasswordAsync(_network.Device.NativeId, _info.Name));
    }

    internal Task ForgetAsync()
    {
        return WriteAsync(() => Task.Run(() =>
        {
            var error = _network.Device.Backend.Forget(_network.Device.NativeId, _info.Name);
            return error == 0 ? null : new Win32Exception((int)error).Message;
        }));
    }

    internal void Update(WifiProfileInfo info)
    {
        var old = _info;
        var oldError = ShownError;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Ssid != info.Ssid, nameof(Ssid));
        Changed(old.AutoConnect != info.AutoConnect, nameof(AutoConnect));
        Changed(old.Security != info.Security, nameof(Security));
        Changed(old.HasPassword != info.HasPassword, nameof(HasPassword));
        Changed(old.Loaded != info.Loaded, nameof(Loaded));
        Changed(oldError != ShownError, nameof(Error));
    }

    internal void Remove()
    {
        Removed = true;
    }

    private void SetActionError(string error)
    {
        var old = ShownError;
        _actionError = error;
        Changed(old != ShownError, nameof(Error));
    }

    private Task WriteAsync(Func<Task<string?>> action)
    {
        if (Removed)
            return Task.CompletedTask;
        return _write.RunAsync(() => SetActionError(""), action, error =>
        {
            if (Removed)
                return;
            SetActionError(error);
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }
}

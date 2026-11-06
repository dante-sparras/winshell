using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Winshell.Network;

/// <summary>A network of an adapter. Wi-Fi networks are <see cref="WifiNetwork"/> objects.</summary>
public class Network : ObservableObject
{
    private NetworkInfo _info;
    private bool _busy;
    private string _error = "";

    internal Network(NetworkDevice device, NetworkInfo info)
    {
        Device = device;
        _info = info;
    }

    /// <summary>An action failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    internal NetworkInfo Info => _info;

    internal bool Removed { get; private set; }

    /// <summary>The SSID of a Wi-Fi network. For a wired network, the connection profile's name, or the adapter's.</summary>
    public string Name => _info.Name;

    /// <summary>The adapter this network is on.</summary>
    public NetworkDevice Device { get; }

    /// <summary>Whether the adapter is connected to this network.</summary>
    public bool Connected => _info.State == ConnectionState.Connected;

    /// <summary>A saved profile matches this network. True for a wired network.</summary>
    public bool Saved => _info.Known;

    /// <summary>
    /// The adapter's connection state on this network. <see cref="ConnectionState.Disconnected"/> for every Wi-Fi
    /// network the adapter is not on.
    /// </summary>
    public ConnectionState State => _info.State;

    /// <summary>An action is running. Other calls are ignored until it ends.</summary>
    public bool Busy => _busy;

    /// <summary>Why the last action failed. Empty while an action runs and after one succeeds.</summary>
    public string Error => _error;

    /// <summary>
    /// The saved Wi-Fi profiles for this network: those whose SSID is its name, and those that can't be read whose
    /// profile name is. Empty for a wired network.
    /// </summary>
    public ObservableCollection<WifiProfile> SavedProfiles { get; } = [];

    /// <summary>Connects to this Wi-Fi network with saved credentials. Fails for a wired network.</summary>
    public void Connect()
    {
        _ = ConnectAsync(null);
    }

    /// <summary>Connects with a saved profile of this network. The next snapshot reports whether the adapter associated.</summary>
    public void ConnectWithProfile(WifiProfile? profile)
    {
        _ = ConnectProfileAsync(profile);
    }

    /// <summary>
    /// Disconnects this network's Wi-Fi adapter. Fails for a wired network, and until
    /// <see cref="NetworkingService.RequestAccess"/> is allowed.
    /// </summary>
    public void Disconnect()
    {
        _ = DisconnectAsync();
    }

    /// <summary>Deletes the saved Wi-Fi profile whose name is this SSID.</summary>
    public void Forget()
    {
        _ = ForgetAsync();
    }

    // These two block until Windows answers, so they run off the calling thread.
    internal Task DisconnectAsync()
    {
        return RunAsync(() => Task.Run(() =>
        {
            if (Device.Type != DeviceType.Wifi || !Device.Backend.Disconnect(Device.NativeId))
                throw new NotSupportedException("Windows cannot disconnect this adapter through WiFiAdapter.");
        }));
    }

    internal Task ForgetAsync()
    {
        return RunAsync(() => Task.Run(() =>
        {
            if (Device.Type != DeviceType.Wifi)
                throw new NotSupportedException("Only saved Wi-Fi profiles can be forgotten.");
            var error = Device.Backend.Forget(Device.NativeId, Name);
            if (error != 0)
                throw new Win32Exception((int)error);
        }));
    }

    internal Task ConnectAsync(string? password)
    {
        return RunAsync(async () =>
        {
            if (Device.Type != DeviceType.Wifi)
                throw new NotSupportedException("Windows manages wired connections automatically.");
            var failure = await Device.Backend.ConnectAsync(Device.NativeId, Name, password).ConfigureAwait(false);
            if (failure is { } reason)
                throw new InvalidOperationException(reason.ToString());
        });
    }

    internal Task ConnectProfileAsync(WifiProfile? profile)
    {
        return RunAsync(async () =>
        {
            if (profile is null || profile.Removed || !ReferenceEquals(profile.Network, this))
                throw new InvalidOperationException("That saved profile is not on this network.");
            if (Device.Type != DeviceType.Wifi)
                throw new NotSupportedException("Windows manages wired connections automatically.");
            var failure = await Device.Backend.ConnectProfileAsync(Device.NativeId, profile.Name, profile.Hidden)
                .ConfigureAwait(false);
            if (failure is { } message)
                throw new InvalidOperationException(message);
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || Removed)
            return;
        Set(ref _busy, true, nameof(Busy));
        Set(ref _error, "", nameof(Error));
        var error = "";
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        finally
        {
            Device.Loop.Post(() =>
            {
                if (Removed)
                    return;
                Set(ref _busy, false, nameof(Busy));
                Set(ref _error, error, nameof(Error));
                if (error.Length > 0)
                    OperationFailed?.Invoke(error);
            });
        }
    }

    internal virtual void Update(NetworkInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Known != info.Known, nameof(Saved));
        Changed(old.State != info.State, nameof(State));
        Changed(old.State == ConnectionState.Connected != Connected, nameof(Connected));
        SavedProfiles.Sync(info.SavedProfiles ?? [], p => p.Id, i => i.Id, i => new WifiProfile(this, i),
            (p, i) => p.Update(i), p => p.Remove());
    }

    internal void Remove()
    {
        Removed = true;
        foreach (var profile in SavedProfiles)
            profile.Remove();
    }
}

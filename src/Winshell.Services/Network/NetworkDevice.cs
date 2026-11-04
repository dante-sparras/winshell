using System.Collections.ObjectModel;

namespace Winshell.Network;

/// <summary>One network adapter. Each one is a <see cref="WifiDevice"/> or a <see cref="WiredDevice"/>.</summary>
public abstract class NetworkDevice : ObservableObject
{
    private NetworkDeviceInfo _info;

    internal NetworkDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
    {
        _info = info;
        Loop = loop;
        Backend = backend;
    }

    internal IEventLoop Loop { get; }

    internal INetworkBackend Backend { get; }

    internal Guid NativeId => _info.Id;

    internal NetworkDeviceInfo Info => _info;

    internal bool Removed { get; private set; }

    /// <summary>The adapter's interface GUID. <see cref="NetworkProfile.AdapterId"/> matches it.</summary>
    public string Id => _info.Id.ToString();

    /// <summary>Whether the adapter is Wi-Fi or wired.</summary>
    public DeviceType Type => _info.Type;

    /// <summary>The adapter's connection name, such as <c>Wi-Fi</c> or <c>Ethernet 2</c>.</summary>
    public string Name => _info.Name;

    /// <summary>For Wi-Fi, the networks of the last scan and the connected network, connected first, then strongest first.</summary>
    public ObservableCollection<Network> Networks { get; } = [];

    /// <summary>The MAC address, as six uppercase hex pairs joined by colons.</summary>
    public string Address => _info.Address;

    /// <summary>Whether the adapter is connected.</summary>
    public bool Connected => _info.State == ConnectionState.Connected;

    /// <summary>
    /// The adapter's connection state. A wired adapter is only <see cref="ConnectionState.Connected"/> or
    /// <see cref="ConnectionState.Disconnected"/>.
    /// </summary>
    public ConnectionState State => _info.State;

    internal virtual void Update(NetworkDeviceInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.State != info.State, nameof(State));
        Changed((old.State == ConnectionState.Connected) != Connected, nameof(Connected));
        Networks.Sync(info.Networks, n => n.Name, i => i.Name,
            i => Type == DeviceType.Wifi ? new WifiNetwork(this, i) : new Network(this, i), (n, i) => n.Update(i), n => n.Remove());
    }

    internal void Remove()
    {
        Removed = true;
        foreach (var network in Networks)
            network.Remove();
    }
}

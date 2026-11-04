namespace Winshell.Network;

/// <summary>An Ethernet adapter. Windows manages wired connections itself.</summary>
public sealed class WiredDevice : NetworkDevice
{
    internal WiredDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
        : base(info, loop, backend)
    {
    }

    /// <summary>The link speed in Mbps. 0 without a link.</summary>
    public int LinkSpeed => (int)Math.Min(int.MaxValue, Info.LinkSpeed);

    /// <summary>Whether the adapter has a link, such as a plugged-in cable.</summary>
    public bool HasLink => Info.HasLink;

    internal override void Update(NetworkDeviceInfo info)
    {
        var old = Info;
        base.Update(info);
        Changed(old.LinkSpeed != info.LinkSpeed, nameof(LinkSpeed));
        Changed(old.HasLink != info.HasLink, nameof(HasLink));
    }
}

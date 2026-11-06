namespace Winshell.Network;

/// <summary>A Windows connection profile reported by NetworkInformation, not a saved WLAN credential.</summary>
public sealed class NetworkProfile : ObservableObject
{
    private NetworkProfileInfo _info;

    internal NetworkProfile(NetworkProfileInfo info)
    {
        _info = info;
    }

    /// <summary>The adapter's ID and the profile's name, joined by a colon.</summary>
    public string Id => _info.Id;

    /// <summary>The profile's name, as Windows shows the connection. For Wi-Fi it is usually the SSID.</summary>
    public string Name => _info.Name;

    /// <summary>The <see cref="NetworkDevice.Id"/> of the adapter the profile is on.</summary>
    public string AdapterId => _info.AdapterId.ToString();

    /// <summary>What this connection can reach.</summary>
    public NetworkConnectivity Connectivity => _info.Connectivity;

    /// <summary>How this connection's data is charged.</summary>
    public NetworkCost Cost => _info.Cost;

    /// <summary>True for a fixed or variable cost, roaming, or an exceeded data limit.</summary>
    public bool Metered => IsMetered(_info);

    /// <summary>Whether the connection is roaming, outside its provider's own network.</summary>
    public bool Roaming => _info.Roaming;

    /// <summary>Whether the connection has used more data than its plan's limit.</summary>
    public bool OverDataLimit => _info.OverDataLimit;

    /// <summary>Whether Windows reports a signal for this connection. False for a wired one.</summary>
    public bool SignalAvailable => _info.SignalStrength.HasValue;

    /// <summary>The signal strength, from 0 to 1. 0 when <see cref="SignalAvailable"/> is false.</summary>
    public double SignalStrength => _info.SignalStrength ?? 0;

    private static bool IsMetered(NetworkProfileInfo info)
    {
        return info.Cost is NetworkCost.Fixed or NetworkCost.Variable || info.Roaming || info.OverDataLimit;
    }

    internal void Update(NetworkProfileInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.AdapterId != info.AdapterId, nameof(AdapterId));
        Changed(old.Connectivity != info.Connectivity, nameof(Connectivity));
        Changed(old.Cost != info.Cost, nameof(Cost));
        Changed(IsMetered(old) != IsMetered(info), nameof(Metered));
        Changed(old.Roaming != info.Roaming, nameof(Roaming));
        Changed(old.OverDataLimit != info.OverDataLimit, nameof(OverDataLimit));
        Changed(old.SignalStrength.HasValue != info.SignalStrength.HasValue, nameof(SignalAvailable));
        Changed((old.SignalStrength ?? 0) != (info.SignalStrength ?? 0), nameof(SignalStrength));
    }
}

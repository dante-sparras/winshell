
namespace Winshell.Core;

/// <summary>The Windows reads a known plugin id is allowed to call.</summary>
public enum PluginFamily
{
    /// <summary>The id has no family. It still attaches.</summary>
    None,

    /// <summary>Opening the Menu and the terminal.</summary>
    Launch,

    /// <summary>Workspace chips.</summary>
    Workspace,

    /// <summary>The glyphs plugins publish for the indicator cluster.</summary>
    Indicators,

    /// <summary>Installed input languages.</summary>
    Language,

    /// <summary>Forecast and place lookup.</summary>
    Weather,

    /// <summary>Notification icons.</summary>
    Tray,

    /// <summary>Wi-Fi and the current link.</summary>
    Network,

    /// <summary>Playback, devices, and sessions.</summary>
    Audio,

    /// <summary>The monitors and the screen brightness.</summary>
    Display,

    /// <summary>Battery and session power.</summary>
    Power,

    /// <summary>The Windows notification list for the bell.</summary>
    Notification,

    /// <summary>Running coding agents.</summary>
    Agents,

    /// <summary>Processor, graphics, and memory load.</summary>
    Usage,

    /// <summary>Battery charge, without the calls that change the session.</summary>
    Battery,

    /// <summary>Sleep, restart, and shutdown. A third-party plugin receives this only as a clone of <c>winoma.power</c>.</summary>
    PowerSession,

    /// <summary>What the Menu reads and runs, clipboard history included. A third-party plugin receives this only as a clone of <c>winoma.menu</c>.</summary>
    Menu,

    /// <summary>The bar's own settings: its screen edge.</summary>
    Bar,

    /// <summary>The reminders Windows holds, and setting, removing, and clearing them.</summary>
    Reminders,

    /// <summary>Night light: a color temperature for every monitor.</summary>
    NightLight,

    /// <summary>Whether a newer Winoma exists, and moving the Host to it. A third-party plugin receives this only when its manifest says it is a clone of <c>winoma.update</c>.</summary>
    Update,

    /// <summary>The time zones the clock's world clock page lists.</summary>
    Clock,

    /// <summary>The Host's Bluetooth radios and paired devices. Only <c>winoma.bluetooth</c> receives it.</summary>
    Bluetooth,

    /// <summary>The Tailscale program: whether the tailnet is connected, its machines, and connecting and disconnecting it. A third-party plugin receives this only when its manifest says it is a clone of <c>winoma.tailscale</c>.</summary>
    Tailscale,

    /// <summary>LocalSend: the devices on the network and sending files to them. A third-party plugin receives this only when its manifest says it is a clone of <c>winoma.share</c>.</summary>
    Share,
    /// <summary>The session Windows reports as playing, and asking it to play, pause, and skip. No other plugin may ask for it.</summary>
    Media,
    /// <summary>The crash records Windows writes, and the Crash plugin's settings. A third-party plugin receives this only when its manifest says it is a clone of <c>winoma.crash</c>.</summary>
    Crash,
}

/// <summary>A live read that runs while that plugin's panel is open.</summary>
public enum PanelPoll
{
    /// <summary>The open panel does not start a read.</summary>
    None,

    /// <summary>The audio panel is open.</summary>
    Audio,

    /// <summary>The network panel is open.</summary>
    Network,

    /// <summary>The power panel or the display panel is open.</summary>
    Power,

    /// <summary>The tray's overflow or its settings are open.</summary>
    Tray,
}

/// <summary>The live reads that run while a plugin's panel is open. One panel can own more than one.</summary>
/// <param name="Audio">The audio mix.</param>
/// <param name="Network">The network meters and list.</param>
/// <param name="Power">The monitors, their brightness, and the battery's slow facts.</param>
/// <param name="Tray">The tray icons.</param>
public readonly record struct PanelReads(bool Audio, bool Network, bool Power, bool Tray)
{
    /// <summary>The read <paramref name="poll"/> names, or none.</summary>
    /// <param name="poll">One family's panel read.</param>
    /// <returns>That read, and no others.</returns>
    public static PanelReads From(PanelPoll poll) => poll switch
    {
        PanelPoll.Audio => new(Audio: true, Network: false, Power: false, Tray: false),
        PanelPoll.Network => new(false, true, false, false),
        PanelPoll.Power => new(false, false, true, false),
        PanelPoll.Tray => new(false, false, false, true),
        _ => default,
    };

    /// <summary>This set with <paramref name="poll"/> added.</summary>
    /// <param name="poll">The read to add.</param>
    /// <returns>The set.</returns>
    public PanelReads And(PanelPoll poll) => this with
    {
        Audio = Audio || poll == PanelPoll.Audio,
        Network = Network || poll == PanelPoll.Network,
        Power = Power || poll == PanelPoll.Power,
        Tray = Tray || poll == PanelPoll.Tray,
    };
}

/// <summary>The family and panel poll for one plugin id.</summary>
/// <param name="Family">The family the id may call. <see cref="PluginFamily.None"/> when the id is unknown.</param>
/// <param name="Poll">The read that starts while this id's panel is open.</param>
public readonly record struct PluginGrant(PluginFamily Family, PanelPoll Poll);

/// <summary>Maps a manifest id to the family and panel poll that id owns.</summary>
public static class PluginGrants
{
    /// <summary>
    /// True when <paramref name="pluginId"/> is the manifest id the bar loaded.
    /// Empty and differing ids do not attach. Comparison is ordinal and case-sensitive.
    /// </summary>
    /// <param name="manifestId">The id from the plugin folder.</param>
    /// <param name="pluginId">The id the class reports.</param>
    /// <returns>True when the class may attach for that folder.</returns>
    public static bool MayAttach(string? manifestId, string? pluginId) =>
        !string.IsNullOrEmpty(manifestId) && string.Equals(manifestId, pluginId, StringComparison.Ordinal);

    /// <summary>
    /// The family and poll for <paramref name="id"/>.
    /// An unknown id has neither. Audio and network poll while their panel is open.
    /// </summary>
    /// <param name="id">A manifest id, or null when no panel is open.</param>
    /// <returns>The row for that id.</returns>
    public static PluginGrant For(string? id) => id switch
    {
        "winoma.menu" => new(PluginFamily.Menu, PanelPoll.None),
        "winoma.workspaces" => new(PluginFamily.Workspace, PanelPoll.None),
        "winoma.indicators" => new(PluginFamily.Indicators, PanelPoll.None),
        "winoma.weather" => new(PluginFamily.Weather, PanelPoll.None),
        "winoma.keyboard-layout" => new(PluginFamily.Language, PanelPoll.None),
        "winoma.tray" => new(PluginFamily.Tray, PanelPoll.Tray),
        "winoma.network" => new(PluginFamily.Network, PanelPoll.Network),
        "winoma.audio" => new(PluginFamily.Audio, PanelPoll.Audio),
        "winoma.microphone" => new(PluginFamily.Audio, PanelPoll.Audio),
        "winoma.display" => new(PluginFamily.Display, PanelPoll.Power),
        "winoma.power" => new(PluginFamily.Power, PanelPoll.Power),
        "winoma.notifications" => new(PluginFamily.Notification, PanelPoll.None),
        "winoma.agents" => new(PluginFamily.Agents, PanelPoll.None),
        "winoma.system-usage" => new(PluginFamily.Usage, PanelPoll.None),
        "winoma.bar" => new(PluginFamily.Bar, PanelPoll.None),
        "winoma.reminders" => new(PluginFamily.Reminders, PanelPoll.None),
        "winoma.nightlight" => new(PluginFamily.NightLight, PanelPoll.None),
        "winoma.update" => new(PluginFamily.Update, PanelPoll.None),
        "winoma.clock" => new(PluginFamily.Clock, PanelPoll.None),
        "winoma.bluetooth" => new(PluginFamily.Bluetooth, PanelPoll.None),
        "winoma.tailscale" => new(PluginFamily.Tailscale, PanelPoll.None),
        "winoma.share" => new(PluginFamily.Share, PanelPoll.None),
        "winoma.media" => new(PluginFamily.Media, PanelPoll.None),
        "winoma.crash" => new(PluginFamily.Crash, PanelPoll.None),
        _ => new(PluginFamily.None, PanelPoll.None),
    };

    /// <summary>
    /// The families <paramref name="manifest"/> may bind.
    /// A first-party manifest keeps <see cref="For"/>. A third-party manifest binds each public capability it names.
    /// <c>power.session</c> is included only when <see cref="PluginManifest.ClonedFrom"/> is a power plugin, and <c>menu</c> only when it is the Menu.
    /// A clone of the power plugin that names both <c>power</c> and <c>power.session</c> also gets the family the built-in binds.
    /// Brightness is not in it: that is <c>display</c>.
    /// </summary>
    /// <param name="manifest">A discovered manifest.</param>
    /// <returns>The grants to bind, and the capability names that were refused.</returns>
    public static PluginRequest Requested(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FirstParty)
        {
            return new PluginRequest([For(manifest.Id)], []);
        }

        var session = For(manifest.ClonedFrom).Family == PluginFamily.Power;
        var grants = new List<PluginGrant>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in manifest.Capabilities)
        {
            if (!seen.Add(name))
            {
                continue;
            }

            if (name.Equals("power.session", StringComparison.Ordinal))
            {
                if (session)
                {
                    grants.Add(new PluginGrant(PluginFamily.PowerSession, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive power.session");
                }

                continue;
            }

            if (name.Equals("menu", StringComparison.Ordinal))
            {
                if (For(manifest.ClonedFrom).Family == PluginFamily.Menu)
                {
                    grants.Add(new PluginGrant(PluginFamily.Menu, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive menu");
                }

                continue;
            }

            if (name.Equals("crash", StringComparison.Ordinal))
            {
                // Like update: only a clone of the built-in gets it.
                if (For(manifest.ClonedFrom).Family == PluginFamily.Crash)
                {
                    grants.Add(new PluginGrant(PluginFamily.Crash, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive crash");
                }

                continue;
            }

            if (name.Equals("update", StringComparison.Ordinal))
            {
                // Only a manifest that says it is a clone of the built-in gets it. That keeps a plugin from asking by accident and is not
                // a wall: a plugin is code, and code on this Host can run winoma update by itself.
                if (For(manifest.ClonedFrom).Family == PluginFamily.Update)
                {
                    grants.Add(new PluginGrant(PluginFamily.Update, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive update");
                }

                continue;
            }

            if (name.Equals("tailscale", StringComparison.Ordinal))
            {
                // The program is the person's own VPN. Only a clone of the built-in may ask it anything.
                if (For(manifest.ClonedFrom).Family == PluginFamily.Tailscale)
                {
                    grants.Add(new PluginGrant(PluginFamily.Tailscale, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive tailscale");
                }

                continue;
            }

            if (name.Equals("share", StringComparison.Ordinal))
            {
                // The sockets reach every device on the person's network. Only a clone of the built-in may ask for them.
                if (For(manifest.ClonedFrom).Family == PluginFamily.Share)
                {
                    grants.Add(new PluginGrant(PluginFamily.Share, PanelPoll.None));
                }
                else
                {
                    warnings.Add(manifest.Id + " does not receive share");
                }

                continue;
            }

            if (!TryPublic(name, out var grant))
            {
                warnings.Add(manifest.Id + " asks for an unknown capability " + name);
                continue;
            }

            grants.Add(grant);
        }

        // A clone of the power plugin is the built-in's own class, which binds the family whole: the battery, the session,
        // and the power modes. The two public names alone give it none of that, and its tick would throw every turn.
        if (session && seen.Contains("power") && seen.Contains("power.session"))
        {
            grants.Add(For(manifest.ClonedFrom));
        }

        return new PluginRequest(grants, warnings);
    }

    private static Dictionary<PluginFamily, Type>? _services;

    /// <summary>
    /// Installs the service interface each family binds. The product that ships the plugins does this once.
    /// <see cref="PluginFamily.None"/> is left out: it has no interface.
    /// </summary>
    /// <param name="services">Each family and the interface <c>IBindable&lt;T&gt;</c> must close over.</param>
    internal static void UseServices(IReadOnlyDictionary<PluginFamily, Type> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = new Dictionary<PluginFamily, Type>(services);
    }

    /// <summary>The service interface for <paramref name="family"/>, or null for <see cref="PluginFamily.None"/> and for a family that was not installed.</summary>
    /// <param name="family">A family a plugin may bind.</param>
    /// <returns>The interface <c>IBindable&lt;T&gt;</c> must close over.</returns>
    public static Type? Service(PluginFamily family) =>
        _services is not null && _services.TryGetValue(family, out var type) ? type : null;

    /// <summary>
    /// Names of grants <paramref name="pluginType"/> does not implement.
    /// The plugin still attaches. The host skips each named family.
    /// </summary>
    /// <param name="id">Manifest id, used in the warning.</param>
    /// <param name="grants">Families the manifest may bind.</param>
    /// <param name="pluginType">The class that was constructed.</param>
    /// <returns>One warning per family the class does not accept.</returns>
    public static IReadOnlyList<string> Unimplemented(string id, IReadOnlyList<PluginGrant> grants, Type pluginType)
    {
        ArgumentNullException.ThrowIfNull(pluginType);
        var warnings = new List<string>();
        foreach (var grant in grants)
        {
            var service = Service(grant.Family);
            if (service is null)
            {
                continue;
            }

            var bindable = typeof(IBindable<>).MakeGenericType(service);
            if (!bindable.IsAssignableFrom(pluginType))
            {
                warnings.Add(id + " does not accept " + Token(grant.Family));
            }
        }

        return warnings;
    }

    /// <summary>The capability name for <paramref name="family"/>.</summary>
    /// <param name="family">A family.</param>
    /// <returns>The manifest token, or empty for <see cref="PluginFamily.None"/>.</returns>
    public static string Token(PluginFamily family) => family switch
    {
        PluginFamily.Launch => "launch",
        PluginFamily.Workspace => "workspace",
        PluginFamily.Indicators => "indicators",
        PluginFamily.Language => "language",
        PluginFamily.Weather => "weather",
        PluginFamily.Tray => "tray",
        PluginFamily.Network => "network",
        PluginFamily.Audio => "audio",
        PluginFamily.Display => "display",
        PluginFamily.Power => "power",
        PluginFamily.Battery => "power",
        PluginFamily.PowerSession => "power.session",
        PluginFamily.Notification => "notification",
        PluginFamily.Agents => "agents",
        PluginFamily.Usage => "usage",
        PluginFamily.Menu => "menu",
        PluginFamily.Bar => "bar",
        PluginFamily.Reminders => "reminders",
        PluginFamily.NightLight => "nightlight",
        PluginFamily.Update => "update",
        PluginFamily.Clock => "clock",
        PluginFamily.Bluetooth => "bluetooth",
        PluginFamily.Tailscale => "tailscale",
        PluginFamily.Share => "share",
        PluginFamily.Media => "media",
        PluginFamily.Crash => "crash",
        _ => "",
    };

    /// <summary>The live reads <paramref name="manifest"/>'s panel starts. Every grant counts, not the first.</summary>
    /// <param name="manifest">The plugin whose panel is open.</param>
    /// <returns>The reads. None when no grant asks for one.</returns>
    public static PanelReads ReadsFor(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var grants = manifest.FirstParty ? [For(manifest.Id)] : Requested(manifest).Grants;
        var reads = default(PanelReads);
        foreach (var grant in grants)
        {
            reads = reads.And(grant.Poll);
        }

        return reads;
    }

    private static bool TryPublic(string name, out PluginGrant grant)
    {
        grant = name switch
        {
            "launch" => new(PluginFamily.Launch, PanelPoll.None),
            "workspace" => new(PluginFamily.Workspace, PanelPoll.None),
            "indicators" => new(PluginFamily.Indicators, PanelPoll.None),
            "language" => new(PluginFamily.Language, PanelPoll.None),
            "weather" => new(PluginFamily.Weather, PanelPoll.None),
            "tray" => new(PluginFamily.Tray, PanelPoll.Tray),
            "network" => new(PluginFamily.Network, PanelPoll.Network),
            "audio" => new(PluginFamily.Audio, PanelPoll.Audio),
            "display" => new(PluginFamily.Display, PanelPoll.Power),
            "power" => new(PluginFamily.Battery, PanelPoll.None),
            "notification" => new(PluginFamily.Notification, PanelPoll.None),
            "agents" => new(PluginFamily.Agents, PanelPoll.None),
            "usage" => new(PluginFamily.Usage, PanelPoll.None),
            "bar" => new(PluginFamily.Bar, PanelPoll.None),
            "reminders" => new(PluginFamily.Reminders, PanelPoll.None),
            "nightlight" => new(PluginFamily.NightLight, PanelPoll.None),
            "clock" => new(PluginFamily.Clock, PanelPoll.None),
            _ => default,
        };
        return name is "launch" or "workspace" or "indicators" or "language" or "weather" or "tray"
            or "network" or "audio" or "display" or "power" or "notification" or "agents" or "usage" or "bar" or "reminders" or "nightlight" or "clock";
    }
}

/// <summary>Capabilities a manifest may bind, and the names the host refuses.</summary>
/// <param name="Grants">Families to bind. Empty when the plugin asks for none.</param>
/// <param name="Warnings">Refused or unknown capability names.</param>
public sealed record PluginRequest(IReadOnlyList<PluginGrant> Grants, IReadOnlyList<string> Warnings);

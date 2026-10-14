namespace Winshell.Core;

/// <summary>Shared attach and face publishing for a bar widget. A user plugin extends this and references Winshell.</summary>
public abstract class BarPlugin : IWinomaPlugin
{
    private IPluginContext _context = null!;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <summary>The host context.</summary>
    protected IPluginContext Context => _context;

    /// <summary>This widget's layout slot.</summary>
    protected BarSlot Slot => _context.Slot;

    /// <inheritdoc />
    public void Attach(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Tick();
    }

    /// <inheritdoc />
    public abstract void Tick();

    /// <inheritdoc />
    public virtual TimeSpan NextTick() => TimeSpan.Zero;

    /// <inheritdoc />
    public virtual void OnGesture(WidgetGesture gesture, int tag)
    {
    }

    /// <inheritdoc />
    public virtual void OnPanel(string command, double? value)
    {
    }

    /// <inheritdoc />
    public virtual void OnText(string command, string text)
    {
    }

    /// <summary>Publishes <paramref name="face"/>. On a vertical bar the host stacks a glyph above its text.</summary>
    /// <param name="face">What the bar should draw.</param>
    protected void Publish(BarFace face) => Context.SetFace(face);
}

/// <summary>A bar widget that can call one service family.</summary>
/// <typeparam name="TServices">The family this plugin's id is allowed to call.</typeparam>
public abstract class BarPlugin<TServices> : BarPlugin, IBindable<TServices>
    where TServices : class
{
    private TServices _services = null!;

    /// <summary>The service family this widget is allowed to call.</summary>
    protected TServices Services => _services;

    /// <inheritdoc />
    public void Bind(TServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }
}

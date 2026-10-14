using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Winshell;

/// <summary>
/// Shell material behind a window. Mica is the Settings surface: Windows paints the wallpaper
/// and accent, so a custom tint is not applied. Acrylic is the flyout material and takes a tint.
/// </summary>
internal sealed class ShellBackdrop : SystemBackdrop
{
    private readonly bool _mica;
    private DesktopAcrylicController? _acrylic;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _configuration;
    private bool _tinted;

    public ShellBackdrop(bool mica = false) => _mica = mica;

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    public static bool IsMicaSupported => MicaController.IsSupported();

    public bool Light { get; set; }

    public Color Tint { get; set; }

    public float TintOpacity { get; set; } = 0.8f;

    public float LuminosityOpacity { get; set; } = 0.96f;

    public void Configure(bool light, Color tint, float tintOpacity, float luminosityOpacity)
    {
        _tinted = true;
        Light = light;
        Tint = tint;
        TintOpacity = tintOpacity;
        LuminosityOpacity = luminosityOpacity;
        Push();
    }

    /// <summary>Mica as Settings draws it. A custom tint would replace the wallpaper and accent.</summary>
    public void ConfigureSystem(bool light)
    {
        _tinted = false;
        Light = light;
        Push();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        // The default configuration follows activation. A workspace switch deactivates this window,
        // and the controller clears the brush for a frame on the way back.
        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark,
        };
        if (_mica)
        {
            _micaController = new MicaController { Kind = MicaKind.Base };
            _micaController.AddSystemBackdropTarget(connectedTarget);
            _micaController.SetSystemBackdropConfiguration(_configuration);
        }
        else
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.AddSystemBackdropTarget(connectedTarget);
            _acrylic.SetSystemBackdropConfiguration(_configuration);
        }

        Push();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _acrylic?.RemoveSystemBackdropTarget(disconnectedTarget);
        _acrylic?.Dispose();
        _acrylic = null;
        _micaController?.RemoveSystemBackdropTarget(disconnectedTarget);
        _micaController?.Dispose();
        _micaController = null;
        _configuration = null;
    }

    private void Push()
    {
        if (_configuration is not null)
        {
            _configuration.Theme = Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        }

        // Setting a tint, opacity, or fallback replaces the system Mica policy.
        if (_mica || _acrylic is null || !_tinted)
        {
            return;
        }

        _acrylic.TintColor = Tint;
        _acrylic.TintOpacity = TintOpacity;
        _acrylic.LuminosityOpacity = LuminosityOpacity;
        _acrylic.FallbackColor = Paint.Derived(255, Tint.R, Tint.G, Tint.B);
    }
}


namespace Winshell.ExplorerTaskbar;

/// <summary>What <see cref="ExplorerTaskbarService"/> asks Windows. Tests use a fake, so they never touch a taskbar.</summary>
internal interface IExplorerTaskbarBackend
{
    /// <summary>Hides the taskbar windows not yet taken. Called again, it takes one Explorer made since.</summary>
    void Hide();

    /// <summary>Puts back what <see cref="Hide"/> took.</summary>
    void Restore();

    /// <summary>Whether a taskbar window of Explorer's is hidden. With no taskbar window at all, none is.</summary>
    bool AnyHidden();

    /// <summary>Shows every hidden taskbar window and reserves its edge again.</summary>
    void RestoreHidden();
}

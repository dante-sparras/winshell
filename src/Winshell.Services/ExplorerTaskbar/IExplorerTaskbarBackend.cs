namespace Winshell.ExplorerTaskbar;

/// <summary>What <see cref="ExplorerTaskbarService"/> asks Windows. Tests use a fake, so they never touch a taskbar.</summary>
internal interface IExplorerTaskbarBackend : IDisposable
{
    /// <summary>Raised on the loop's thread when Explorer may have made a taskbar window.</summary>
    event Action? TaskbarMayHaveChanged;

    /// <summary>Hides the taskbar windows not yet taken. Called again, it takes one Explorer made since.</summary>
    void Hide();

    /// <summary>Puts back what <see cref="Hide"/> took.</summary>
    void Restore();
}

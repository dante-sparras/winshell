namespace Winshell.Input;

/// <summary>
/// What <see cref="HotkeyRegistrar"/> needs of Windows: registering a chord, the process keyboard hook, and whether a
/// key is down. The registrar decides which chords are shared, when the hook is installed, and when a sequence
/// expires. <see cref="ChordPressed"/> is raised on the loop's thread. The hook callback runs on the hook thread.
/// </summary>
internal interface IHotkeyBackend : IDisposable
{
    /// <summary>A registered chord was pressed. Raised on the loop's thread.</summary>
    event Action<OsChord>? ChordPressed;

    /// <summary>
    /// Registers <paramref name="chord"/> with Windows, or says why it would not. A chord already registered here is
    /// active.
    /// </summary>
    HotkeyClaim Register(HotkeyChord chord);

    /// <summary>Gives a chord back to Windows. A chord this did not register is ignored.</summary>
    void Unregister(OsChord chord);

    /// <summary>
    /// Installs this process's keyboard hook. False when Windows refused. Calling it again while this hook is
    /// installed leaves that hook in place.
    /// </summary>
    /// <param name="onKey">
    /// Runs on the hook thread with the key, whether it came up, and the modifiers Windows sees held. True swallows
    /// the key.
    /// </param>
    /// <param name="onLost">Runs on the hook thread when that thread ends without <see cref="RemoveHook"/>.</param>
    bool InstallHook(Func<int, bool, HotkeyModifiers, bool> onKey, Action onLost);

    /// <summary>Removes the hook this installed, if it is installed.</summary>
    void RemoveHook();

    /// <summary>Whether the hook this installed is in place.</summary>
    bool IsHookInstalled { get; }

    /// <summary>Whether Windows reports the key down.</summary>
    bool IsKeyDown(int virtualKey);
}

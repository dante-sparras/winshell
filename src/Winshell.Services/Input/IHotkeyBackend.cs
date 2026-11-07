namespace Winshell.Input;

/// <summary>
/// What <see cref="HotkeyRegistrar"/> needs of Windows: registering a chord, the process keyboard hook, whether a key
/// is down, and the masking key. The registrar decides which chords are shared, when the hook is installed, and when a
/// sequence expires. Its calls come on the loop's thread unless a member says otherwise, and so do
/// <see cref="ChordPressed"/> and a lost hook.
/// </summary>
internal interface IHotkeyBackend : IDisposable
{
    /// <summary>A registered chord was pressed.</summary>
    event Action<OsChord>? ChordPressed;

    /// <summary>Registers a chord that is not registered here, or says why Windows would not.</summary>
    HotkeyClaim Register(HotkeyChord chord);

    /// <summary>Gives a chord back to Windows. A chord this did not register is ignored.</summary>
    void Unregister(OsChord chord);

    /// <summary>
    /// Installs this process's keyboard hook, or leaves the one this installed in place. False when the hook is not
    /// installed: Windows refused, or another hook in the process holds it.
    /// </summary>
    /// <param name="onKey">
    /// Runs on the hook thread with the key and whether it came up. True swallows the key. It does not see the
    /// masking key.
    /// </param>
    /// <param name="onLost">
    /// Runs when the hook thread ends without <see cref="RemoveHook"/>. Windows can also remove the hook and leave the
    /// thread running, and then this does not run.
    /// </param>
    bool InstallHook(Func<int, bool, bool> onKey, Action onLost);

    /// <summary>Removes the hook this installed, if it is installed.</summary>
    void RemoveHook();

    /// <summary>Whether Windows reports the key down. Any thread.</summary>
    bool IsKeyDown(int virtualKey);

    /// <summary>
    /// Injects a key nothing is assigned to. Sent while the Windows key is down, it uses that press so Start does not
    /// open. Any thread.
    /// </summary>
    void SendMask();
}

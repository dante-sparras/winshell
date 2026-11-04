namespace Winshell.DefaultShell.Taskbar;

/// <summary>One Start hotkey for the shell. An open Start closes. Otherwise the main taskbar's Start opens.</summary>
public static class StartHotkey
{
    public static void Toggle(IReadOnlyList<StartPopup> bars, int main)
    {
        foreach (var bar in bars)
        {
            if (!bar.IsOpen)
                continue;
            bar.Close();
            return;
        }
        if ((uint)main < (uint)bars.Count)
            bars[main].Open();
    }
}

using System.Runtime.InteropServices;
using System.Text;
using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows;

internal enum ImeActionKind
{
    None,
    Cancel,
    Swallow,
    Commit,
    Preedit,
    Insert,
}

internal readonly record struct ImeAction(ImeActionKind Kind, string Text);

/// <summary>
/// IMM32 composition for one window. A result string commits once, and the WM_IME_CHAR messages that repeat
/// it are swallowed. A WM_IME_CHAR with no result string still inserts. The pending result survives
/// end-composition, because the characters can arrive after it, and the next key clears it.
/// </summary>
internal sealed class ImeSession
{
    public const uint WmImeStartComposition = 0x010D;
    public const uint WmImeEndComposition = 0x010E;
    public const uint WmImeComposition = 0x010F;
    public const uint WmImeSetContext = 0x0281;
    public const uint WmImeNotify = 0x0282;
    public const uint WmImeChar = 0x0286;
    public const int GcsCompStr = 0x0008;
    public const int GcsResultStr = 0x0800;
    public const int ImnOpenCandidate = 5;
    public const int ImnChangeCandidate = 3;
    public const int CfsPoint = 2;
    public const int CfsCandidatePos = 0x40;

    public bool Composing { get; private set; }

    public string Preedit { get; private set; } = "";

    public string PendingResult { get; private set; } = "";

    public ImeAction OnComposition(int flags, string? text)
    {
        text ??= "";
        if (flags == 0)
            return CancelPreedit();
        if ((flags & GcsResultStr) != 0)
        {
            PendingResult = text;
            Preedit = "";
            Composing = false;
            return new ImeAction(ImeActionKind.Commit, text);
        }
        if ((flags & GcsCompStr) != 0)
        {
            Preedit = text;
            Composing = text.Length > 0;
            return new ImeAction(ImeActionKind.Preedit, text);
        }
        return new ImeAction(ImeActionKind.None, "");
    }

    /// <summary>
    /// A composition message can carry a committed string and the next preedit together, as the Korean IME does
    /// at a syllable boundary. The commit is first, then the preedit, so the new composition is not dropped.
    /// </summary>
    public ImeAction[] ReadComposition(int flags, string? result, string? composition)
    {
        if ((flags & (GcsResultStr | GcsCompStr)) == 0)
            return [OnComposition(flags, "")];
        var actions = new List<ImeAction>(2);
        if ((flags & GcsResultStr) != 0)
            actions.Add(OnComposition(GcsResultStr, result));
        if ((flags & GcsCompStr) != 0)
            actions.Add(OnComposition(GcsCompStr, composition));
        return [.. actions];
    }

    public ImeAction OnImeChar(char unit)
    {
        if (PendingResult.Length > 0 && PendingResult[0] == unit)
        {
            PendingResult = PendingResult[1..];
            return new ImeAction(ImeActionKind.Swallow, "");
        }
        return new ImeAction(ImeActionKind.Insert, unit.ToString());
    }

    public ImeAction OnEnd() => CancelPreedit();

    public void ClearPending() => PendingResult = "";

    /// <summary>Clears the bit that would show the default composition window. The candidate window stays.</summary>
    public static nint HideDefaultCompositionWindow(nint lParam) => (nint)((long)lParam & ~0x80000000L);

    private ImeAction CancelPreedit()
    {
        if (Preedit.Length == 0 && !Composing)
            return new ImeAction(ImeActionKind.None, "");
        Preedit = "";
        Composing = false;
        return new ImeAction(ImeActionKind.Cancel, "");
    }
}

/// <summary>IMM32 calls that place the candidate window and read the composition string.</summary>
internal static class ImeNative
{
    public static void Place(nint hwnd, int x, int y)
    {
        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return;
        try
        {
            var comp = new CompositionForm { Style = ImeSession.CfsPoint, CurrentPos = new POINT { X = x, Y = y } };
            ImmSetCompositionWindow(himc, ref comp);
            var cand = new CandidateForm { Index = 0, Style = ImeSession.CfsCandidatePos, CurrentPos = new POINT { X = x, Y = y } };
            ImmSetCandidateWindow(himc, ref cand);
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    public static string CompositionString(nint hwnd, int index)
    {
        var himc = ImmGetContext(hwnd);
        if (himc == 0)
            return "";
        try
        {
            var bytes = ImmGetCompositionStringW(himc, index, null, 0);
            if (bytes <= 0)
                return "";
            var buffer = new byte[bytes];
            var written = ImmGetCompositionStringW(himc, index, buffer, bytes);
            if (written <= 0)
                return "";
            var count = Math.Min(written, buffer.Length);
            count -= count % 2;
            return Encoding.Unicode.GetString(buffer, 0, count).TrimEnd('\0');
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    [DllImport("imm32.dll")]
    private static extern nint ImmGetContext(nint hwnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(nint hwnd, nint himc);

    [DllImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW")]
    private static extern int ImmGetCompositionStringW(nint himc, int index, byte[]? buffer, int length);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionWindow(nint himc, ref CompositionForm form);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCandidateWindow(nint himc, ref CandidateForm form);

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public int Style;
        public POINT CurrentPos;
        public RECT Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public int Index;
        public int Style;
        public POINT CurrentPos;
        public RECT Area;
    }
}

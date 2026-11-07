using System.Globalization;
using Winshell.Native;

namespace Winshell.Diagnostics;

/// <summary>The doctor's two screen checks, App bars and DPI, judged from the facts it reads about each screen.</summary>
internal static class DoctorScreens
{
    /// <summary>A screen: its name, its bounds and its work area, in physical pixels.</summary>
    internal sealed record MonitorFacts(
        string Name,
        int Left,
        int Top,
        int Right,
        int Bottom,
        int WorkLeft,
        int WorkTop,
        int WorkRight,
        int WorkBottom,
        double Scale);

    /// <summary>A visible top-level window: its class, its bounds in physical pixels, and its program.</summary>
    internal sealed record WindowFacts(string ClassName, int Left, int Top, int Right, int Bottom, string Program);

    internal const string TaskbarClass = TaskbarWindows.PrimaryClass;
    internal const string SecondaryTaskbarClass = TaskbarWindows.SecondaryClass;
    internal const string WinshellClass = "WinshellHostWindow";

    /// <summary>How thick a window must be to be named as an app bar beside a Winshell panel; thinner ones are borders and edge helpers.</summary>
    private const int MinAppBar = 8;

    internal static DoctorCheck AppBars(IReadOnlyList<MonitorFacts> monitors, IReadOnlyList<WindowFacts> windows)
    {
        var problems = new List<string>();
        var both = new List<string>();
        foreach (var m in monitors)
        {
            var onScreen = windows
                .Where(w => w.Right > m.Left && w.Left < m.Right && w.Bottom > m.Top && w.Top < m.Bottom).ToList();
            foreach (var (edge, reserved) in new[]
                     {
                         ("top", m.WorkTop - m.Top), ("bottom", m.Bottom - m.WorkBottom), ("left", m.WorkLeft - m.Left),
                         ("right", m.Right - m.WorkRight)
                     })
            {
                if (reserved <= 0)
                    continue;
                var taskbars = onScreen.Where(w =>
                        w.ClassName is TaskbarClass or SecondaryTaskbarClass && Within(Depth(m, w, edge), reserved))
                    .ToList();
                // A Winshell panel also reserves its margins, which its window does not cover, and its zone can differ
                // from its size. A window that starts in the strip explains those pixels, but another app's window
                // in the same strip is still named, even when it overlaps the panel and nothing is left over.
                var winshell = onScreen
                    .Where(w => w.ClassName == WinshellClass && Starts(m, Depth(m, w, edge), edge, reserved)).ToList();
                var taskbar = Covered(taskbars.Select(w => Depth(m, w, edge)), reserved);
                var unexplained =
                    reserved - Covered(taskbars.Concat(winshell).Select(w => Depth(m, w, edge)), reserved);
                if (taskbar > 0 && winshell.Count > 0 && reserved > taskbar + 2)
                    both.Add($"the {edge} of {m.Name}");
                if (unexplained <= 2 && winshell.Count == 0)
                    continue;
                var others = onScreen.Where(w =>
                        w.ClassName is not (TaskbarClass or SecondaryTaskbarClass or WinshellClass) &&
                        w.Program.Length > 0)
                    .Select(w => (w.Program, Depth: Depth(m, w, edge)))
                    .Where(w => Within(w.Depth, reserved))
                    .Select(w => (w.Program, Thickness: w.Depth.End - w.Depth.Start))
                    .Where(w => winshell.Count > 0
                        ? w.Thickness >= MinAppBar
                        : Math.Abs(w.Thickness - unexplained) <= 4)
                    .Select(w => w.Program).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (winshell.Count > 0)
                {
                    if (others.Count > 0)
                        problems.Add($"{string.Join(" or ", others)} reserves space at the {edge} of {m.Name}");
                    continue;
                }

                var who = others.Count > 0 ? string.Join(" or ", others) : "Another app";
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{who} reserves {unexplained} pixels at the {edge} of {m.Name}"));
            }
        }

        if (problems.Count > 0)
            return new DoctorCheck("appBars", "App bars", CheckStatus.Warn, string.Join("; ", problems) + ".",
                "Close that app, or turn off its option to reserve screen space. Maximized windows stop short of that edge, and a Winshell panel there sits beside it.");
        if (both.Count > 0)
            return new DoctorCheck("appBars", "App bars", CheckStatus.Warn,
                $"The Windows taskbar and a Winshell panel both reserve {string.Join(" and ", both)}, so maximized windows lose both.",
                "Turn on Automatically hide the taskbar in Settings > Personalization > Taskbar, or put the Winshell panel on another edge.");
        return new DoctorCheck("appBars", "App bars", CheckStatus.Pass, "No other app reserves an edge of a screen");
    }

    /// <summary>How far into the screen from an edge a window starts and ends.</summary>
    private static (int Start, int End) Depth(MonitorFacts m, WindowFacts w, string edge)
    {
        return edge switch
        {
            "top" => (w.Top - m.Top, w.Bottom - m.Top),
            "bottom" => (m.Bottom - w.Bottom, m.Bottom - w.Top),
            "left" => (w.Left - m.Left, w.Right - m.Left),
            _ => (m.Right - w.Right, m.Right - w.Left)
        };
    }

    /// <summary>Whether a window lies in the strip an edge reserves, as app bars stacked on one edge each do.</summary>
    private static bool Within((int Start, int End) depth, int reserved)
    {
        return depth.Start >= -2 && depth.End <= reserved + 2;
    }

    /// <summary>Whether a window starts in the strip an edge reserves and reaches less than halfway across the screen, as a panel does.</summary>
    private static bool Starts(MonitorFacts m, (int Start, int End) depth, string edge, int reserved)
    {
        return depth.Start >= -2 && depth.Start < reserved &&
               depth.End <= (edge is "top" or "bottom" ? m.Bottom - m.Top : m.Right - m.Left) / 2;
    }

    /// <summary>How much of the strip the windows cover together: windows side by side on one edge count once.</summary>
    private static int Covered(IEnumerable<(int Start, int End)> depths, int reserved)
    {
        var covered = 0;
        var reach = 0;
        foreach (var (start, end) in depths.Select(d => (Start: Math.Max(0, d.Start), End: Math.Min(reserved, d.End)))
                     .Where(d => d.End > d.Start).OrderBy(d => d.Start))
        {
            var from = Math.Max(start, reach);
            if (end <= from)
                continue;
            covered += end - from;
            reach = end;
        }

        return covered;
    }

    internal static DoctorCheck Dpi(bool perMonitorV2, IReadOnlyList<string> layers,
        IReadOnlyList<MonitorFacts> monitors)
    {
        var scales = string.Join(", ",
            monitors.Select(m =>
                string.Create(CultureInfo.InvariantCulture, $"{m.Name} at {Math.Round(m.Scale * 100)}%")));
        var flags = layers.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(f => f != "~")
            .ToList();
        const string HighDpi =
            "Right-click winshell.exe, choose Properties > Compatibility > Change high DPI settings, and clear";
        var unaware = flags.Where(f => f is "DPIUNAWARE" or "GDIDPISCALING").ToList();
        if (!perMonitorV2 || unaware.Count > 0)
        {
            var cause = unaware.Count > 0
                ? $"Windows overrides how Winshell scales ({string.Join(", ", unaware)})"
                : "Winshell is not per-monitor DPI aware";
            return new DoctorCheck("dpi", "DPI", CheckStatus.Fail,
                $"{cause}, so it draws blurry or the wrong size. {scales}",
                $"{HighDpi} Override high DPI scaling behavior.");
        }

        var messages = new List<string>();
        var fixes = new List<string>();
        // "Scaling performed by: Application" and "Program DPI": Winshell already scales itself on each screen.
        var highDpi = flags
            .Where(f => f is "HIGHDPIAWARE" or "PERPROCESSSYSTEMDPIFORCEON" or "PERPROCESSSYSTEMDPIFORCEOFF").ToList();
        if (highDpi.Count > 0)
        {
            messages.Add(
                $"Windows keeps high DPI settings for winshell.exe ({string.Join(", ", highDpi)}). Winshell scales itself on each screen, so they can only get in its way.");
            var boxes = new List<string>();
            if (highDpi.Contains("HIGHDPIAWARE"))
                boxes.Add("Override high DPI scaling behavior");
            if (highDpi.Any(f => f.StartsWith("PERPROCESSSYSTEMDPI", StringComparison.Ordinal)))
                boxes.Add("Use this setting to fix scaling problems for this program instead of the one in Settings");
            fixes.Add($"{HighDpi} {string.Join(" and ", boxes)}.");
        }

        if (flags.Contains("RUNASADMIN"))
        {
            messages.Add(
                "Windows always starts winshell.exe as administrator, so it asks first, and skips it when you sign in.");
            fixes.Add(
                "Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program as an administrator.");
        }

        var other = flags.Except(highDpi).Where(f => f != "RUNASADMIN").ToList();
        if (other.Count > 0)
        {
            messages.Add($"Winshell runs in compatibility mode ({string.Join(", ", other)}).");
            fixes.Add(
                "Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program in compatibility mode.");
        }

        if (messages.Count > 0)
            return new DoctorCheck("dpi", "DPI", CheckStatus.Warn, $"{string.Join(" ", messages)} {scales}",
                string.Join(" ", fixes));
        return new DoctorCheck("dpi", "DPI", CheckStatus.Pass, $"Per-monitor aware. {scales}");
    }
}

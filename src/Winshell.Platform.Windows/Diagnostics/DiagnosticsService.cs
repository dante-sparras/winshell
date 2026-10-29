using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Diagnostics;

/// <summary>
/// The <c>Diagnostics</c> singleton: runs the checks <c>winshell doctor</c> prints, for Winshell Settings' About page.
/// The checks run off the loop and only read. Without <c>winshell.exe</c> there is no doctor, and
/// <see cref="Available"/> is false.
/// </summary>
public sealed class DiagnosticsService : QObject
{
    private readonly IDoctor? _doctor;
    private bool _running;
    private string _summary = "";

    internal DiagnosticsService(QmlEngine engine)
    {
        Engine = engine;
        _doctor = engine.GetService<IDoctor>();
        Checks = new ObjectModel { Engine = engine };
        Checks.SetOwner(this);
    }

    [Qml] public bool Available => _doctor is not null;

    [Qml] public bool Running { get { Track(); return _running; } }

    /// <summary>The checks of the last run, each a <see cref="DiagnosticCheck"/>, in the order the doctor prints them.</summary>
    [Qml] public ObjectModel Checks { get; }

    /// <summary>The last run counted, such as "8 passed, 2 warnings, 0 failed."; empty before the first.</summary>
    [Qml] public string Summary { get { Track(); return _summary; } }

    [Qml]
    public void Run()
    {
        if (_doctor is not { } doctor || _running || Engine is not { } engine)
            return;
        SetField(ref _running, true, "running");
        _ = Task.Run(() => Attempt(doctor)).ContinueWith(t => engine.Loop.Post(() => Show(t.Result)), TaskScheduler.Default);
    }

    private static IReadOnlyList<DoctorCheck> Attempt(IDoctor doctor)
    {
        try
        {
            return doctor.Run();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return [new DoctorCheck("doctor", "Diagnostics", CheckStatus.Fail, $"Could not run: {e.GetType().Name}: {e.Message}", $"Report it at {Doctor.BugReports}.")];
        }
    }

    private void Show(IReadOnlyList<DoctorCheck> checks)
    {
        if (IsDestroyed)
            return;
        var old = Checks.Rows.OfType<QObject>().ToList();
        var shown = checks.Select(c =>
        {
            var row = new DiagnosticCheck(c) { Engine = Engine };
            row.SetOwner(this);
            return (object?)row;
        }).ToList();
        Checks.Set(shown);
        foreach (var row in old)
            row.Destroy();
        SetField(ref _summary, Doctor.Summary(checks), "summary");
        SetField(ref _running, false, "running");
    }
}

/// <summary>One check of a diagnostics run, as QML reads it.</summary>
public sealed class DiagnosticCheck(DoctorCheck check) : QObject
{
    [Qml] public string Id => check.Id;

    [Qml] public string Title => check.Title;

    /// <summary><c>pass</c>, <c>warn</c> or <c>fail</c>.</summary>
    [Qml] public string Status => Doctor.StatusName(check.Status);

    [Qml] public string Message => check.Message;

    /// <summary>How to fix what is wrong; empty when nothing is.</summary>
    [Qml] public string Fix => check.Fix ?? "";
}

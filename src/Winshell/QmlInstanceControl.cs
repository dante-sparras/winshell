using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell;

/// <summary>The control pipe's way into a shell made of QML.</summary>
internal sealed class QmlInstanceControl(Func<ShellHost> host, Action quit) : IInstanceControl
{
    public IIpcTargets Ipc => host().Ipc;

    public string? Reload(bool hard) => host().TryReload(hard) ? null : "The configuration did not load. Run winshell log to see why.";

    public void Quit() => quit();

    public string Describe(Exception e) => Log.Describe(e, "ipc");
}

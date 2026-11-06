namespace Winshell.Tests;

/// <summary>
/// Builds <see cref="ProbeShell"/> as a class fixture, before the class's first test. The build takes minutes on a busy
/// machine. Inside a test it would spend that test's deadline, and the deadline of every test waiting for it.
/// </summary>
public sealed class ProbeBuild
{
    public ProbeBuild()
    {
        ProbeShell.Ensure();
    }
}

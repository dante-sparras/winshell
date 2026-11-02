namespace Winshell.Tests;

/// <summary>
/// Tests that start the real default shell, one at a time, so a desktop has one extra taskbar at most. A test that only
/// cares about instances starts <see cref="ProbeShell"/>, which has no windows.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DefaultShellCollection
{
    public const string Name = "The default shell";
}

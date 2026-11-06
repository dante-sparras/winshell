namespace Winshell.Services.Tests;

/// <summary>
/// Tests that install the process keyboard hook. A process has one, so they must not run beside each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class KeyboardHookCollection
{
    public const string Name = "The keyboard hook";
}

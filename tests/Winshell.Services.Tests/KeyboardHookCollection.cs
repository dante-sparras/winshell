namespace Winshell.Services.Tests;

/// <summary>
/// Tests of the keyboard hook. A process has one, so a class that removes a leaked hook after each test must not run
/// beside another that is using it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class KeyboardHookCollection
{
    public const string Name = "The keyboard hook";
}

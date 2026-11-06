namespace Winshell.Services.Tests;

/// <summary>
/// Tests that publish an instance or record a clean exit. This process has one instance list and one last-exit
/// file, and two live rows with its process id collapse to one, so these tests run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InstanceRunCollection
{
    public const string Name = "One process's instances";
}

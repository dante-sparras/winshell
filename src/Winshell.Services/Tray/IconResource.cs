namespace Winshell.Tray;

/// <summary>An icon resource in a program or library: a numeric id, or a name when the id is 0.</summary>
internal sealed record IconResource(string Module, ushort Id, string Name);

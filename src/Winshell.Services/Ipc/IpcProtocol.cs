namespace Winshell;

/// <summary>
/// The version of the lines the control pipe carries. A shell can outlive an update of the CLI, so each side names its
/// protocol on every line. A side with no number sent no <c>protocol</c> field, and reads past one, which makes the
/// field safe for both: a refusal is an ordinary failed response, and every client since the first prints those.
/// </summary>
internal static class IpcProtocol
{
    /// <summary>What this build speaks. Raise it when a side from before could not read a line.</summary>
    public const int Current = 1;

    /// <summary>The oldest this build still speaks. 0 is a side with no number, whose lines are the same as 1.</summary>
    public const int Oldest = 0;

    /// <summary>Why a shell speaking <paramref name="current"/> refuses a client speaking <paramref name="client"/>, or null.</summary>
    public static string? ClientRefusal(int client, int current = Current, int oldest = Oldest) =>
        client >= oldest && client <= current ? null : Mismatch(current, client);

    /// <summary>Why a client speaking <paramref name="current"/> refuses a shell speaking <paramref name="shell"/>, or null. A newer shell decides for itself.</summary>
    public static string? ShellRefusal(int shell, int current = Current, int oldest = Oldest) =>
        shell >= oldest ? null : Mismatch(shell, current);

    /// <summary>
    /// Why a shell of release <paramref name="shell"/> refuses a winshell of release <paramref name="client"/>, or null.
    /// A side that names no release is allowed, so a peer from before this check still answers. The release is
    /// major.minor.patch: a prerelease and a build id do not refuse.
    /// </summary>
    public static string? ReleaseRefusal(string? shell, string? client)
    {
        if (string.IsNullOrEmpty(shell) || string.IsNullOrEmpty(client))
            return null;
        var left = ProductVersion.Parse(shell);
        var right = ProductVersion.Parse(client);
        if (left.Major == right.Major && left.Minor == right.Minor && left.Patch == right.Patch)
            return null;
        return $"This shell is Winshell {shell} and this winshell is {client}. Use the winshell that matches, or build the shell with this one.";
    }

    private static string Mismatch(int shell, int client) =>
        $"This shell speaks control protocol {shell} and this winshell speaks {client}. Use the winshell that matches, or build the shell with this one.";
}

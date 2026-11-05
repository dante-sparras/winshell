namespace Winshell.Diagnostics;

/// <summary>How a <see cref="DoctorCheck"/> came out.</summary>
public enum CheckStatus
{
    /// <summary>Nothing is wrong.</summary>
    Pass,

    /// <summary>Winshell runs, but something may not work as expected.</summary>
    Warn,

    /// <summary>Something stops Winshell from working. <c>winshell doctor</c> exits with 1.</summary>
    Fail
}

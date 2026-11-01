namespace Winshell.Notifications;

/// <summary>The sending app's logo, as Windows returns it. Opened off the UI thread.</summary>
internal interface IAppLogoSource
{
    /// <summary>The logo, or null when Windows has none. May throw when the read fails.</summary>
    Stream? Open(CancellationToken cancellation);
}

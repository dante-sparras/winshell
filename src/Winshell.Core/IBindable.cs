namespace Winshell.Core;

/// <summary>A plugin that receives one service family when the host attaches it.</summary>
/// <typeparam name="TServices">The family this plugin's id is allowed to call.</typeparam>
public interface IBindable<TServices>
    where TServices : class
{
    /// <summary>Stores <paramref name="services"/> before <see cref="IWinomaPlugin.Attach"/>.</summary>
    /// <param name="services">The family for this plugin's id.</param>
    void Bind(TServices services);
}

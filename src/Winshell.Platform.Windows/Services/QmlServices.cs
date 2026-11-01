using System.Globalization;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services;

/// <summary>Helpers the QML modules of the Windows services share.</summary>
internal static class QmlServices
{
    /// <summary>
    /// Registers a C# enum as a QML enum type: <c>PowerDeviceState.Charging</c> and
    /// <c>PowerDeviceState.toString(state)</c>.
    /// </summary>
    public static QmlModule Enum<T>(this QmlModule module, Func<T, string> toString, string? name = null)
        where T : struct, Enum
    {
        var values = System.Enum.GetValues<T>().ToDictionary(v => v.ToString(), v => Convert.ToInt32(v, CultureInfo.InvariantCulture));
        return module.Add(new TypeEntry
        {
            Name = name ?? typeof(T).Name,
            Module = module.Name,
            ClrType = typeof(T),
            Creatable = false,
            Enums = values,
            EnumToString = v => toString((T)System.Enum.ToObject(typeof(T), v)),
        });
    }

    /// <summary>Writes a warning from a service, for actions Windows cannot do.</summary>
    public static void Warn(QObject service, string category, string message) =>
        (service.Engine?.Log ?? Log.Default).Warn(category, message);
}

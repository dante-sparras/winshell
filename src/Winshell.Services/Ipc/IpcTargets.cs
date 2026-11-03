using System.Globalization;
using System.Text;

namespace Winshell;

/// <summary>One parameter of a function or a signal <c>ipc show</c> can list.</summary>
internal sealed record IpcParameterView(string Name, string? Type);

/// <summary>A function <c>ipc show</c> can call.</summary>
internal sealed record IpcFunctionView(string Name, IReadOnlyList<IpcParameterView> Parameters, string Returns);

/// <summary>A property <c>ipc show</c> can read.</summary>
internal sealed record IpcPropertyView(string Name, string Type);

/// <summary>A signal <c>ipc wait</c> and <c>ipc listen</c> can follow.</summary>
internal sealed record IpcSignalView(string Name, IReadOnlyList<IpcParameterView> Parameters);

/// <summary>One IPC target, as <c>ipc show</c> knows it. <c>ipc show -j</c> prints these records as they are.</summary>
internal sealed record IpcTargetView(
    string Target,
    IReadOnlyList<IpcFunctionView> Functions,
    IReadOnlyList<IpcPropertyView> Properties,
    IReadOnlyList<IpcSignalView> Signals);

/// <summary>
/// A refused IPC request. A handler throws it to refuse a call: its message goes to the client as it is, where another
/// exception arrives with its type name.
/// </summary>
public sealed class IpcException(string message) : Exception(message);

/// <summary>The rules a value follows across the pipe.</summary>
internal static class IpcValues
{
    /// <summary>The most parameters a function can take.</summary>
    public const int MaxParameters = 10;

    /// <summary>Every target with its functions, properties and signals, as <c>ipc show</c> prints them.</summary>
    public static string Show(this ShellIpc ipc)
    {
        var sb = new StringBuilder();
        foreach (var target in ipc.Describe())
        {
            sb.Append("target ").Append(target.Target).Append('\n');
            foreach (var function in target.Functions)
                sb.Append("  function ").Append(function.Name).Append('(').Append(string.Join(", ", function.Parameters.Select(p => $"{p.Name}: {p.Type}"))).Append("): ").Append(function.Returns).Append('\n');
            foreach (var property in target.Properties)
                sb.Append("  property ").Append(property.Name).Append(": ").Append(property.Type).Append('\n');
            foreach (var signal in target.Signals)
                sb.Append("  signal ").Append(signal.Name).Append('(').Append(string.Join(", ", signal.Parameters.Select(p => p.Name))).Append(")\n");
        }
        return sb.ToString();
    }

    public static string WrongTypes(string target, string function) =>
        $"Function {target}.{function} has a type IPC can't pass. It takes and returns string, int, bool, real, double or color.";

    public static string WrongCount(string target, string function, int declared, int given) =>
        $"Function {target}.{function} takes {declared} argument{(declared == 1 ? "" : "s")}, {given} given";

    public static int ParseInt(string text, string name) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw new IpcException($"Argument {name} is not an int: {text}");

    public static double ParseReal(string text, string name) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new IpcException($"Argument {name} is not a real: {text}");

    public static bool ParseBool(string text, string name) => text switch
    {
        "true" => true,
        "false" => false,
        _ when int.TryParse(text, out var n) => n != 0,
        _ => throw new IpcException($"Argument {name} is not a bool: {text}"),
    };

    /// <summary>A color can come without its <c>#</c>, so a hotkey need not quote it.</summary>
    public static string ColorText(string text) => text.StartsWith('#') || !IsHex(text) ? text : "#" + text;

    private static bool IsHex(string s) => s.Length is 3 or 6 or 8 && s.All(char.IsAsciiHexDigit);
}

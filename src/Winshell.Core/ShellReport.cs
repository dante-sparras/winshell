using System.Globalization;
using System.Text.Json.Nodes;

namespace Winshell.Core;

/// <summary>One monitor's bar, as <c>winshell state</c> prints it.</summary>
/// <param name="Monitor">The monitor, starting at 0.</param>
/// <param name="Edge"><c>top</c>, <c>bottom</c>, <c>left</c>, or <c>right</c>.</param>
/// <param name="Plugins">The plugin ids on it, in bar order.</param>
public sealed record ShellBarReport(int Monitor, string Edge, IReadOnlyList<string> Plugins);

/// <summary>What <c>winshell state</c> prints: the open panel and its rows, the bars, and plugin facts.</summary>
public sealed class ShellReport
{
    /// <summary>Creates a report. Null lists are empty.</summary>
    /// <param name="panel">The open panel's plugin id, or null when none is open.</param>
    /// <param name="rows">The open panel's rows, top to bottom.</param>
    /// <param name="bars">One entry per monitor.</param>
    /// <param name="plugins">Facts keyed by plugin id. Strings, booleans, and numbers are kept.</param>
    /// <param name="extensions">Facts an extension published, keyed by its name.</param>
    public ShellReport(
        string? panel,
        IReadOnlyList<string>? rows,
        IReadOnlyList<ShellBarReport>? bars,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>>? plugins,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>>? extensions = null)
    {
        Panel = panel;
        Rows = rows ?? [];
        Bars = bars ?? [];
        Plugins = plugins ?? new Dictionary<string, IReadOnlyDictionary<string, object>>();
        Extensions = extensions ?? new Dictionary<string, IReadOnlyDictionary<string, object>>();
    }

    /// <summary>The open panel's plugin id, or null when none is open.</summary>
    public string? Panel { get; }

    /// <summary>The open panel's rows, top to bottom.</summary>
    public IReadOnlyList<string> Rows { get; }

    /// <summary>One entry per monitor that has a bar.</summary>
    public IReadOnlyList<ShellBarReport> Bars { get; }

    /// <summary>Facts keyed by plugin id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Plugins { get; }

    /// <summary>Facts an extension published, keyed by its name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Extensions { get; }

    /// <summary>One fact per line. A shell with nothing open says <c>panel none</c>.</summary>
    /// <returns>The text.</returns>
    public string Text()
    {
        var lines = new List<string> { "panel " + (string.IsNullOrEmpty(Panel) ? "none" : Panel) };
        foreach (var row in Rows)
        {
            lines.Add("row " + row);
        }

        foreach (var bar in Bars)
        {
            lines.Add("bar " + bar.Monitor.ToString(CultureInfo.InvariantCulture) + " " + bar.Edge + " " + string.Join(' ', bar.Plugins));
        }

        foreach (var (id, facts) in Plugins.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var (name, value) in facts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                lines.Add("plugin " + id + " " + name + "=" + ValueText(value));
            }
        }

        foreach (var (id, facts) in Extensions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var (name, value) in facts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                lines.Add("extension " + id + " " + name + "=" + ValueText(value));
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The same report as one JSON object.</summary>
    /// <returns>The JSON.</returns>
    public string Json()
    {
        var plugins = Map(Plugins);

        var bars = new JsonArray();
        foreach (var bar in Bars)
        {
            var pluginsOnBar = new JsonArray();
            foreach (var id in bar.Plugins)
            {
                JsonNode? plugin = JsonValue.Create(id);
                pluginsOnBar.Add(plugin);
            }

            JsonNode barNode = new JsonObject
            {
                ["monitor"] = bar.Monitor,
                ["edge"] = bar.Edge,
                ["plugins"] = pluginsOnBar,
            };
            bars.Add(barNode);
        }

        var rows = new JsonArray();
        foreach (var row in Rows)
        {
            JsonNode? line = JsonValue.Create(row);
            rows.Add(line);
        }

        var root = new JsonObject
        {
            ["panel"] = Panel is null ? null : JsonValue.Create(Panel),
            ["rows"] = rows,
            ["bars"] = bars,
            ["plugins"] = plugins,
            ["extensions"] = Map(Extensions),
        };
        return root.ToJsonString();
    }

    private static JsonObject Map(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> facts)
    {
        var map = new JsonObject();
        foreach (var (id, values) in facts.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var obj = new JsonObject();
            foreach (var (name, value) in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (Node(value) is { } node)
                {
                    obj[name] = node;
                }
            }

            map[id] = obj;
        }

        return map;
    }

    private static string ValueText(object value) => value switch
    {
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static JsonNode? Node(object? value) => value switch
    {
        bool flag => JsonValue.Create(flag),
        byte number => JsonValue.Create((int)number),
        short number => JsonValue.Create((int)number),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        float number => JsonValue.Create((double)number),
        double number => JsonValue.Create(number),
        string text => JsonValue.Create(text),
        _ => null,
    };
}

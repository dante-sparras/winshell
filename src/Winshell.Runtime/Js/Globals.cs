using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Winshell.Runtime;

/// <summary>The globals QML scripts get: <c>Qt</c>, <c>console</c>, <c>print</c>.</summary>
internal static class Globals
{
    public static void Install(JsRuntime runtime, QmlEngine qml)
    {
        var engine = runtime.Engine;
        engine.SetValue("console", MakeConsole(runtime, qml));
        engine.SetValue("print", Fn(engine, "print", args => { qml.Log.Info("", Join(runtime, args)); return JsValue.Undefined; }));
        engine.SetValue("Qt", MakeQt(runtime, qml));
        // A typo such as `widht = 5` would otherwise become a property every component can read, and a
        // script could replace Qt for the rest of the generation. Sloppy mode then drops the write.
        var global = engine.Global;
        foreach (var name in new[] { "Qt", "console", "print" })
        {
            var value = global.Get(name);
            global.DefineOwnProperty(name, new PropertyDescriptor(value, writable: false, enumerable: true, configurable: false));
        }
        global.PreventExtensions();
    }

    private static ClrFunction Fn(Engine engine, string name, Func<JsValue[], JsValue> body) =>
        new(engine, name, (_, args) => body(args));

    private static JsObject MakeConsole(JsRuntime runtime, QmlEngine qml)
    {
        var engine = runtime.Engine;
        var console = new JsObject(engine);
        var timers = new Dictionary<string, TimeSpan>();
        var counts = new Dictionary<string, int>();
        void Add(string name, LogLevel level) =>
            console.Set(name, Fn(engine, name, args => { qml.Log.Write(level, "", Join(runtime, args)); return JsValue.Undefined; }));
        Add("log", LogLevel.Info);
        Add("info", LogLevel.Info);
        Add("debug", LogLevel.Debug);
        Add("warn", LogLevel.Warn);
        Add("error", LogLevel.Error);
        Add("exception", LogLevel.Error);
        console.Set("trace", Fn(engine, "trace", args =>
        {
            var text = Join(runtime, args);
            var stack = ScriptStack(engine);
            qml.Log.Info("", stack.Length == 0 ? text : text + Environment.NewLine + stack);
            return JsValue.Undefined;
        }));
        console.Set("assert", Fn(engine, "assert", args =>
        {
            if (args.Length == 0 || !TypeConverter.ToBoolean(args[0]))
                qml.Log.Error("", "Assertion failed: " + Join(runtime, args.Skip(1).ToArray()));
            return JsValue.Undefined;
        }));
        console.Set("time", Fn(engine, "time", args => { timers[Str(args, 0, "default")] = qml.Loop.Now; return JsValue.Undefined; }));
        console.Set("timeEnd", Fn(engine, "timeEnd", args =>
        {
            var label = Str(args, 0, "default");
            if (timers.Remove(label, out var start))
                qml.Log.Info("", $"{label}: {(qml.Loop.Now - start).TotalMilliseconds:0}ms");
            return JsValue.Undefined;
        }));
        console.Set("count", Fn(engine, "count", args =>
        {
            var label = Str(args, 0, "default");
            counts[label] = counts.GetValueOrDefault(label) + 1;
            qml.Log.Info("", $"{label}: {counts[label]}");
            return JsValue.Undefined;
        }));
        return console;
    }

    private static string Str(JsValue[] args, int i, string fallback) =>
        i < args.Length && !args[i].IsUndefined() ? TypeConverter.ToString(args[i]) : fallback;

    /// <summary>The script stack at the call, as <c>new Error().stack</c> would report it.</summary>
    private static string ScriptStack(Engine engine)
    {
        try
        {
            var error = engine.Intrinsics.Error.Construct("trace");
            var stack = error.Get("stack");
            return stack.IsString() ? stack.AsString() : "";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return "";
        }
    }

    /// <summary>Formats console arguments like QML: objects by their type, arrays by their items.</summary>
    public static string Join(JsRuntime runtime, JsValue[] args) =>
        string.Join(" ", args.Select(a => Describe(runtime, a, 0)));

    private static string Describe(JsRuntime runtime, JsValue value, int depth)
    {
        if (value is QObjectWrapper w)
            return w.Target.ToString();
        if (value.IsArray() && depth < 3)
        {
            var array = value.AsArray();
            var items = new List<string>();
            for (uint i = 0; i < array.Length; i++)
                items.Add(Describe(runtime, array[i], depth + 1));
            return "[" + string.Join(",", items) + "]";
        }
        if (value.IsObject() && !value.IsCallable() && value is not ObjectWrapper { Target: QColor or QPoint or QSize or QRect } && !value.IsDate() && !value.IsRegExp())
        {
            if (value.AsObject().Get("toString") is var ts && ts.IsCallable() && ts != runtime.Engine.Intrinsics.Object.PrototypeObject.Get("toString"))
            {
                try
                {
                    return TypeConverter.ToString(runtime.Engine.Call(ts, value, []));
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    return "[object Object]";
                }
            }
            try
            {
                return depth > 0 ? "[object Object]" : new Jint.Native.Json.JsonSerializer(runtime.Engine).Serialize(value).ToString();
            }
            catch (JavaScriptException)
            {
                return "[object Object]";
            }
        }
        if (value.IsDate())
            return value.AsDate().ToDateTime().ToLocalTime().ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        return TypeConverter.ToString(value);
    }

    private static JsObject MakeQt(JsRuntime runtime, QmlEngine qml)
    {
        var engine = runtime.Engine;
        var qt = new JsObject(engine);
        void Set(string name, Func<JsValue[], JsValue> body) => qt.Set(name, Fn(engine, name, body));
        double Num(JsValue[] a, int i, double fallback) => i < a.Length && !a[i].IsUndefined() ? TypeConverter.ToNumber(a[i]) : fallback;
        QColor Col(JsValue[] a, int i) => i < a.Length ? ValueConverter.ToColor(a[i]) : QColor.Invalid;

        Set("rgba", a => runtime.ToJs(new QColor(Num(a, 0, 0), Num(a, 1, 0), Num(a, 2, 0), Num(a, 3, 1))));
        Set("hsla", a => runtime.ToJs(QColor.FromHsl(Num(a, 0, 0), Num(a, 1, 0), Num(a, 2, 0), Num(a, 3, 1))));
        Set("hsva", a => runtime.ToJs(QColor.FromHsv(Num(a, 0, 0), Num(a, 1, 0), Num(a, 2, 0), Num(a, 3, 1))));
        Set("color", a => runtime.ToJs(Col(a, 0)));
        Set("lighter", a => runtime.ToJs(Col(a, 0).Lighter(Num(a, 1, 1.5))));
        Set("darker", a => runtime.ToJs(Col(a, 0).Darker(Num(a, 1, 2.0))));
        Set("alpha", a => runtime.ToJs(Col(a, 0).WithAlpha(Num(a, 1, 1))));
        Set("tint", a => runtime.ToJs(QColor.Tint(Col(a, 0), Col(a, 1))));
        Set("colorEqual", a => Col(a, 0).Argb == Col(a, 1).Argb);
        Set("point", a => runtime.ToJs(new QPoint(Num(a, 0, 0), Num(a, 1, 0))));
        Set("size", a => runtime.ToJs(new QSize(Num(a, 0, 0), Num(a, 1, 0))));
        Set("rect", a => runtime.ToJs(new QRect(Num(a, 0, 0), Num(a, 1, 0), Num(a, 2, 0), Num(a, 3, 0))));
        Set("vector2d", a =>
        {
            var o = new JsObject(engine);
            o.Set("x", Num(a, 0, 0));
            o.Set("y", Num(a, 1, 0));
            return o;
        });
        Set("font", a => a.Length > 0 ? a[0] : new JsObject(engine));
        Set("binding", a => a.Length > 0 && a[0].IsCallable()
            ? JsValue.FromObject(engine, new BindingFunction(a[0]))
            : throw new JavaScriptException(engine.Intrinsics.TypeError, "binding(): argument is not a function"));
        Set("callLater", a =>
        {
            if (a.Length == 0 || !a[0].IsCallable())
                return JsValue.Undefined;
            var fn = a[0];
            var rest = a.Skip(1).ToArray();
            CallLater.Schedule(qml, fn, rest);
            return JsValue.Undefined;
        });
        // Code written in a QML file sees that document's Qt (MakeDocumentQt). Anything else, such as a
        // .pragma library script, resolves against the shell's folder.
        string ShellDir() => qml.ShellDir ?? Environment.CurrentDirectory;
        Set("resolvedUrl", a => Resolve(Str(a, 0, ""), ShellDir()));
        Set("createComponent", a => CreateComponent(runtime, qml, Resolve(Str(a, 0, ""), ShellDir())));
        Set("openUrlExternally", a =>
        {
            if (a.Length > 0)
                qml.GetService<IPlatformServices>()?.OpenUrl(TypeConverter.ToString(a[0]));
            return true;
        });
        Set("quit", _ =>
        {
            qml.GetService<IPlatformServices>()?.Quit(0);
            return JsValue.Undefined;
        });
        Set("exit", a =>
        {
            qml.GetService<IPlatformServices>()?.Quit((int)Num(a, 0, 0));
            return JsValue.Undefined;
        });
        Set("formatDateTime", a => FormatDate(a, "ddd MMM d hh:mm:ss yyyy", LocalePart.DateTime));
        Set("formatDate", a => FormatDate(a, "ddd MMM d yyyy", LocalePart.Date));
        Set("formatTime", a => FormatDate(a, "hh:mm:ss", LocalePart.Time));
        Set("md5", a => Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(Str(a, 0, "")))));
        Set("btoa", a => Convert.ToBase64String(Encoding.UTF8.GetBytes(Str(a, 0, ""))));
        Set("atob", a => Encoding.UTF8.GetString(Convert.FromBase64String(Str(a, 0, ""))));
        Set("isQtObject", a => a.Length > 0 && a[0] is QObjectWrapper);
        Set("createQmlObject", a =>
        {
            var text = Str(a, 0, "");
            var parent = a.Length > 1 ? ValueConverter.FromJs(a[1]) as QObject : null;
            var doc = qml.LoadInline(text, qml.ShellDir ?? Environment.CurrentDirectory);
            var obj = new QmlComponent(qml, doc).Create(parent, null, null);
            return runtime.Wrap(obj);
        });

        var platform = new JsObject(engine);
        platform.Set("os", "windows");
        platform.Set("pluginName", "windows");
        qt.Set("platform", platform);

        var application = new JsObject(engine);
        application.Set("name", "winshell");
        application.Set("displayName", "Winshell");
        application.Set("version", typeof(Globals).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        application.Set("organization", "");
        application.Set("arguments", new JsArray(engine, Environment.GetCommandLineArgs().Select(s => (JsValue)s).ToArray()));
        qt.Set("application", application);

        foreach (var (name, value) in QtEnums.All)
            qt.Set(name, value);
        // Qt.ColorScheme is an enum, not a number. Qt.ColorScheme.Dark is how a config names the scheme.
        var colorScheme = new JsObject(engine);
        colorScheme.Set("Unknown", 0);
        colorScheme.Set("Light", 1);
        colorScheme.Set("Dark", 2);
        qt.Set("ColorScheme", colorScheme);
        return qt;
    }

    /// <summary>
    /// The <c>Qt</c> that code written in <paramref name="file"/> sees: the global one, except that
    /// <c>resolvedUrl</c> and <c>createComponent</c> resolve a relative URL against that file's folder.
    /// </summary>
    internal static ObjectInstance MakeDocumentQt(JsRuntime runtime, QmlEngine qml, ObjectInstance qt, string file)
    {
        var engine = runtime.Engine;
        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var documentQt = (ObjectInstance)engine.Call(engine.Intrinsics.Object.Get("create"), qt);
        documentQt.Set("resolvedUrl", Fn(engine, "resolvedUrl", a => Resolve(Str(a, 0, ""), dir)));
        documentQt.Set("createComponent", Fn(engine, "createComponent", a => CreateComponent(runtime, qml, Resolve(Str(a, 0, ""), dir))));
        return documentQt;
    }

    /// <summary>An absolute path or a URL with a scheme is returned as given. <c>file:///</c> is turned into a path.</summary>
    private static string Resolve(string url, string dir)
    {
        if (url.StartsWith("file:///", StringComparison.Ordinal))
            url = Uri.UnescapeDataString(url[8..]);
        if (url.Length == 0 || Path.IsPathRooted(url) || url.Contains("://", StringComparison.Ordinal))
            return url;
        return Path.GetFullPath(Path.Combine(dir, url));
    }

    private static JsValue CreateComponent(JsRuntime runtime, QmlEngine qml, string path)
    {
        try
        {
            return runtime.Wrap(new QmlComponent(qml, qml.LoadDocument(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Qml.QmlSyntaxException or QmlCompileException)
        {
            return runtime.Wrap(new QmlComponent(qml, e.Message));
        }
    }

    private static JsValue FormatDate(JsValue[] args, string defaultFormat, LocalePart part)
    {
        if (args.Length == 0)
            return "";
        DateTime date;
        if (args[0].IsDate())
            date = args[0].AsDate().ToDateTime().ToLocalTime();
        else if (args[0].IsNumber())
            date = DateTimeOffset.FromUnixTimeMilliseconds((long)args[0].AsNumber()).LocalDateTime;
        else if (args[0].IsString() && DateTime.TryParse(args[0].AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            date = parsed;
        else
            return "";
        if (args.Length < 2 || args[1].IsUndefined())
            return QtDateFormat.Format(date, defaultFormat);
        if (args[1].IsNumber())
        {
            return (int)args[1].AsNumber() switch
            {
                1 => date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                9 => date.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                8 => date.ToString("dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture) + QtDateFormat.Rfc2822Offset(date),
                // Qt.SystemLocaleShortDate, Qt.DefaultLocaleShortDate, and the deprecated locale aliases.
                // The same patterns as CalendarMonth.timeLabel: the process regional format.
                2 or 3 or 4 or 6 => Regional(date, part, longFormat: false),
                5 or 7 => Regional(date, part, longFormat: true),
                _ => QtDateFormat.Format(date, defaultFormat),
            };
        }
        return QtDateFormat.Format(date, TypeConverter.ToString(args[1]));
    }

    /// <summary>Which part of a date <c>Qt.formatDate</c>, <c>Qt.formatTime</c>, or <c>Qt.formatDateTime</c> is writing.</summary>
    private enum LocalePart { DateTime, Date, Time }

    private static string Regional(DateTime date, LocalePart part, bool longFormat)
    {
        var pattern = (part, longFormat) switch
        {
            (LocalePart.Time, false) => "t",
            (LocalePart.Time, true) => "T",
            (LocalePart.Date, false) => "d",
            (LocalePart.Date, true) => "D",
            (_, false) => "g",
            _ => "F",
        };
        return date.ToString(pattern, CultureInfo.CurrentCulture);
    }
}

/// <summary>Runs functions queued with <c>Qt.callLater</c> once, after the current work.</summary>
internal static class CallLater
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QmlEngine, List<(JsValue Fn, JsValue[] Args)>> Queues = new();

    public static void Schedule(QmlEngine engine, JsValue fn, JsValue[] args)
    {
        var queue = Queues.GetOrCreateValue(engine);
        // A function queued twice before it runs runs once, with the newest arguments.
        var index = queue.FindIndex(e => ReferenceEquals(e.Fn, fn));
        if (index >= 0)
        {
            queue[index] = (fn, args);
            return;
        }
        queue.Add((fn, args));
        if (queue.Count == 1)
        {
            engine.Loop.Post(() =>
            {
                var items = queue.ToArray();
                queue.Clear();
                if (engine.IsDisposed)
                    return;
                foreach (var (f, a) in items)
                {
                    try
                    {
                        engine.Js.Engine.Call(f, JsValue.Undefined, a);
                    }
                    catch (Exception e)
                    {
                        engine.Log.ScriptError(e, "Qt.callLater");
                    }
                }
            });
        }
    }
}

/// <summary>Formats dates with Qt's format letters: <c>ddd MMM d hh:mm:ss AP</c>.</summary>
public static class QtDateFormat
{
    public static string Format(DateTime date, string format)
    {
        var hasAmPm = HasAmPm(format);
        var sb = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            if (c == '\'')
            {
                var end = format.IndexOf('\'', i + 1);
                if (end == i + 1)
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }
                if (end < 0)
                    end = format.Length;
                sb.Append(format, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }
            var run = 1;
            while (i + run < format.Length && format[i + run] == c)
                run++;
            var culture = CultureInfo.CurrentCulture;
            var hour12 = date.Hour % 12 == 0 ? 12 : date.Hour % 12;
            switch (c)
            {
                case 'd':
                    sb.Append(run switch
                    {
                        1 => date.Day.ToString(CultureInfo.InvariantCulture),
                        2 => date.Day.ToString("00", CultureInfo.InvariantCulture),
                        3 => culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek),
                        _ => culture.DateTimeFormat.GetDayName(date.DayOfWeek),
                    });
                    break;
                case 'M':
                    sb.Append(run switch
                    {
                        1 => date.Month.ToString(CultureInfo.InvariantCulture),
                        2 => date.Month.ToString("00", CultureInfo.InvariantCulture),
                        3 => culture.DateTimeFormat.GetAbbreviatedMonthName(date.Month),
                        _ => culture.DateTimeFormat.GetMonthName(date.Month),
                    });
                    break;
                case 'y':
                    sb.Append(run >= 4 ? date.Year.ToString("0000", CultureInfo.InvariantCulture) : (date.Year % 100).ToString("00", CultureInfo.InvariantCulture));
                    if (run != 2 && run < 4)
                        sb.Append(new string('y', run == 1 ? 0 : run - 2));
                    break;
                case 'h':
                    {
                        var h = hasAmPm ? hour12 : date.Hour;
                        sb.Append(run >= 2 ? h.ToString("00", CultureInfo.InvariantCulture) : h.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                case 'H':
                    sb.Append(run >= 2 ? date.Hour.ToString("00", CultureInfo.InvariantCulture) : date.Hour.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'm':
                    sb.Append(run >= 2 ? date.Minute.ToString("00", CultureInfo.InvariantCulture) : date.Minute.ToString(CultureInfo.InvariantCulture));
                    break;
                case 's':
                    sb.Append(run >= 2 ? date.Second.ToString("00", CultureInfo.InvariantCulture) : date.Second.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'z':
                    sb.Append(run >= 3 ? date.Millisecond.ToString("000", CultureInfo.InvariantCulture) : date.Millisecond.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'A' or 'a':
                    {
                        var marker = date.Hour < 12 ? culture.DateTimeFormat.AMDesignator : culture.DateTimeFormat.PMDesignator;
                        if (string.IsNullOrEmpty(marker))
                            marker = date.Hour < 12 ? "AM" : "PM";
                        sb.Append(c == 'A' ? marker.ToUpper(culture) : marker.ToLower(culture));
                        // "AP" and "ap" are one token.
                        if (i + 1 < format.Length && (format[i + 1] == 'P' || format[i + 1] == 'p'))
                            run = 2;
                        else
                            run = 1;
                        break;
                    }
                case 't':
                    sb.Append(ZoneAbbreviation(date));
                    break;
                default:
                    sb.Append(c, run);
                    break;
            }
            i += run;
        }
        return sb.ToString();
    }

    /// <summary>RFC 2822 offset, <c>+0100</c>, with no colon.</summary>
    internal static string Rfc2822Offset(DateTime date)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(date);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{offset.Hours:00}{offset.Minutes:00}");
    }

    /// <summary>A short zone name, such as <c>PST</c>. The long Windows name is not what Qt prints.</summary>
    private static string ZoneAbbreviation(DateTime date)
    {
        var zone = TimeZoneInfo.Local;
        var name = zone.IsDaylightSavingTime(date) ? zone.DaylightName : zone.StandardName;
        if (name.Length > 0 && name.Length <= 5 && !name.Contains(' '))
            return name;
        var initials = new StringBuilder();
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var letter in word)
            {
                if (char.IsLetter(letter))
                {
                    initials.Append(char.ToUpperInvariant(letter));
                    break;
                }
            }
        }
        return initials.Length > 0 ? initials.ToString() : Rfc2822Offset(date);
    }

    /// <summary>An <c>a</c> or <c>A</c> outside quotes selects 12-hour time. Quoted text, such as <c>'at'</c>, does not.</summary>
    private static bool HasAmPm(string format)
    {
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '\'')
            {
                if (format[i] is 'a' or 'A')
                    return true;
                continue;
            }
            var end = format.IndexOf('\'', i + 1);
            if (end < 0)
                return false;
            i = end;
        }
        return false;
    }
}

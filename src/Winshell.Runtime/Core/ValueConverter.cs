using System.Collections;
using System.Globalization;
using Jint;
using Jint.Native;

namespace Winshell.Runtime;

/// <summary>Converts values between JavaScript, QML types and C# property types.</summary>
public static class ValueConverter
{
    public static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    public static object? DefaultOfQmlType(string qmlType) => qmlType switch
    {
        "int" => 0,
        "real" or "double" or "number" => 0.0,
        "bool" => false,
        "string" or "url" => "",
        "color" => QColor.Invalid,
        // QML's default date is an invalid Date, which scripts can still call methods on.
        "date" => DateTime.MinValue,
        "var" or "variant" => null,
        _ when qmlType.StartsWith("list<", StringComparison.Ordinal) || qmlType == "list" => new QmlList<object>(),
        _ => null,
    };

    /// <summary>Converts a value for a property declared in QML with the given type.</summary>
    public static object? ConvertToQmlType(object? value, string qmlType, QmlEngine? engine)
    {
        if (qmlType is "var" or "variant")
            return KeepJs(value);
        value = Unwrap(value);
        switch (qmlType)
        {
            case "int":
                return ToInt(value, qmlType);
            case "real" or "double" or "number":
                return ToDouble(value, qmlType);
            case "bool":
                return ToBool(value);
            case "string":
                return ToQmlString(value);
            case "url":
                return value is null ? "" : ToQmlString(value);
            case "color":
                return ToColor(value);
            case "var" or "variant" or "alias":
                return value;
            case "date":
                return value;
            case "point":
                return value is QPoint ? value : value;
            default:
                if (value is null or QObject)
                    return value;
                if (value is QmlComponent)
                    return value;
                return value;
        }
    }

    /// <summary>Converts a value for a C# property or parameter type.</summary>
    public static object? Convert(object? value, Type target, QmlEngine? engine)
    {
        if (target == typeof(JsValue))
            return value as JsValue ?? (engine is null ? JsValue.Undefined : engine.Js.ToJs(value));
        value = Unwrap(value);
        if (target == typeof(object))
            return value;
        if (value is not null && target.IsInstanceOfType(value))
            return value;
        var underlying = Nullable.GetUnderlyingType(target);
        if (underlying is not null)
        {
            if (value is null)
                return null;
            target = underlying;
        }
        if (target == typeof(double))
            return ToDouble(value, "double");
        if (target == typeof(float))
            return (float)ToDouble(value, "real");
        if (target == typeof(int))
            return ToInt(value, "int");
        if (target == typeof(long))
            return (long)ToDouble(value, "int");
        if (target == typeof(bool))
            return ToBool(value);
        if (target == typeof(string))
            return value is null ? null : ToQmlString(value);
        if (target == typeof(QColor))
            return ToColor(value);
        if (target.IsEnum)
        {
            if (value is string s && Enum.TryParse(target, s, true, out var parsed))
                return parsed;
            return Enum.ToObject(target, ToInt(value, target.Name));
        }
        if (target == typeof(QPoint) && value is IDictionary<string, object?> pd)
            return new QPoint(ToDouble(Get(pd, "x"), "real"), ToDouble(Get(pd, "y"), "real"));
        if (target == typeof(QSize) && value is IDictionary<string, object?> sd)
            return new QSize(ToDouble(Get(sd, "width"), "real"), ToDouble(Get(sd, "height"), "real"));
        if (target == typeof(QRect) && value is IDictionary<string, object?> rd)
            return new QRect(ToDouble(Get(rd, "x"), "real"), ToDouble(Get(rd, "y"), "real"),
                ToDouble(Get(rd, "width"), "real"), ToDouble(Get(rd, "height"), "real"));
        if (target == typeof(TimeSpan))
            return TimeSpan.FromMilliseconds(ToDouble(value, "int"));
        if (value is null)
        {
            if (target.IsValueType)
                throw new QmlException($"Unable to assign [undefined] to {QmlTypeName(target, engine)}");
            return null;
        }
        if (typeof(QObject).IsAssignableFrom(target))
            throw new QmlException($"Unable to assign {Describe(value)} to {QmlTypeName(target, engine)}");
        if (target == typeof(IReadOnlyList<string>) || target == typeof(string[]) || target == typeof(List<string>))
        {
            var strings = ToList(value).Select(v => v is null ? "" : ToQmlString(v)).ToList();
            return target == typeof(string[]) ? strings.ToArray() : strings;
        }
        if (target == typeof(IReadOnlyList<object?>) || target == typeof(List<object?>))
            return ToList(value);
        if (target == typeof(IReadOnlyDictionary<string, object?>) || target == typeof(Dictionary<string, object?>))
        {
            if (value is Dictionary<string, object?> dict)
                return dict;
        }
        throw new QmlException($"Unable to assign {Describe(value)} to {QmlTypeName(target, engine)}");
    }

    private static object? Get(IDictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    /// <summary>Replaces the items of a list property with the given value: one object, or an array of them.</summary>
    public static void AssignList(IQmlList list, object? value, QmlEngine? engine)
    {
        value = Unwrap(value);
        list.Clear();
        if (value is null)
            return;
        if (value is IQmlList other)
        {
            foreach (var item in other.ToList())
                list.Append(item);
            return;
        }
        if (value is IList items)
        {
            foreach (var item in items)
                list.Append(Unwrap(item));
            return;
        }
        list.Append(value);
    }

    /// <summary>
    /// The value a <c>var</c> property keeps: JavaScript arrays and objects stay JavaScript values, so a script
    /// that changes them changes the property's value; primitives and QML objects become C# values.
    /// </summary>
    public static object? KeepJs(object? value)
    {
        if (value is not JsValue js)
            return value;
        if (js is QObjectWrapper wrapper)
            return wrapper.Target;
        // Value types cross as interop wrappers. A plain object or array must stay the JavaScript value:
        // ToObject() would copy the whole graph and throw the copy away.
        if (js is Jint.Runtime.Interop.ObjectWrapper objectWrapper)
            return objectWrapper.Target;
        if (js.IsObject())
            return js;
        return FromJs(js);
    }

    /// <summary>Turns JavaScript values into plain C# values. Functions and plain objects stay JsValue.</summary>
    public static object? Unwrap(object? value)
    {
        if (value is not JsValue js)
            return value;
        return FromJs(js);
    }

    public static object? FromJs(JsValue js)
    {
        if (js.IsUndefined() || js.IsNull())
            return null;
        if (js.IsBoolean())
            return js.AsBoolean();
        if (js.IsNumber())
            return js.AsNumber();
        if (js.IsString())
            return js.AsString();
        if (js is QObjectWrapper wrapper)
            return wrapper.Target;
        if (js.IsDate())
            return js.AsDate().ToDateTime();
        if (js.IsArray())
        {
            var array = js.AsArray();
            var list = new List<object?>((int)array.Length);
            for (uint i = 0; i < array.Length; i++)
                list.Add(FromJs(array[i]));
            return list;
        }
        if (js is Jint.Runtime.Interop.ObjectWrapper objectWrapper)
            return objectWrapper.Target;
        return js;
    }

    public static List<object?> ToList(object? value)
    {
        value = Unwrap(value);
        return value switch
        {
            null => [],
            List<object?> l => l,
            IQmlList q => q.ToList(),
            string s => [s],
            IEnumerable e => e.Cast<object?>().ToList(),
            _ => [value],
        };
    }

    public static double ToDouble(object? value, string type) => Unwrap(value) switch
    {
        double d => d,
        int i => i,
        long l => l,
        float f => f,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        string s when s.Length == 0 => 0,
        Enum e => System.Convert.ToInt32(e, CultureInfo.InvariantCulture),
        null => throw new QmlException($"Unable to assign [undefined] to {type}"),
        var other => throw new QmlException($"Unable to assign {Describe(other)} to {type}"),
    };

    public static int ToInt(object? value, string type)
    {
        var d = ToDouble(value, type);
        if (double.IsNaN(d) || double.IsInfinity(d))
            return 0;
        return (int)Math.Clamp(Math.Truncate(d), int.MinValue, int.MaxValue);
    }

    public static bool ToBool(object? value) => Unwrap(value) switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        int i => i != 0,
        string s => s.Length > 0,
        _ => true,
    };

    public static QColor ToColor(object? value) => Unwrap(value) switch
    {
        QColor c => c,
        string s => QColor.TryParse(s, out var c) ? c : throw new QmlException($"Invalid color \"{s}\""),
        null => QColor.Invalid,
        double d => QColor.FromArgb((uint)d),
        var other => throw new QmlException($"Unable to assign {Describe(other)} to color"),
    };

    public static string ToQmlString(object? value) => Unwrap(value) switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        int i => i.ToString(CultureInfo.InvariantCulture),
        JsValue js => js.ToString(),
        var other => other.ToString() ?? "",
    };

    /// <summary>Formats a number like JavaScript does.</summary>
    public static string FormatNumber(double d)
    {
        if (double.IsNaN(d))
            return "NaN";
        if (double.IsPositiveInfinity(d))
            return "Infinity";
        if (double.IsNegativeInfinity(d))
            return "-Infinity";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e21)
            return ((decimal)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    public static bool AreEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a is JsValue ja && b is JsValue jb)
            return ja.IsPrimitive() && jb.IsPrimitive() ? ja.Equals(jb) : ReferenceEquals(ja, jb);
        if (a is JsValue || b is JsValue)
            return false;
        return a.Equals(b);
    }

    public static string QmlTypeName(Type type, QmlEngine? engine = null) => type switch
    {
        _ when type == typeof(double) => "double",
        _ when type == typeof(int) => "int",
        _ when type == typeof(bool) => "bool",
        _ when type == typeof(string) => "QString",
        _ when type == typeof(QColor) => "QColor",
        _ => engine?.Modules.NameOf(type) ?? type.Name,
    };

    public static string Describe(object? value) => value switch
    {
        null => "[undefined]",
        QObject o => o.ToString(),
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "",
    };
}

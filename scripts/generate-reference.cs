// Writes the reference pages, docs/reference/winshell*.md: a page per namespace of the Winshell package, from the
// public types of Winshell.Shell, Winshell.Services and Winshell.Core and their /// comments. A page keeps what is
// above its marker line, and this writes everything below it. Run it after changing a public member, and commit the
// pages it writes. DocsTests compiles this file and fails when a committed page is not what it writes.
//
//   dotnet run scripts/generate-reference.cs

#:property PublishAot=false
#:property RuntimeIdentifier=$(NETCoreSdkRuntimeIdentifier)
#:project ../src/Winshell.Shell/Winshell.Shell.csproj
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Winshell.Docs;

#if !REFERENCE_PAGES_LIBRARY
internal static class Program
{
    private static void Main()
    {
        var scripts = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".";
        var folder = Path.GetFullPath(Path.Combine(scripts, "..", "docs", "reference"));
        var pages = ReferencePages.Generate(folder);
        foreach (var file in ReferencePages.Stale(folder, pages))
        {
            if (pages.TryGetValue(file, out var text))
            {
                File.WriteAllText(Path.Combine(folder, file), text);
                Console.WriteLine($"wrote docs/reference/{file}");
            }
            else
            {
                File.Delete(Path.Combine(folder, file));
                Console.WriteLine($"deleted docs/reference/{file}, whose namespace is gone");
            }
        }
    }
}
#endif

/// <summary>
/// The reference pages of Winshell.Shell, Winshell.Services and Winshell.Core. A page has a section for each
/// included type: the public types of a <c>Winshell</c> namespace, and the public types their members use. A
/// section lists the public members the type declares, with their /// comments.
/// </summary>
internal static partial class ReferencePages
{
    public const string Marker = "<!-- generated below: dotnet run scripts/generate-reference.cs -->";

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Each page's text by file name, with the top of the page that is in <paramref name="folder"/>.</summary>
    public static SortedDictionary<string, string> Generate(string folder)
    {
        Assembly[] assemblies = [typeof(Shell).Assembly, typeof(ShellIpc).Assembly, typeof(IEventLoop).Assembly];
        var docs = assemblies.SelectMany(Docs).DistinctBy(pair => pair.Key).ToDictionary(StringComparer.Ordinal);
        var included = Included(assemblies);
        var pages = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in included.GroupBy(type => type.Namespace!))
        {
            var file = FileName(group.Key);
            var page = new Page(group.Key, included, docs);
            var text = new StringBuilder(Top(Path.Combine(folder, file), group.Key));
            foreach (var type in group.OrderBy(type => type.Name, StringComparer.Ordinal))
                text.Append(page.Section(type));
            pages[file] = text.ToString().TrimEnd('\n') + "\n";
        }

        return pages;
    }

    /// <summary>
    /// The files in <paramref name="folder"/> that are not what <paramref name="pages"/> says: a page that differs
    /// or is missing, and a <c>winshell*.md</c> page of a namespace that is gone.
    /// </summary>
    public static List<string> Stale(string folder, SortedDictionary<string, string> pages)
    {
        var stale = pages
            .Where(page => !File.Exists(Path.Combine(folder, page.Key)) ||
                           File.ReadAllText(Path.Combine(folder, page.Key)).ReplaceLineEndings("\n") != page.Value)
            .Select(page => page.Key)
            .ToList();
        stale.AddRange(Directory.EnumerateFiles(folder, "winshell*.md").Select(path => Path.GetFileName(path))
            .Where(file => !pages.ContainsKey(file)));
        return stale;
    }

    private static string FileName(string ns)
    {
        return ns == "Winshell" ? "winshell.md" : ns.ToLowerInvariant().Replace('.', '-') + ".md";
    }

    /// <summary>The hand-written top of a page, down to the marker, or a title and the marker for a new page.</summary>
    private static string Top(string path, string ns)
    {
        if (!File.Exists(path))
            return $"# {ns}\n\n{Marker}\n";
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        var marker = text.IndexOf(Marker + "\n", StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidOperationException($"{path} has no line {Marker}");
        return text[..(marker + Marker.Length + 1)];
    }

    private static IEnumerable<KeyValuePair<string, XElement>> Docs(Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        foreach (var member in XDocument.Load(path).Descendants("member"))
            yield return new KeyValuePair<string, XElement>((string?)member.Attribute("name") ?? "", member);
    }

    /// <summary>One namespace's page: links to included types, and the /// comments of every member.</summary>
    private sealed class Page(string ns, HashSet<Type> included, Dictionary<string, XElement> docs)
    {
        private readonly NullabilityInfoContext nullability = new();

        public string Section(Type type)
        {
            var text = new StringBuilder($"\n## {type.Name}\n\n");
            var summary = Doc("T:" + Id(type), type, true);
            if (summary.Length > 0)
                text.Append(summary).Append("\n\n");
            if (type.IsEnum)
            {
                text.Append("| Value | Number | Description |\n| --- | --- | --- |\n");
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var number = Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
                    text.Append(CultureInfo.InvariantCulture,
                        $"| `{field.Name}` | {number} | {Doc("F:" + Id(type) + "." + field.Name, type)} |\n");
                }

                return text.ToString();
            }

            if (Inherits(type) is { } line)
                text.Append(line).Append("\n\n");
            var rows = Rows(type).ToList();
            if (rows.Count == 0)
                return text.ToString();
            text.Append("| Member | Type | Description |\n| --- | --- | --- |\n");
            // A record's primary constructor has the record's own comment, which the section already says.
            var said = Doc("T:" + Id(type), type);
            foreach (var (cell, kind, id) in rows)
            {
                // A constructor the compiler adds has no comment.
                var doc = Doc(id, type, optional: cell == type.Name + "()");
                text.Append(CultureInfo.InvariantCulture, $"| `{cell}` | {kind} | {(doc == said ? "" : doc)} |\n");
            }

            return text.ToString();
        }

        /// <summary>What a section says about the base type, whose members it does not list.</summary>
        private string? Inherits(Type type)
        {
            if (type.IsInterface || type.IsValueType || type.BaseType is not { } baseType || baseType == typeof(object))
                return null;
            if (included.Contains(baseType))
                return $"Has the members of {Link(baseType)}.";
            if (baseType == typeof(Exception))
                return "An exception.";
            if (baseType == typeof(Attribute))
                return "An attribute.";
            var winui = baseType.Namespace?.StartsWith("Microsoft.UI", StringComparison.Ordinal) == true
                ? "WinUI "
                : "";
            return $"A {winui}{baseType.Name}. The members here are the ones Winshell adds.";
        }

        /// <summary>The members table: constructors, properties, fields, events and methods, by name.</summary>
        private IEnumerable<(string Cell, string Kind, string Id)> Rows(Type type)
        {
            foreach (var ctor in type.GetConstructors(Declared).OrderBy(ctor => ctor.GetParameters().Length))
                yield return ($"{type.Name}({Parameters(ctor)})", "constructor", Id(ctor));
            foreach (var property in type.GetProperties(Declared).Where(member => !Skip(member))
                         .OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                // A property has a getter or a setter.
                var accessor = property.GetMethod ?? property.SetMethod!;
                var kind = Name(property.PropertyType, nullability.Create(property).ReadState) +
                           (accessor.IsStatic ? ", static" : "");
                if (property.SetMethod is not { IsPublic: true } setter)
                    kind += ", read-only";
                else if (setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                    kind += ", init";
                yield return (property.Name, kind,
                    "P:" + Id(type) + "." + property.Name + Id(property.GetIndexParameters()));
            }

            foreach (var field in type.GetFields(Declared).Where(member => !Skip(member))
                         .OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                var kind = Name(field.FieldType, nullability.Create(field).ReadState) +
                           (field.IsStatic ? ", static" : "");
                yield return (field.Name, kind, "F:" + Id(type) + "." + field.Name);
            }

            // An event has a handler type.
            foreach (var evt in type.GetEvents(Declared).Where(member => !Skip(member))
                         .OrderBy(member => member.Name, StringComparer.Ordinal))
                yield return (evt.Name, Name(evt.EventHandlerType!), "E:" + Id(type) + "." + evt.Name);
            var methods = type.GetMethods(Declared)
                .Where(method => !method.IsSpecialName && !Skip(method))
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.GetParameters().Length);
            foreach (var method in methods)
            {
                var generic = method.IsGenericMethod
                    ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">"
                    : "";
                var kind = Name(method.ReturnType, nullability.Create(method.ReturnParameter).ReadState) +
                           (method.IsStatic ? ", static" : "");
                yield return ($"{method.Name}{generic}({Parameters(method)})", kind, Id(method));
            }
        }

        private string Parameters(MethodBase method)
        {
            return string.Join(", ", method.GetParameters().Select(parameter =>
            {
                var text = $"{Name(parameter.ParameterType, nullability.Create(parameter).ReadState)} {parameter.Name}";
                return parameter.HasDefaultValue ? $"{text} = {Literal(parameter)}" : text;
            }));
        }

        private static string Literal(ParameterInfo parameter)
        {
            return parameter.DefaultValue switch
            {
                null when parameter.ParameterType.IsValueType &&
                          Nullable.GetUnderlyingType(parameter.ParameterType) is null => "default",
                null => "null",
                string text => $"\"{text}\"",
                bool value => value ? "true" : "false",
                Enum value => $"{value.GetType().Name}.{value}",
                var value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
        }

        /// <summary>A type as a reader names it: <c>IReadOnlyList of string</c>, and a link for an included type.</summary>
        private string Name(Type type, NullabilityState state = NullabilityState.Unknown)
        {
            var name = type switch
            {
                _ when Nullable.GetUnderlyingType(type) is { } inner => Name(inner) + "?",
                { IsByRef: true } when type.GetElementType() is { } element => Name(element),
                { IsArray: true } when type.GetElementType() is { } element => Name(element) + "[]",
                { IsGenericParameter: true } => type.Name,
                { IsGenericType: true } when
                    type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true =>
                    "(" + string.Join(", ", type.GetGenericArguments().Select(t => Name(t))) + ")",
                { IsGenericType: true } => Bare(type.Name) + " of " +
                                           string.Join(", ", type.GetGenericArguments().Select(t => Name(t))),
                _ when Aliases.TryGetValue(type, out var alias) => alias,
                _ when included.Contains(type) => Link(type),
                _ => type.Name
            };
            return state == NullabilityState.Nullable && !type.IsValueType ? name + "?" : name;
        }

        private string Link(Type type)
        {
            return type.Namespace == ns || type.Namespace is null
                ? $"[{type.Name}](#{type.Name.ToLowerInvariant()})"
                : $"[{type.Name}]({FileName(type.Namespace)}#{type.Name.ToLowerInvariant()})";
        }

        /// <summary>
        /// A member's /// comment as Markdown: its summary, its value or what it returns, and what it throws. Its
        /// params and remarks stay in the code. A table cell is one line; a type's section keeps its paragraphs.
        /// </summary>
        private string Doc(string id, Type within, bool paragraphs = false, bool optional = false)
        {
            if (!docs.TryGetValue(id, out var member))
                return optional ? "" : throw new InvalidOperationException($"{id} has no /// comment");
            var parts = member.Elements()
                .Where(element => element.Name.LocalName is "summary" or "value" or "returns" or "exception")
                .Select(element => element.Name.LocalName == "exception"
                    ? Throws(element, Id(within))
                    : Markdown(element, Id(within)));
            var text = string.Join("\n\n", parts.Where(part => part.Length > 0));
            var blocks = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(block => Spaces().Replace(block, " "));
            return paragraphs
                ? string.Join("\n\n", blocks)
                : string.Join(" ", blocks).Replace("|", "\\|", StringComparison.Ordinal);
        }

        private static string Markdown(XElement element, string within)
        {
            return string.Concat(element.Nodes().Select(node => node switch
            {
                XText text => text.Value,
                XElement { Name.LocalName: "c" } code => $"`{code.Value}`",
                XElement { Name.LocalName: "code" } => "",
                XElement { Name.LocalName: "para" } para => $"\n\n{Markdown(para, within)}\n\n",
                XElement { Name.LocalName: "paramref" or "typeparamref" } reference => reference.Attribute("name")
                    ?.Value ?? "",
                XElement { Name.LocalName: "see" or "seealso" } see when see.Attribute("cref") is { } cref => Cref(
                    cref.Value, within),
                XElement { Name.LocalName: "see" or "seealso" } see when see.Attribute("langword") is { } word => word
                    .Value,
                XElement { Name.LocalName: "see" or "seealso" } see when see.Attribute("href") is { } href =>
                    $"[{(see.Value.Length > 0 ? see.Value : href.Value)}]({href.Value})",
                XElement other => throw new NotSupportedException(
                    $"<{other.Name}> in a /// comment has no Markdown here"),
                _ => ""
            }));
        }

        /// <summary>An exception as a sentence: "The handler is null." reads "Throws X when the handler is null."</summary>
        private static string Throws(XElement exception, string within)
        {
            var text = Markdown(exception, within).Trim();
            if (text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]))
                text = char.ToLowerInvariant(text[0]) + text[1..];
            return (string?)exception.Attribute("cref") is { } cref ? $"Throws {Cref(cref, within)} when {text}" : text;
        }

        /// <summary>A cref as its name: a type alone, and a member with its type unless it is the one documented.</summary>
        private static string Cref(string cref, string within)
        {
            var name = cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..];
            var paren = name.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
                name = name[..paren];
            name = Arity().Replace(name, "");
            if (cref.StartsWith("T:", StringComparison.Ordinal) && Type.GetType(name) is { } type &&
                Aliases.TryGetValue(type, out var alias))
                return alias;
            if (cref.StartsWith("T:", StringComparison.Ordinal) || cref.StartsWith("N:", StringComparison.Ordinal))
                return name[(name.LastIndexOf('.') + 1)..];
            var dot = name.LastIndexOf('.');
            var owner = name[..dot];
            var member = name[(dot + 1)..]
                .Replace("#ctor", owner[(owner.LastIndexOf('.') + 1)..], StringComparison.Ordinal);
            return owner == within ? member : owner[(owner.LastIndexOf('.') + 1)..] + "." + member;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("`+[0-9]+")]
    private static partial Regex Arity();

    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(void)] = "void",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal"
    };

    private static string Bare(string name)
    {
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>The XML doc id of a constructor or method, as the compiler writes it.</summary>
    private static string Id(MethodBase method)
    {
        var name = method.IsConstructor ? "#ctor" : method.Name;
        if (method.IsGenericMethod)
            name += "``" + method.GetGenericArguments().Length;
        // Every method here is a member of a type.
        return "M:" + Id(method.DeclaringType!) + "." + name + Id(method.GetParameters());
    }

    private static string Id(ParameterInfo[] parameters)
    {
        return parameters.Length == 0
            ? ""
            : "(" + string.Join(",", parameters.Select(parameter => Id(parameter.ParameterType))) + ")";
    }

    /// <summary>A type as an XML doc id writes it, without the <c>T:</c>.</summary>
    private static string Id(Type? type)
    {
        return type switch
        {
            null => "",
            { IsGenericParameter: true } => (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition,
            { IsByRef: true } => Id(type.GetElementType()) + "@",
            { IsArray: true } => Id(type.GetElementType()) + "[]",
            { IsConstructedGenericType: true } => Bare(Id(type.GetGenericTypeDefinition()))
                                                  + "{" + string.Join(",", type.GetGenericArguments().Select(Id)) + "}",
            _ => (type.FullName ?? type.Name).Replace('+', '.')
        };
    }

    private static bool Skip(MemberInfo member)
    {
        if (member.Name is "Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "PrintMembers" or "GetType"
            or "op_Equality" or "op_Inequality" or "EqualityContract")
            return true;
        if (member.Name.StartsWith('<'))
            return true;
        return member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
    }

    /// <summary>
    /// The public types of a <c>Winshell</c> namespace in the assemblies, their public nested types, and the public
    /// types of the assemblies that their members, constructors and base types use.
    /// </summary>
    public static HashSet<Type> Included(Assembly[] assemblies)
    {
        var included = new HashSet<Type>();
        foreach (var type in assemblies.SelectMany(assembly => assembly.GetExportedTypes())
                     .Where(type => type.IsPublic && !type.IsNested))
            if (type.Namespace is "Winshell" ||
                type.Namespace?.StartsWith("Winshell.", StringComparison.Ordinal) == true)
                included.Add(type);
        var queue = new Queue<Type>(included);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                if (included.Add(nested))
                    queue.Enqueue(nested);
            foreach (var related in Related(type))
            {
                if (!assemblies.Contains(related.Assembly))
                    continue;
                if (related.IsGenericParameter || related.Name.StartsWith('<') || related.IsByRef || related.IsPointer)
                    continue;
                if (!related.IsPublic && !related.IsNestedPublic)
                    continue;
                if (included.Add(related))
                    queue.Enqueue(related);
            }
        }

        return included;
    }

    private static IEnumerable<Type> Related(Type type)
    {
        if (type.BaseType is { } baseType)
            yield return baseType;
        var types = type.GetProperties(Declared).Select(property => property.PropertyType)
            .Concat(type.GetFields(Declared).Select(field => field.FieldType))
            .Concat(type.GetEvents(Declared).Select(evt => evt.EventHandlerType).OfType<Type>())
            .Concat(type.GetMethods(Declared).Select(method => method.ReturnType))
            .Concat(type.GetMethods(Declared).Concat<MethodBase>(type.GetConstructors(Declared))
                .SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType));
        foreach (var related in types.SelectMany(Atoms))
            yield return related;
    }

    /// <summary>The types a type is made of: the element of an array or reference, and the arguments of a generic.</summary>
    private static IEnumerable<Type> Atoms(Type type)
    {
        if (type.GetElementType() is { } element)
            return Atoms(element);
        if (type.IsGenericType)
            return type.GetGenericArguments().SelectMany(Atoms);
        return [type];
    }
}

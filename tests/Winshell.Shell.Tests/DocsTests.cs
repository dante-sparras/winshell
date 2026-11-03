using System.Reflection;
using System.Runtime.CompilerServices;

namespace Winshell.ShellTests;

/// <summary>
/// Each reference page matches the public API of <c>Winshell.Shell</c> and <c>Winshell.Services</c>: every included
/// type has a section, and that section lists the public members the type declares and nothing else. Each
/// <c>csharp</c> sample in the docs and the README is text from <see cref="GuideSamples"/>, which this project builds.
/// </summary>
public sealed class DocsTests
{
    private static readonly string Repo = TestSupport.Repo.Root;

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Every_csharp_example_is_compiled()
    {
        var source = File.ReadAllText(Path.Combine(Repo, "tests", "Winshell.Shell.Tests", "GuideSamples.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var examples = Examples().ToList();
        Assert.NotEmpty(examples);
        var problems = new List<string>();
        foreach (var (file, line, code) in examples)
        {
            if (code.Length == 0)
                problems.Add($"{Path.GetRelativePath(Repo, file)}:{line}: empty csharp fence");
            else if (!source.Contains(code, StringComparison.Ordinal))
                problems.Add($"{Path.GetRelativePath(Repo, file)}:{line}: csharp fence is not in GuideSamples.cs");
        }
        Assert.True(problems.Count == 0, "C# examples that are not compiled:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_page_is_in_a_table_of_contents()
    {
        var docs = Path.Combine(Repo, "docs");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(docs, "index.md") };
        foreach (var toc in Directory.EnumerateFiles(docs, "toc.yml", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(toc))
            {
                var href = line.Trim();
                if (!href.StartsWith("href:", StringComparison.Ordinal))
                    continue;
                href = href["href:".Length..].Trim();
                if (href.EndsWith(".md", StringComparison.Ordinal))
                    listed.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(toc)!, href)));
            }
        }
        var missing = Pages().Where(p => p.StartsWith(docs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !listed.Contains(p));
        Assert.Empty(missing.Select(p => Path.GetRelativePath(Repo, p)));
    }

    /// <summary>
    /// Each Winshell namespace has a page. A section's table lists every public constructor, property, method, event
    /// and field the type declares, and no name the type does not have. An overload is one row. Enum values are the
    /// section's rows. A heading that is not an included type fails.
    /// </summary>
    [Fact]
    public void The_reference_lists_every_type_and_member()
    {
        var shell = typeof(Shell).Assembly;
        var services = typeof(ShellIpc).Assembly;
        var included = Included(shell, services);
        var problems = new List<string>();
        foreach (var group in included.GroupBy(t => t.Namespace!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var file = group.Key == "Winshell" ? "winshell.md" : group.Key.ToLowerInvariant().Replace('.', '-') + ".md";
            var page = Path.Combine("docs", "reference", file);
            var path = Path.Combine(Repo, page);
            if (!File.Exists(path))
            {
                problems.Add($"{group.Key} has no page {page}");
                continue;
            }
            var lines = File.ReadAllLines(path);
            if (lines.FirstOrDefault() != "# " + group.Key)
                problems.Add($"{page} does not start with \"# {group.Key}\"");
            var sections = Sections(lines, problems, page);
            var names = group.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var type in group)
            {
                if (!sections.TryGetValue(type.Name, out var section))
                {
                    problems.Add($"{page} has no \"## {type.Name}\" section");
                    continue;
                }
                if (type.IsEnum)
                {
                    CheckEnum(page, type, section, problems);
                    continue;
                }
                CheckMembers(page, type, section, problems);
            }
            foreach (var name in sections.Keys.Where(name => !names.Contains(name)))
                problems.Add($"{page}: \"## {name}\" is not a type of {group.Key}");
        }
        Assert.True(problems.Count == 0, "The reference is out of date:\n" + string.Join("\n", problems));
    }

    private static void CheckEnum(string page, Type type, Section section, List<string> problems)
    {
        var required = Enum.GetNames(type);
        foreach (var name in required.Where(name => !section.Text.Contains($"`{name}`", StringComparison.Ordinal)))
            problems.Add($"{page}: {type.Name} does not name the value {name}");
        var documented = Counts(section.Rows.Select(row => Bare(row.Name)));
        var expected = Counts(required);
        foreach (var (name, count) in expected)
        {
            documented.TryGetValue(name, out var rows);
            if (rows != count)
                problems.Add($"{page}: {type.Name} lists {name} {rows} times, and has {count}");
        }
        foreach (var name in documented.Keys.Where(name => !expected.ContainsKey(name)))
            problems.Add($"{page}: {type.Name} has no value {name}");
    }

    private static void CheckMembers(string page, Type type, Section section, List<string> problems)
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string name)
        {
            expected.TryGetValue(name, out var count);
            expected[name] = count + 1;
        }
        foreach (var property in type.GetProperties(Declared).Where(member => !Skip(member)))
            Add(property.Name);
        foreach (var evt in type.GetEvents(Declared).Where(member => !Skip(member)))
            Add(evt.Name);
        foreach (var field in type.GetFields(Declared).Where(member => !Skip(member)))
            Add(field.Name);
        foreach (var method in type.GetMethods(Declared).Where(method => !method.IsSpecialName && !Skip(method)))
            Add(method.Name);
        var constructors = type.GetConstructors(Declared).Length;
        var constructorRows = section.Rows.Count(row => row.Name == type.Name && row.Cell.Contains('(', StringComparison.Ordinal));
        if (constructorRows != constructors)
            problems.Add($"{page}: {type.Name} lists {constructorRows} constructors, and has {constructors}");
        var documented = Counts(section.Rows
            .Where(row => !(row.Name == type.Name && row.Cell.Contains('(', StringComparison.Ordinal)))
            .Select(row => Bare(row.Name)));
        foreach (var (name, count) in expected)
        {
            documented.TryGetValue(name, out var rows);
            if (rows != count)
                problems.Add($"{page}: {type.Name} lists {name} {rows} times, and has {count}");
        }
        foreach (var name in documented.Keys.Where(name => !expected.ContainsKey(name)))
            problems.Add($"{page}: {type.Name} has no member {name}");
    }

    private static Dictionary<string, int> Counts(IEnumerable<string> names)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            counts.TryGetValue(name, out var count);
            counts[name] = count + 1;
        }
        return counts;
    }

    private static string Bare(string name)
    {
        var angle = name.IndexOf('<');
        return angle < 0 ? name : name[..angle];
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

    private static HashSet<Type> Included(Assembly shell, Assembly services)
    {
        var included = new HashSet<Type>();
        foreach (var type in Exported(shell).Concat(Exported(services)).Where(type => type.IsPublic && !type.IsNested))
        {
            if (Seed(type))
                included.Add(type);
        }
        var queue = new Queue<Type>(included);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
            {
                if (nested.IsNestedPublic && included.Add(nested))
                    queue.Enqueue(nested);
            }
            foreach (var related in Related(type))
            {
                if (related.Assembly != shell && related.Assembly != services)
                    continue;
                if (related.IsGenericParameter || related.Name.StartsWith('<') || related.IsByRef || related.IsPointer || related.Name.Contains('&'))
                    continue;
                if (!related.IsPublic && !related.IsNestedPublic)
                    continue;
                if (included.Add(related))
                    queue.Enqueue(related);
            }
        }
        return included;
    }

    private static bool Seed(Type type)
    {
        if (type.Namespace is null || type.Name.StartsWith('<'))
            return false;
        return type.Namespace == "Winshell" || type.Namespace.StartsWith("Winshell.", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> Related(Type type)
    {
        if (type.BaseType is { } baseType)
            yield return baseType;
        foreach (var property in type.GetProperties(Declared))
            foreach (var atom in Atoms(property.PropertyType))
                yield return atom;
        foreach (var field in type.GetFields(Declared))
            foreach (var atom in Atoms(field.FieldType))
                yield return atom;
        foreach (var evt in type.GetEvents(Declared))
        {
            if (evt.EventHandlerType is not null)
                foreach (var atom in Atoms(evt.EventHandlerType))
                    yield return atom;
        }
        foreach (var method in type.GetMethods(Declared))
        {
            foreach (var atom in Atoms(method.ReturnType))
                yield return atom;
            foreach (var parameter in method.GetParameters())
                foreach (var atom in Atoms(parameter.ParameterType))
                    yield return atom;
        }
        foreach (var ctor in type.GetConstructors(Declared))
        {
            foreach (var parameter in ctor.GetParameters())
                foreach (var atom in Atoms(parameter.ParameterType))
                    yield return atom;
        }
    }

    private static IEnumerable<Type> Atoms(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            foreach (var inner in Atoms(type.GetElementType()!))
                yield return inner;
            yield break;
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                foreach (var inner in Atoms(argument))
                    yield return inner;
            yield break;
        }
        yield return type;
    }

    private static Type[] Exported(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private sealed record Row(string Cell, string Name);

    private sealed record Section(string Text, List<Row> Rows);

    /// <summary>Each <c>## Heading</c> with its text and the names in the first column of its tables.</summary>
    private static Dictionary<string, Section> Sections(string[] lines, List<string> problems, string page)
    {
        var sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal))
                continue;
            var end = i + 1;
            while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal))
                end++;
            var heading = lines[i][3..].Trim();
            var body = lines[(i + 1)..end];
            var rows = new List<Row>();
            foreach (var line in body.Where(line => line.StartsWith("| `", StringComparison.Ordinal)))
            {
                var close = line.IndexOf('`', 3);
                if (close < 0)
                    continue;
                var cell = line[3..close];
                var paren = cell.IndexOf('(');
                rows.Add(new Row(cell, paren < 0 ? cell : cell[..paren]));
            }
            if (!sections.TryAdd(heading, new Section(string.Join('\n', body), rows)))
                problems.Add($"{page}: \"## {heading}\" is repeated");
            i = end - 1;
        }
        return sections;
    }

    /// <summary>The <c>```csharp</c> blocks of every page, with the line each one starts on.</summary>
    private static IEnumerable<(string File, int Line, string Code)> Examples()
    {
        foreach (var file in Pages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```csharp")
                    continue;
                var start = i + 1;
                var end = start;
                while (end < lines.Length && lines[end].Trim() != "```")
                    end++;
                yield return (file, start + 1, string.Join('\n', lines[start..end]));
                i = end;
            }
        }
    }

    /// <summary>The pages the docs site is built from, and the README.</summary>
    private static IEnumerable<string> Pages()
    {
        var docs = Path.Combine(Repo, "docs");
        var skipped = new[] { Path.Combine(docs, "agents"), Path.Combine(docs, "_site") };
        return Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Where(file => !skipped.Any(dir => file.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Append(Path.Combine(Repo, "README.md"))
            .Order(StringComparer.Ordinal);
    }
}

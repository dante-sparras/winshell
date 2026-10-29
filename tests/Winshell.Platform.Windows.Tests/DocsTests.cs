using Winshell.Qml;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The QML examples in <c>docs/</c> and the README stay true to the API: each one parses, imports modules that exist,
/// and uses types, properties and signal handlers those modules have. The default shell's files count as local types,
/// as they do in a copy of that folder.
/// </summary>
public sealed class DocsTests
{
    private static readonly string Repo = FindRepo();
    private static readonly string DefaultShell = Path.Combine(Repo, "src", "Winshell", "DefaultShell");
    private static readonly ModuleRegistry Modules = CreateModules();
    private readonly Dictionary<string, Members?> _localTypes = new(StringComparer.Ordinal);

    [Fact]
    public void Every_qml_example_uses_the_real_api()
    {
        var examples = Examples().ToList();
        Assert.NotEmpty(examples);
        var problems = new List<string>();
        foreach (var (file, line, code) in examples)
        {
            var where = $"{Path.GetRelativePath(Repo, file)}:{line}";
            QmlDocument document;
            try
            {
                document = QmlParser.Parse(code, where);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                problems.Add($"{where}: does not parse: {e.Message}");
                continue;
            }
            var scope = new Scope(this, document, problems, where);
            scope.Check(document.Root);
        }
        Assert.True(problems.Count == 0, "QML examples that do not match the API:\n" + string.Join("\n", problems));
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

    /// <summary>The <c>```qml</c> blocks of every page, with the line each one starts on.</summary>
    internal static IEnumerable<(string File, int Line, string Code)> Examples()
    {
        foreach (var file in Pages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```qml")
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
    internal static IEnumerable<string> Pages()
    {
        var docs = Path.Combine(Repo, "docs");
        var skipped = new[] { Path.Combine(docs, "agents"), Path.Combine(docs, "_site") };
        return Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(s => f.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Append(Path.Combine(Repo, "README.md"))
            .Order(StringComparer.Ordinal);
    }

    internal static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("The tests run outside the repository.");
    }

    internal static ModuleRegistry CreateModules()
    {
        var modules = StandardModules.Create();
        WindowsModules.Register(modules);
        ServiceModules.Register(modules);
        return modules;
    }

    /// <summary>What a type lets a document set: its properties and signals.</summary>
    private sealed record Members(HashSet<string> Properties, HashSet<string> Signals)
    {
        public static Members Of(Type type)
        {
            var meta = MetaType.Of(type);
            return new([.. meta.Properties.Keys], [.. meta.Signals.Keys]);
        }

        public Members With(Members other) => new([.. Properties, .. other.Properties], [.. Signals, .. other.Signals]);
    }

    /// <summary>The members a type in the default shell declares, on top of its root type's.</summary>
    private Members? LocalType(string name)
    {
        if (_localTypes.TryGetValue(name, out var cached))
            return cached;
        _localTypes[name] = null;
        var path = Path.Combine(DefaultShell, name + ".qml");
        if (!File.Exists(path))
            return null;
        var document = QmlParser.Parse(File.ReadAllText(path), path);
        var root = new Scope(this, document, [], path).Resolve(document.Root.TypeName) ?? new Members([], []);
        var members = root.With(Declared(document.Root));
        _localTypes[name] = members;
        return members;
    }

    private static Members Declared(QmlObjectNode node)
    {
        var properties = new HashSet<string>(StringComparer.Ordinal);
        var signals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in node.Members)
        {
            switch (member)
            {
                case QmlPropertyDeclaration p:
                    properties.Add(p.Name);
                    break;
                case QmlRequiredMember r:
                    properties.Add(r.Name);
                    break;
                case QmlSignalDeclaration s:
                    signals.Add(s.Name);
                    break;
            }
        }
        return new(properties, signals);
    }

    private sealed class Scope(DocsTests owner, QmlDocument document, List<string> problems, string where)
    {
        private readonly HashSet<string> _inline = [.. Inline(document.Root)];

        /// <summary>Unchecked imports, such as <c>import "widgets"</c>, can bring in types this test cannot see.</summary>
        private readonly bool _open = document.Imports.Any(i => i.Kind == ImportKind.Directory
            || (i.Kind == ImportKind.Module && !Modules.TryGetModule(i.Uri, out _)));

        public void Check(QmlObjectNode node)
        {
            foreach (var import in document.Imports.Where(i => i.Kind == ImportKind.Module && i.Uri.StartsWith("Winshell", StringComparison.Ordinal)))
            {
                if (!Modules.TryGetModule(import.Uri, out _))
                    problems.Add($"{where}: imports {import.Uri}, which is not a module");
            }
            Visit(node);
        }

        public Members? Resolve(string typeName)
        {
            if (_inline.Contains(typeName))
                return new Members([], []);
            var dot = typeName.LastIndexOf('.');
            var qualifier = dot < 0 ? null : typeName[..dot];
            var name = dot < 0 ? typeName : typeName[(dot + 1)..];
            // A document without imports is a fragment of a larger file: any module may hold the type.
            var modules = document.Imports.Count == 0
                ? Modules.All
                : document.Imports
                    .Where(i => i.Kind == ImportKind.Module && i.Qualifier == qualifier)
                    .Select(i => Modules.TryGetModule(i.Uri, out var m) ? m : null)
                    .OfType<QmlModule>();
            foreach (var module in modules)
            {
                if (module.Types.TryGetValue(name, out var entry) && typeof(QObject).IsAssignableFrom(entry.ClrType))
                    return Members.Of(entry.ClrType);
            }
            return qualifier is null ? owner.LocalType(name) : null;
        }

        private void Visit(QmlObjectNode node)
        {
            var type = Resolve(node.TypeName);
            if (type is null && !_open)
                problems.Add($"{where}: {node.TypeName} is not a type in the modules this example imports");
            var members = type?.With(Declared(node));
            foreach (var member in node.Members)
            {
                switch (member)
                {
                    case QmlAssignment a:
                        CheckName(node.TypeName, members, a.Name);
                        if (a.Value is QmlObjectValue ov)
                            Visit(ov.Object);
                        else if (a.Value is QmlListValue lv)
                            lv.Objects.ToList().ForEach(Visit);
                        break;
                    case QmlGroupedBlock g:
                        CheckName(node.TypeName, members, g.Name);
                        foreach (var child in g.Members.OfType<QmlChildObject>())
                            Visit(child.Object);
                        break;
                    case QmlPropertyDeclaration { Value: QmlObjectValue pv }:
                        Visit(pv.Object);
                        break;
                    case QmlChildObject c:
                        Visit(c.Object);
                        break;
                    case QmlOnAssignment o:
                        Visit(o.Object);
                        break;
                    case QmlInlineComponent ic:
                        Visit(ic.Object);
                        break;
                }
            }
        }

        /// <summary>
        /// A property, or a handler of a signal or of a property's change. Only the first part of a dotted name is
        /// checked: <c>anchors.fill</c> needs <c>anchors</c>, and an attached name such as <c>Layout</c> is a type.
        /// </summary>
        private void CheckName(string typeName, Members? members, string name)
        {
            if (members is null)
                return;
            var first = name.Split('.')[0];
            if (char.IsUpper(first[0]))
                return;
            if (members.Properties.Contains(first))
                return;
            if (Names.SignalOfHandler(first) is { } signal)
            {
                if (members.Signals.Contains(signal))
                    return;
                if (signal.EndsWith("Changed", StringComparison.Ordinal) && members.Properties.Contains(signal[..^"Changed".Length]))
                    return;
                problems.Add($"{where}: {typeName} has no signal {signal} for {first}");
                return;
            }
            problems.Add($"{where}: {typeName} has no property {first}");
        }

        private static IEnumerable<string> Inline(QmlObjectNode node) =>
            node.Members.OfType<QmlInlineComponent>().Select(c => c.Name)
                .Concat(node.Members.OfType<QmlChildObject>().SelectMany(c => Inline(c.Object)));
    }
}

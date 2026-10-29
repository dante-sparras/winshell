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

    /// <summary>
    /// Each <c>Winshell</c> module has a page in <c>docs/reference/</c> with a <c>## Type</c> section for each of its
    /// types. A section's tables list every member the type adds to its nearest registered base type, and nothing the
    /// type doesn't have. Members of an unregistered base class, such as the windows' <c>ShellWindow</c>, may be listed
    /// once in a section named after that class instead. Enum values are named in their section.
    /// </summary>
    [Fact]
    public void The_reference_lists_every_type_and_member()
    {
        var registered = Modules.All.SelectMany(m => m.Types.Values).Select(t => t.ClrType).ToHashSet();
        // Unregistered base classes of registered types, such as ShellWindow, by name.
        var shared = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in registered)
        {
            for (var c = t.BaseType; c is not null && c != typeof(QObject) && !registered.Contains(c); c = c.BaseType)
                shared.TryAdd(c.Name, c);
        }
        var problems = new List<string>();
        foreach (var module in Modules.All.Where(m => m.Name.StartsWith("Winshell", StringComparison.Ordinal)))
        {
            var page = Path.Combine("docs", "reference", module.Name.ToLowerInvariant().Replace('.', '-') + ".md");
            var path = Path.Combine(Repo, page);
            if (!File.Exists(path))
            {
                problems.Add($"{module.Name} has no page {page}");
                continue;
            }
            var lines = File.ReadAllLines(path);
            if (lines.FirstOrDefault() != "# " + module.Name)
                problems.Add($"{page} does not start with \"# {module.Name}\"");
            var sections = Sections(lines);
            foreach (var type in module.Types.Values)
            {
                if (!sections.TryGetValue(type.Name, out var section))
                {
                    problems.Add($"{page} has no \"## {type.Name}\" section");
                    continue;
                }
                var values = typeof(QObject).IsAssignableFrom(type.ClrType) ? OwnEnums(type, registered) : type.Enums.Keys;
                foreach (var value in values.Where(v => !section.Text.Contains($"`{v}`", StringComparison.Ordinal)))
                    problems.Add($"{page}: {type.Name} does not name the value {value}");
                if (!typeof(QObject).IsAssignableFrom(type.ClrType))
                {
                    foreach (var member in section.Members.Where(m => !type.Enums.ContainsKey(m)))
                        problems.Add($"{page}: {type.Name} has no value {member}");
                    continue;
                }
                var allowed = MemberNames(type.ClrType).Concat(type.AttachedType is { } a ? MemberNames(a) : [])
                    .Concat(shared.TryGetValue(type.Name, out var b) ? MemberNames(b) : []).Concat(type.Enums.Keys).ToHashSet();
                foreach (var member in section.Members.Where(m => !allowed.Contains(m)))
                    problems.Add($"{page}: {type.Name} has no member {member}");
                for (var c = type.ClrType; c is not null && c != typeof(QObject) && (c == type.ClrType || !registered.Contains(c)); c = c.BaseType)
                {
                    var common = c == type.ClrType ? null : sections.GetValueOrDefault(c.Name);
                    foreach (var member in DeclaredMembers(c))
                    {
                        if (!section.Members.Contains(member) && common?.Members.Contains(member) != true)
                            problems.Add($"{page}: {type.Name} does not list {member}");
                    }
                }
            }
            // A section about a shared base class must be about one, so its members are checked too.
            foreach (var (name, section) in sections.Where(s => !module.Types.ContainsKey(s.Key) && s.Value.Members.Count > 0))
                problems.Add($"{page}: \"## {name}\" is not a type of {module.Name}");
        }
        CheckQtQuick(problems);
        Assert.True(problems.Count == 0, "The reference is out of date:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// <c>qt-quick.md</c> names every Qt Quick type, its tables name only real ones, and each row of its differences
    /// table is a real <c>Type.member</c>.
    /// </summary>
    private static void CheckQtQuick(List<string> problems)
    {
        const string page = @"docs\reference\qt-quick.md";
        var qt = new Dictionary<string, TypeEntry>(StringComparer.Ordinal);
        foreach (var entry in Modules.All.Where(m => m.Name.StartsWith("Qt", StringComparison.Ordinal)).SelectMany(m => m.Types.Values))
            qt.TryAdd(entry.Name, entry);
        var lines = File.ReadAllLines(Path.Combine(Repo, "docs", "reference", "qt-quick.md"));
        var text = string.Join('\n', lines);
        foreach (var type in qt.Keys.Where(t => !text.Contains($"`{t}`", StringComparison.Ordinal)))
            problems.Add($"{page} does not name {type}");
        var sections = Sections(lines);
        foreach (var (name, section) in sections)
        {
            var differences = name == "Where Winshell differs from Qt";
            foreach (var row in section.Text.Split('\n').Where(l => l.StartsWith('|') && !l.StartsWith("| ---", StringComparison.Ordinal)))
            {
                var cells = row.Split('|');
                if (differences && cells[1].Trim() is ['`', .. var cell, '`'])
                {
                    var parts = cell.TrimEnd(')').TrimEnd('(').Split('.');
                    if (parts.Length != 2 || !qt.TryGetValue(parts[0], out var entry))
                        problems.Add($"{page}: {cell} is not a Qt Quick type's member");
                    else if (!MemberNames(entry.ClrType).Concat(entry.AttachedType is { } a ? MemberNames(a) : []).Contains(parts[1]))
                        problems.Add($"{page}: {parts[0]} has no member {parts[1]}");
                    continue;
                }
                foreach (var token in cells.Skip(1).SelectMany(c => c.Split('`').Where((_, i) => i % 2 == 1)))
                {
                    if (token.Length > 0 && char.IsUpper(token[0]) && token.All(char.IsLetterOrDigit) && !qt.ContainsKey(token))
                        problems.Add($"{page}: {token} is not a Qt Quick type");
                }
            }
        }
    }

    private sealed record Section(string Text, HashSet<string> Members);

    /// <summary>Each <c>## Heading</c> with its text and the names in the first column of its tables.</summary>
    private static Dictionary<string, Section> Sections(string[] lines)
    {
        var sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal))
                continue;
            var end = i + 1;
            while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal))
                end++;
            var body = lines[(i + 1)..end];
            var members = body.Where(l => l.StartsWith("| `", StringComparison.Ordinal))
                .Select(l => l[3..l.IndexOf('`', 3)])
                .Select(m => m.Split('(')[0])
                .ToHashSet(StringComparer.Ordinal);
            sections[lines[i][3..].Trim()] = new(string.Join('\n', body), members);
            i = end - 1;
        }
        return sections;
    }

    private static IEnumerable<string> MemberNames(Type type)
    {
        var meta = MetaType.Of(type);
        return meta.Properties.Keys.Concat(meta.Methods.Keys).Concat(meta.Signals.Keys);
    }

    /// <summary>The members a class adds to its base class.</summary>
    private static IEnumerable<string> DeclaredMembers(Type type)
    {
        var inherited = type.BaseType is { } b && typeof(QObject).IsAssignableFrom(b) ? MemberNames(b).ToHashSet() : [];
        return MemberNames(type).Where(m => !inherited.Contains(m));
    }

    /// <summary>The enum values a type adds to its nearest registered base type, such as <c>PanelWindow.Ignore</c>.</summary>
    private static IEnumerable<string> OwnEnums(TypeEntry type, HashSet<Type> registered)
    {
        var b = type.ClrType.BaseType;
        while (b is not null && b != typeof(QObject) && !registered.Contains(b))
            b = b.BaseType;
        var inherited = b is null || b == typeof(QObject) ? [] : MetaType.Of(b).Enums.Keys.ToHashSet();
        return type.Enums.Keys.Where(k => !inherited.Contains(k));
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

        /// <summary>
        /// A folder of the configuration, <c>import "widgets"</c> or <c>import config.widgets</c>, can bring in types
        /// this test cannot see. Members of the types it can see are still checked.
        /// </summary>
        private readonly bool _open = document.Imports.Any(i => i.Kind == ImportKind.Directory
            || (i.Kind == ImportKind.Module && IsConfigFolder(i.Uri)));

        public void Check(QmlObjectNode node)
        {
            foreach (var import in document.Imports.Where(i => i.Kind == ImportKind.Module && !IsConfigFolder(i.Uri)))
            {
                if (!Modules.TryGetModule(import.Uri, out _))
                    problems.Add($"{where}: imports {import.Uri}, which is not a module");
            }
            Visit(node);
        }

        /// <summary><c>config</c> and <c>config.*</c> are folders of the configuration, as the runtime resolves them.</summary>
        private static bool IsConfigFolder(string uri) => uri == "config" || uri.StartsWith("config.", StringComparison.Ordinal);

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

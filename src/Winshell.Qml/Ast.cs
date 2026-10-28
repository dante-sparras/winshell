namespace Winshell.Qml;

/// <summary>A position in a QML file. Line and column are 1-based.</summary>
public readonly record struct SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}[{Line}:{Column}]";
}

/// <summary>A parsed QML file.</summary>
public sealed record QmlDocument(
    string File,
    IReadOnlyList<QmlPragma> Pragmas,
    IReadOnlyList<QmlImport> Imports,
    QmlObjectNode Root)
{
    public bool IsSingleton => Pragmas.Any(p => p.Name == "Singleton");

    public string? Pragma(string name) => Pragmas.FirstOrDefault(p => p.Name == name)?.Value;
}

public sealed record QmlPragma(string Name, string? Value, SourceLocation Location);

public enum ImportKind
{
    /// <summary><c>import QtQuick</c> or <c>import qs.widgets</c>.</summary>
    Module,

    /// <summary><c>import "dir"</c>.</summary>
    Directory,

    /// <summary><c>import "file.js" as Name</c>.</summary>
    Script,
}

public sealed record QmlImport(ImportKind Kind, string Uri, string? Version, string? Qualifier, SourceLocation Location);

/// <summary>An object definition: <c>Type { members }</c>.</summary>
public sealed record QmlObjectNode(string TypeName, IReadOnlyList<QmlMember> Members, SourceLocation Location)
{
    public string? Id => Members.OfType<QmlIdMember>().FirstOrDefault()?.Id;
}

public abstract record QmlMember(SourceLocation Location);

public sealed record QmlIdMember(string Id, SourceLocation Location) : QmlMember(Location);

/// <summary><c>name: value</c>, <c>a.b: value</c>, <c>onClicked: ...</c> or <c>Layout.fillWidth: ...</c>.</summary>
public sealed record QmlAssignment(string Name, QmlValue Value, SourceLocation Location) : QmlMember(Location);

/// <summary><c>anchors { fill: parent }</c>: assignments to the members of a grouped or attached property.</summary>
public sealed record QmlGroupedBlock(string Name, IReadOnlyList<QmlMember> Members, SourceLocation Location) : QmlMember(Location);

/// <summary><c>[required] [readonly] [default] property type name[: value]</c>, including <c>property alias</c>.</summary>
public sealed record QmlPropertyDeclaration(
    string Name,
    string Type,
    bool IsRequired,
    bool IsReadonly,
    bool IsDefault,
    QmlValue? Value,
    SourceLocation Location) : QmlMember(Location)
{
    public bool IsAlias => Type == "alias";
}

/// <summary><c>required name</c>: marks an inherited property as required.</summary>
public sealed record QmlRequiredMember(string Name, SourceLocation Location) : QmlMember(Location);

public sealed record QmlParameter(string Name, string? Type);

public sealed record QmlSignalDeclaration(string Name, IReadOnlyList<QmlParameter> Parameters, SourceLocation Location) : QmlMember(Location);

/// <summary>A function. <see cref="Body"/> is the JavaScript between the braces.</summary>
public sealed record QmlFunctionDeclaration(
    string Name,
    IReadOnlyList<QmlParameter> Parameters,
    string Body,
    SourceLocation BodyLocation,
    SourceLocation Location) : QmlMember(Location);

/// <summary>An object placed in the parent's default property.</summary>
public sealed record QmlChildObject(QmlObjectNode Object, SourceLocation Location) : QmlMember(Location);

/// <summary><c>Behavior on x { }</c>: a value source or interceptor for one property.</summary>
public sealed record QmlOnAssignment(string Property, QmlObjectNode Object, SourceLocation Location) : QmlMember(Location);

/// <summary><c>component Name: Type { }</c>.</summary>
public sealed record QmlInlineComponent(string Name, QmlObjectNode Object, SourceLocation Location) : QmlMember(Location);

public sealed record QmlEnumDeclaration(string Name, IReadOnlyList<KeyValuePair<string, int>> Values, SourceLocation Location) : QmlMember(Location);

public abstract record QmlValue(SourceLocation Location);

/// <summary>JavaScript. <see cref="IsBlock"/> is true for <c>{ statements }</c>, whose code excludes the outer braces.</summary>
public sealed record QmlScriptValue(string Code, bool IsBlock, SourceLocation Location) : QmlValue(Location);

public sealed record QmlObjectValue(QmlObjectNode Object, SourceLocation Location) : QmlValue(Location);

/// <summary><c>[ A {}, B {} ]</c>: a list of object definitions.</summary>
public sealed record QmlListValue(IReadOnlyList<QmlObjectNode> Objects, SourceLocation Location) : QmlValue(Location);

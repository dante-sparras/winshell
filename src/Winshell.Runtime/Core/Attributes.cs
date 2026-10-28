namespace Winshell.Runtime;

/// <summary>
/// Exposes a C# property or method to QML. The QML name is the C# name with its first letter lowered,
/// unless <see cref="Name"/> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = true)]
public sealed class QmlAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>Declares a signal on a QML type, with the names of its parameters.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class QmlSignalAttribute(string name, params string[] parameters) : Attribute
{
    public string Name { get; } = name;

    public string[] Parameters { get; } = parameters;
}

/// <summary>Names the property that child objects without a property name are assigned to.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class QmlDefaultPropertyAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Marks a nested enum whose members are exposed on the type, such as <c>Text.AlignLeft</c>.</summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class QmlEnumAttribute : Attribute;

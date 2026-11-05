using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace Winshell.Architecture.Tests;

/// <summary>
/// Only the Services feature that owns a backend uses it: its <c>I*Backend</c> interface and the types that implement
/// it. Other features and Shell use the service. The feature is the backend interface's namespace.
/// </summary>
public partial class FeatureBackendTests
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Dictionary<short, OperandType> Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value, code => code.OperandType);

    [Fact]
    public void Only_the_owning_feature_uses_a_backend()
    {
        var services = Assembly.Load("Winshell.Services");
        var backends = services.GetTypes().Where(type => type.IsInterface && BackendName().IsMatch(type.Name)).ToList();
        Assert.NotEmpty(backends);
        var owners = services.GetTypes()
            .SelectMany(type => backends.Where(backend => backend.IsAssignableFrom(type)).Select(backend => (type, backend.Namespace)))
            .ToLookup(pair => pair.type, pair => pair.Namespace);

        var crossings = new[] { services, Assembly.Load("Winshell.Shell") }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(user => Uses(user).SelectMany(Parts).Distinct()
                .Where(used => owners.Contains(used) && !(user.Assembly == services && owners[used].Contains(user.Namespace)))
                .Select(used => $"{user.Assembly.GetName().Name}: {user.FullName} uses {used.FullName}"))
            .Distinct()
            .ToList();

        Assert.True(crossings.Count == 0, string.Join(Environment.NewLine, crossings));
    }

    [GeneratedRegex("^I[A-Z].*Backend$")]
    private static partial Regex BackendName();

    // What a type's declaration, signatures and method bodies name. A property or event is its accessor methods.
    private static IEnumerable<Type> Uses(Type type)
    {
        if (type.BaseType is { } baseType)
            yield return baseType;
        foreach (var contract in type.GetInterfaces())
            yield return contract;
        foreach (var field in type.GetFields(Declared))
            yield return field.FieldType;
        foreach (var method in type.GetMethods(Declared).Concat<MethodBase>(type.GetConstructors(Declared)))
        {
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
            if (method is MethodInfo info)
                yield return info.ReturnType;
            if (method.GetMethodBody() is not { } body)
                continue;
            foreach (var local in body.LocalVariables)
                yield return local.LocalType;
            var typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            foreach (var token in Tokens(body.GetILAsByteArray()!))
            {
                switch (type.Module.ResolveMember(token, typeArguments, methodArguments))
                {
                    case Type named:
                        yield return named;
                        break;
                    case FieldInfo field:
                        yield return field.DeclaringType!;
                        yield return field.FieldType;
                        break;
                    // A call names what it calls, its type arguments and what it gets back. Its parameters are no use:
                    // a backend passed in was named where it was made or found. Shell builds a service with its
                    // optional backend parameter left out, so the service builds its own.
                    case MethodInfo called:
                        yield return called.DeclaringType!;
                        yield return called.ReturnType;
                        foreach (var argument in called.GetGenericArguments())
                            yield return argument;
                        break;
                    case MethodBase constructed:
                        yield return constructed.DeclaringType!;
                        break;
                }
            }
        }
    }

    // The metadata tokens the instructions name: types, fields and methods.
    private static IEnumerable<int> Tokens(byte[] il)
    {
        for (var i = 0; i < il.Length;)
        {
            var twoBytes = il[i] == 0xFE;
            var operand = Operands[twoBytes ? (short)(0xFE00 | il[i + 1]) : il[i]];
            i += twoBytes ? 2 : 1;
            if (operand is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                yield return BitConverter.ToInt32(il, i);
            i += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
        }
    }

    // A type with its element and generic argument types: a List<IAppsBackend> uses IAppsBackend.
    private static IEnumerable<Type> Parts(Type type) =>
        type.HasElementType ? Parts(type.GetElementType()!)
        : type.IsConstructedGenericType ? type.GetGenericArguments().SelectMany(Parts).Prepend(type.GetGenericTypeDefinition())
        : [type];
}

using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace Winshell;

/// <summary>
/// The WinUI application every shell runs in. A configuration has no App.xaml, so this finds the XAML type
/// information its assemblies were compiled with, which a custom control in XAML is looked up in.
/// </summary>
internal sealed class ShellApplication(Action started) : Application, IXamlMetadataProvider
{
    private readonly List<IXamlMetadataProvider> _providers =
        [new XamlControlsXamlMetaDataProvider(), .. CompiledProviders()];

    public IXamlType GetXamlType(Type type)
    {
        return _providers.Select(p => p.GetXamlType(type)).FirstOrDefault(t => t is not null)!;
    }

    public IXamlType GetXamlType(string fullName)
    {
        return _providers.Select(p => p.GetXamlType(fullName)).FirstOrDefault(t => t is not null)!;
    }

    public XmlnsDefinition[] GetXmlnsDefinitions()
    {
        return [.. _providers.SelectMany(p => p.GetXmlnsDefinitions())];
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        started();
    }

    private static IEnumerable<IXamlMetadataProvider> CompiledProviders()
    {
        if (Assembly.GetEntryAssembly() is not { } entry)
            yield break;
        var assemblies = entry.GetReferencedAssemblies()
            .Where(a => a.Name is { } name && !name.StartsWith("Microsoft.", StringComparison.Ordinal) &&
                        !name.StartsWith("System", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Prepend(entry);
        foreach (var assembly in assemblies)
        foreach (var type in assembly.GetExportedTypes())
            if (type.Name == "XamlMetaDataProvider" && typeof(IXamlMetadataProvider).IsAssignableFrom(type) &&
                !type.IsAbstract)
                yield return (IXamlMetadataProvider)Activator.CreateInstance(type)!;
    }
}

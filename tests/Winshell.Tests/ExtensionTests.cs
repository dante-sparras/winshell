using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

/// <summary>The shell loads the extensions its settings name, and skips a path or a type that will not construct.</summary>
[TestClass]
public sealed class ExtensionTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Delete() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void ResolveLoaded_returns_an_assembly_the_process_already_has()
    {
        ExtensionLoader.ResolveLoaded(new System.Reflection.AssemblyName("Winshell.Core"))
            .Should().BeSameAs(typeof(ExtensionLoader).Assembly);
    }

    [TestMethod]
    public void ResolveLoaded_returns_null_when_the_process_has_no_such_assembly()
    {
        ExtensionLoader.ResolveLoaded(new System.Reflection.AssemblyName("No.Such.Assembly")).Should().BeNull();
    }

    [TestMethod]
    public void An_extension_uses_the_shell_copy_of_an_assembly_the_shell_can_load()
    {
        var context = new ExtensionLoader.ExtensionLoadContext(typeof(ExtensionLoader).Assembly.Location);
        var loaded = context.LoadNamed(new System.Reflection.AssemblyName("System.IO.Compression"));

        loaded.Should().NotBeNull();
        System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies.Should().Contain(loaded!);
    }

    [TestMethod]
    public void An_extension_keeps_an_assembly_the_shell_cannot_load()
    {
        var context = new ExtensionLoader.ExtensionLoadContext(typeof(ExtensionLoader).Assembly.Location);
        context.LoadNamed(new System.Reflection.AssemblyName("NAudio.Wasapi")).Should().BeNull();
    }

    [TestMethod]
    public void A_missing_key_or_a_null_lists_nothing()
    {
        var notes = new List<string>();
        ExtensionList.Read(Object("""{ "taskbar": "hidden" }"""), notes).Should().BeEmpty();
        ExtensionList.Read(Object("""{ "extensions": null }"""), notes).Should().BeEmpty();
        notes.Should().BeEmpty();
    }

    [TestMethod]
    public void A_list_keeps_paths_and_notes_the_rest()
    {
        var notes = new List<string>();
        var paths = ExtensionList.Read(Object("""{ "extensions": ["Winoma.Desktop.dll", "", 1, "  ", "other.dll"] }"""), notes);

        paths.Should().Equal("Winoma.Desktop.dll", "other.dll");
        notes.Should().Equal("extensions entry is empty", "extensions entry is not a path", "extensions entry is empty");
    }

    [TestMethod]
    public void A_value_that_is_not_a_list_is_a_note()
    {
        var notes = new List<string>();
        ExtensionList.Read(Object("""{ "extensions": "Winoma.Desktop.dll" }"""), notes).Should().BeEmpty();
        notes.Should().Equal("extensions is not a list");
    }

    [TestMethod]
    public void Load_constructs_the_public_extensions_and_notes_the_rest()
    {
        var location = typeof(SampleExtension).Assembly.Location;
        var loaded = Load([location], _root);

        loaded.Extensions.Select(extension => extension.Name).Should().BeEquivalentTo("sample", "fill", "extra");
        var sample = loaded.Extensions.Should().ContainSingle(extension => extension.Name == "sample").Subject;
        sample.Surfaces.Should().Equal("about");
        sample.Globals.Should().Equal("winoma:about");
        sample.Facts()!["ready"].Should().Be(true);
        sample.FillsSurfaces.Should().BeFalse();
        sample.TryHandleGlobal("winoma:about", down: true, repeat: false).Should().BeTrue();
        sample.TryHandleGlobal("winoma:about", down: false, repeat: false).Should().BeFalse();
        var fillers = loaded.Extensions.Where(extension => extension.FillsSurfaces).ToList();
        fillers.Should().HaveCount(2);
        ShellExtensions.Filler(loaded.Extensions).Should().BeSameAs(fillers[0]);
        loaded.Notes.Should().Contain(note => note.Contains("UnbuiltExtension", StringComparison.Ordinal) && note.Contains("no public constructor", StringComparison.Ordinal));
        loaded.Notes.Should().Contain(note => note.Contains("BrokenExtension", StringComparison.Ordinal) && note.Contains("broken", StringComparison.Ordinal));
        loaded.Notes.Should().Contain(note => note.StartsWith("second desktop ", StringComparison.Ordinal) && note.EndsWith("; the shell opens the first", StringComparison.Ordinal));
        loaded.Notes.Should().NotContain(note => note.Contains("HiddenExtension", StringComparison.Ordinal));
        foreach (var extension in loaded.Extensions)
        {
            var giveBack = extension.GiveBack;
            giveBack.Should().NotThrow();
        }
    }

    [TestMethod]
    public void A_missing_file_is_a_note()
    {
        var missing = Path.Combine(_root, "gone.dll");
        var loaded = Load([missing], _root);

        loaded.Extensions.Should().BeEmpty();
        loaded.Notes.Should().Equal("missing extension " + Path.GetFullPath(missing));
    }

    [TestMethod]
    public void A_file_that_is_not_an_assembly_is_a_note()
    {
        var file = Path.Combine(_root, "not-an-extension.dll");
        File.WriteAllText(file, "not an assembly");

        var loaded = Load([file], _root);

        loaded.Extensions.Should().BeEmpty();
        loaded.Notes.Should().ContainSingle().Which.Should().StartWith("extension is not a shell extension " + Path.GetFullPath(file));
    }

    [TestMethod]
    public void A_relative_path_is_read_beside_the_program()
    {
        var location = typeof(SampleExtension).Assembly.Location;
        var loaded = Load([Path.GetFileName(location)], Path.GetDirectoryName(location)!);

        loaded.Extensions.Should().Contain(extension => extension.Name == "sample");
        loaded.Notes.Should().NotContain(note => note.Contains("missing extension", StringComparison.Ordinal));
    }

    [TestMethod]
    public void The_first_filler_is_the_one_the_shell_opens()
    {
        IShellExtension[] extensions = [new SampleExtension(), new ExtraFiller(), new SampleFiller()];
        ShellExtensions.Filler(extensions).Should().BeSameAs(extensions[1]);
        ShellExtensions.Filler([new SampleExtension()]).Should().BeNull();
    }

    [TestMethod]
    public void An_empty_list_loads_nothing()
    {
        var loaded = Load([], _root);
        loaded.Extensions.Should().BeEmpty();
        loaded.Notes.Should().BeEmpty();
        ShellExtensions.Filler(loaded.Extensions).Should().BeNull();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "The test loads a known assembly.")]
    private static ExtensionLoader.LoadResult Load(IReadOnlyList<string> paths, string baseDirectory) =>
        ExtensionLoader.Load(paths, baseDirectory);

    private static JsonElement Object(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>A stand-in so each fixture only says what it changes.</summary>
public abstract class ExtensionStub : IShellExtension
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Surfaces => [];

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Globals => [];

    /// <inheritdoc />
    public virtual IPluginServiceSource? Services => null;

    /// <inheritdoc />
    public virtual void Start(IShellHost host)
    {
    }

    /// <inheritdoc />
    public virtual void Stop()
    {
    }

    /// <inheritdoc />
    public virtual bool TryHandleGlobal(string name, bool down, bool repeat) => false;

    /// <inheritdoc />
    public virtual IReadOnlyDictionary<string, object?>? Facts() => null;

    /// <inheritdoc />
    public virtual bool FillsSurfaces => false;

    /// <inheritdoc />
    public virtual void OpenDesktop(IShellHost host)
    {
    }
}

/// <summary>An extension the loader can construct.</summary>
public sealed class SampleExtension : ExtensionStub
{
    /// <inheritdoc />
    public override string Name => "sample";

    /// <inheritdoc />
    public override IReadOnlyList<string> Surfaces => ["about"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Globals => ["winoma:about"];

    /// <inheritdoc />
    public override bool TryHandleGlobal(string name, bool down, bool repeat) => name == "winoma:about" && down;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?>? Facts() => new Dictionary<string, object?> { ["ready"] = true };
}

/// <summary>A type the loader skips because it cannot construct it.</summary>
public sealed class UnbuiltExtension : ExtensionStub
{
    private UnbuiltExtension()
    {
    }

    /// <inheritdoc />
    public override string Name => "unbuilt";
}

/// <summary>A type whose constructor fails.</summary>
public sealed class BrokenExtension : ExtensionStub
{
    /// <summary>Fails on purpose, so the loader notes it and continues.</summary>
    public BrokenExtension() => throw new InvalidOperationException("broken");

    /// <inheritdoc />
    public override string Name => "broken";
}

/// <summary>The first extension that fills the surfaces. The shell would open this one.</summary>
public sealed class SampleFiller : ExtensionStub
{
    /// <inheritdoc />
    public override string Name => "fill";

    /// <inheritdoc />
    public override bool FillsSurfaces => true;
}

/// <summary>A second extension that fills the surfaces. The loader notes it and still returns it.</summary>
public sealed class ExtraFiller : ExtensionStub
{
    /// <inheritdoc />
    public override string Name => "extra";

    /// <inheritdoc />
    public override bool FillsSurfaces => true;
}

/// <summary>Not public, so the loader does not construct it.</summary>
internal sealed class HiddenExtension : ExtensionStub
{
    /// <inheritdoc />
    public override string Name => "hidden";
}

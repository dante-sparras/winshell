namespace Winshell.Tests;

/// <summary>Reads what <c>dotnet watch</c> really printed, from a run on a WinUI shell.</summary>
public sealed class DevOutputTests
{
    private const string Broken = @"C:\Shells\Mine\Marker.cs(4,45): error CS1002: ; expected";

    private const string Shown = "Marker.cs(4,45): error CS1002: ; expected";

    private readonly DevOutput _output = new(@"C:\Shells\Mine");

    [Theory]
    [InlineData("dotnet watch : File updated: .\\Marker.cs")]
    [InlineData("dotnet watch : Waiting for changes")]
    [InlineData("dotnet watch : No managed code changes to apply.")]
    [InlineData("dotnet watch : Restart is needed to apply the changes.")]
    [InlineData(" INFO shell: Shell ID: \"Mine\"")]
    [InlineData("  Mine -> C:\\Shells\\Mine\\bin\\Debug\\Mine.dll")]
    [InlineData(" WARN notes: the word error: appears in this log line")]
    public void A_line_that_is_not_a_result_changes_nothing_on_screen(string line) =>
        Assert.Null(_output.Read(line));

    [Fact]
    public void A_compile_error_in_an_edit_is_what_the_notice_says_from_the_project_folder()
    {
        _output.Read("dotnet watch : Unable to apply changes due to compilation errors.");

        Assert.Equal(Shown, _output.Read("dotnet watch : " + Broken));
    }

    [Theory]
    [InlineData(@"C:\Shared\Lib.cs(1,1): error CS0103: The name 'x' does not exist")]
    [InlineData(@"C:\Shells\Mine2\Lib.cs(1,1): error CS0103: The name 'x' does not exist")]
    public void An_error_outside_the_project_keeps_its_full_path(string line) =>
        Assert.Equal(line, _output.Read(line));

    [Fact]
    public void A_compile_error_in_a_full_build_loses_its_project_and_comes_once()
    {
        const string line = Broken + @" [C:\Shells\Mine\Mine.csproj::TargetFramework=net10.0-windows10.0.22621.0]";

        Assert.Equal(Shown, _output.Read("  " + line));
        Assert.Null(_output.Read("  " + line));
    }

    [Fact]
    public void A_xaml_compiler_error_is_what_the_notice_says()
    {
        const string line = @"C:\Shells\Mine\PanelView.xaml(2,5): XamlCompiler error WMC0130: Invalid value for 'PanelView'.";

        Assert.Equal("PanelView.xaml(2,5): XamlCompiler error WMC0130: Invalid value for 'PanelView'.", _output.Read(line));
    }

    [Fact]
    public void A_second_error_joins_the_first_and_the_list_stops_at_five()
    {
        var shown = Enumerable.Range(1, 7).Select(i => _output.Read($"A.cs({i},1): error CS0103: No name {i}")).ToList();

        Assert.Equal("A.cs(1,1): error CS0103: No name 1\nA.cs(2,1): error CS0103: No name 2", shown[1]);
        Assert.Equal(5, shown[4]!.Split('\n').Length);
        Assert.Equal([null, null], shown[5..]);
    }

    [Fact]
    public void An_edit_that_still_fails_replaces_the_errors_of_the_last_one()
    {
        _output.Read("dotnet watch : " + Broken);

        _output.Read("dotnet watch : Unable to apply changes due to compilation errors.");

        Assert.Equal("B.cs(1,1): error CS1513: } expected", _output.Read("dotnet watch : B.cs(1,1): error CS1513: } expected"));
    }

    [Theory]
    [InlineData("dotnet watch : C# and Razor changes applied in 103ms.")]
    [InlineData("Build succeeded.")]
    public void Code_that_applies_takes_the_notice_away_and_forgets_the_errors(string line)
    {
        _output.Read("dotnet watch : " + Broken);

        Assert.Equal("", _output.Read(line));
        Assert.Equal(Shown, _output.Read("dotnet watch : " + Broken));
    }
}

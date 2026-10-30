using System.Globalization;

namespace Winshell.Runtime.Tests;

// Temporary, to show the stress step failing on a real pull request. Removed before review.
public class StressProofTests
{
    [Fact]
    public void Stays_green() => Assert.True(true);

    [Fact]
    public void Fails_on_its_third_run()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-stress-proof.txt");
        var runs = File.Exists(path) ? int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture) : 0;
        File.WriteAllText(path, (runs + 1).ToString(CultureInfo.InvariantCulture));
        Assert.NotEqual(2, runs);
    }
}

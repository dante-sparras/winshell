using Winshell.Platform.Windows.Services.Power;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Reads the battery, the power mode, and monitor brightness. It never writes them.
/// </summary>
public sealed class PowerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory;
    private readonly ManualEventLoop _loop = new();
    private readonly QmlEngine _engine;
    private readonly QObject _root;

    public PowerTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "shell.qml"), """
            import QtQuick
            import Winshell.Power
            Item {
              property var power: Power
              property var display: Power.displayDevice
              property string deviceId: Power.displayDevice.deviceId
              property bool powersTheSystem: Power.displayDevice.powersTheSystem
              property real remaining: Power.displayDevice.remainingCapacity
              property real full: Power.displayDevice.fullChargeCapacity
              property real rate: Power.displayDevice.chargeRate
              property bool systemBattery: Power.displayDevice.isSystemBattery
              property string icon: Power.displayDevice.iconName
              property var mode: PowerMode
              property int bestEfficiency: PowerModeLevel.BestEfficiency
              property int balanced: PowerModeLevel.Balanced
              property int bestPerformance: PowerModeLevel.BestPerformance
              property int energySaver: EffectivePowerMode.EnergySaver
              property int effectiveBalanced: EffectivePowerMode.Balanced
              property var brightness: Brightness
              property int internalPanel: BrightnessConnection.Internal
              property int externalMonitor: BrightnessConnection.External
              IdleMonitor { id: idle }
              property bool respectKeepAwake: idle.respectKeepAwake
            }
            """);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var log = new Log { Sink = (_, category, message) => output.WriteLine($"{category}: {message}") };
        _engine = new QmlEngine(registry, _loop, log, _directory);
        _root = _engine.CreateRoot(Path.Combine(_directory, "shell.qml"));
        _loop.Process();
    }

    [Fact]
    public async Task Battery_power_mode_and_brightness_match_windows()
    {
        Assert.Equal(PowerModeLevel.BestEfficiency, PowerModeBackend.FromGuid(PowerModeBackend.BestEfficiency));
        Assert.Equal(PowerModeLevel.Balanced, PowerModeBackend.FromGuid(Guid.Empty));
        Assert.Equal(PowerModeLevel.BestPerformance, PowerModeBackend.FromGuid(PowerModeBackend.BestPerformance));
        Assert.Equal(PowerModeLevel.BestPerformance, PowerModeBackend.FromGuid(PowerModeBackend.DeprecatedPerformance));
        Assert.Equal(PowerModeBackend.BestEfficiency, PowerModeBackend.ToGuid(PowerModeLevel.BestEfficiency));
        Assert.Equal(Guid.Empty, PowerModeBackend.ToGuid(PowerModeLevel.Balanced));
        Assert.Equal(PowerModeBackend.BestPerformance, PowerModeBackend.ToGuid(PowerModeLevel.BestPerformance));
        Assert.NotEqual(PowerModeBackend.DeprecatedPerformance, PowerModeBackend.ToGuid(PowerModeLevel.BestPerformance));
        Assert.Equal(0, _root.ReadProperty("bestEfficiency"));
        Assert.Equal(1, _root.ReadProperty("balanced"));
        Assert.Equal(2, _root.ReadProperty("bestPerformance"));
        Assert.Equal(0, _root.ReadProperty("energySaver"));
        Assert.Equal(2, _root.ReadProperty("effectiveBalanced"));
        Assert.Equal(0, _root.ReadProperty("internalPanel"));
        Assert.Equal(1, _root.ReadProperty("externalMonitor"));
        Assert.True((bool)_root.ReadProperty("respectKeepAwake")!);

        var display = (PowerDevice)_root.ReadProperty("display")!;
        await Wait.UntilAsync(() => display.Ready, "Power did not become ready.", _loop.Process);
        _output.WriteLine(
            $"battery present {display.IsPresent} state {display.State} charge {display.Percentage:0.###} " +
            $"remaining {display.RemainingCapacity} full {display.FullChargeCapacity} rate {display.ChargeRate} " +
            $"system {display.IsSystemBattery} icon {display.IconName} onBattery {((PowerService)_root.ReadProperty("power")!).OnBattery}");
        Assert.InRange(display.Percentage, 0, 1);
        Assert.False(display.IconName.Contains("symbolic", StringComparison.Ordinal));
        Assert.True(Enum.IsDefined(display.State));
        if (display.IsPresent)
        {
            Assert.True(display.IsSystemBattery);
            Assert.True(display.PowersTheSystem);
        }

        var mode = (PowerModeService)_root.ReadProperty("mode")!;
        await Wait.UntilAsync(() => mode.Ready, "The power mode did not become ready.", _loop.Process);
        _output.WriteLine($"power mode {mode.Mode} effective {mode.Effective} available {mode.Available}");
        Assert.True(mode.Available);
        Assert.True(Enum.IsDefined(mode.Mode));
        Assert.True(Enum.IsDefined(mode.Effective));

        var brightness = (BrightnessService)_root.ReadProperty("brightness")!;
        await Wait.UntilAsync(() => brightness.Ready, "Brightness did not become ready.", _loop.Process);
        foreach (var monitor in brightness.Monitors.Values.Cast<BrightnessMonitor>())
        {
            _output.WriteLine(
                $"monitor {monitor.Name} {monitor.Connection} {monitor.DeviceName} " +
                $"brightness {monitor.Brightness} range {monitor.Minimum}..{monitor.Maximum}");
            Assert.InRange(monitor.Brightness, monitor.Minimum, monitor.Maximum);
            Assert.True(monitor.Maximum >= monitor.Minimum);
            Assert.False(string.IsNullOrWhiteSpace(monitor.DeviceName));
        }
    }

    public void Dispose()
    {
        _engine.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

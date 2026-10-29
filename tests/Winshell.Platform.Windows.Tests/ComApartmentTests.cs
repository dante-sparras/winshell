using System.Reflection;
using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Services.Audio;

namespace Winshell.Platform.Windows.Tests;

public class ComApartmentTests
{
    [Fact]
    public void A_com_object_is_usable_on_the_thread_that_restores_it()
    {
        var type = Type.GetTypeFromProgID("Scripting.Dictionary");
        Assert.NotNull(type);
        var source = Activator.CreateInstance(type!)!;
        try
        {
            source.GetType().InvokeMember("Add", BindingFlags.InvokeMethod, null, source, ["k", "v"]);
            var stream = ComApartment.Capture(source);
            object? restored = null;
            string? value = null;
            var thread = new Thread(() =>
            {
                restored = ComApartment.Restore(stream);
                value = (string)restored.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, restored, ["k"])!;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal("v", value);
            if (restored is not null)
                Marshal.ReleaseComObject(restored);
        }
        finally
        {
            Marshal.ReleaseComObject(source);
        }
    }
}

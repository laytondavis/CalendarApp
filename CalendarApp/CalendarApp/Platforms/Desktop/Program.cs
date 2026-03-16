using Uno.UI.Hosting;
using Velopack;

namespace CalendarApp;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // MUST be the very first call in Main.
        // Velopack uses this to handle installer hooks (install/uninstall/update)
        // and to apply any downloaded update before the app UI starts.
        VelopackApp.Build().Run();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}

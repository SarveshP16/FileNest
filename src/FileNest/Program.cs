using System.IO;
using FileNest.Services;
using Velopack;

namespace FileNest;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: when Setup.exe or the updater launches FileNest with install/uninstall hooks,
        // Velopack handles them here and exits before any window is created.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Don't leave a startup entry pointing at an exe that's about to be deleted.
                try
                {
                    WindowsIntegration.SetStartWithWindows(false);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

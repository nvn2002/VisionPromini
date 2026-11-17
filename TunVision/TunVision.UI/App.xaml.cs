using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TunVision.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                File.WriteAllText("crash.log", args.ExceptionObject?.ToString() ?? "Unknown exception");
            }
            catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                File.WriteAllText("crash.log", args.Exception?.ToString() ?? "Unknown dispatcher exception");
            }
            catch { }
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            try
            {
                File.WriteAllText("crash.log", args.Exception?.ToString() ?? "Unobserved task exception");
            }
            catch { }
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}

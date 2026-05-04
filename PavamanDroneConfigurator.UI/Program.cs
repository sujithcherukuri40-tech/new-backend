using Avalonia;
using System;

namespace PavamanDroneConfigurator.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
 public static void Main(string[] args)
    {
        // Catch unhandled exceptions on background threads to prevent silent app crashes
        // Without this, an OOM on a Task.Run thread can surface as a mystery "disconnect" redirect
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
        };

        // Catch unobserved task exceptions (fire-and-forget tasks that throw)
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"[WARN] Unobserved task exception: {e.Exception?.InnerException?.Message ?? e.Exception?.Message}");
            e.SetObserved(); // Prevent process termination
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

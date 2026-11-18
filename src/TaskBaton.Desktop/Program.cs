using System.IO;
using System.Windows;
using TaskBaton.Core;

namespace TaskBaton.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application();
        try
        {
            var demo = args.Contains("--demo");
            var options = args.Where(a => a != "--demo").ToArray();
            if (args.Count(a => a == "--demo") > 1 || options.Length != 0 && (options.Length != 2 || options[0] != "--home"))
                throw new RelayException("invalid_arguments", "用法：TaskBaton.Desktop.exe [--demo] [--home <data directory>]");
            var environmentHome = TaskBatonHome.EnvironmentHome;
            var explicitHome = options.Length == 2 ? options[1] : null;
            var home = options.Length == 2 ? options[1] : demo ? NewDemoHome() : TaskBatonHome.Resolve();
            var resolvedHome = demo ? home : RelayService.ResolveRelocatedHome(home);
            var service = demo ? DemoWorkspace.CreateAsync(resolvedHome).GetAwaiter().GetResult() : new RelayService(resolvedHome);
            service.Initialize();
            if (resolvedHome != home) service.RefreshGuidance();
            if (!demo && explicitHome is null && environmentHome is null)
                RelayLocation.Save(service.Home);
            return app.Run(new MainWindow(service, demo));
        }
        catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(error.Message, "TaskBaton — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return 2;
        }
    }

    internal static string NewDemoHome() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskBaton.Demos", Guid.NewGuid().ToString());
}

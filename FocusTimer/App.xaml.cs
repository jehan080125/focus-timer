using System.Windows;

namespace FocusTimer;

public partial class App : Application
{
    internal ThemeService Theme { get; private set; } = null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme = new ThemeService();
        Theme.Start();
        if (e.Args.Contains("--verify-ui"))
        {
            Theme.Dispose(); // Diagnostic captures explicitly select both themes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { UiVerification.Run(e.Args); Shutdown(0); }
                catch (Exception ex) { System.IO.File.WriteAllText("ui-verification-error.txt", ex.ToString()); Shutdown(1); }
            }));
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { Theme.Dispose(); base.OnExit(e); }
}

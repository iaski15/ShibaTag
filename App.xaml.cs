using System.Windows;

namespace ShibaTag;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            Shutdown(SelfTest.Run(e.Args.ElementAtOrDefault(1)));
            return;
        }
        new MainWindow(e.Args.FirstOrDefault(System.IO.Directory.Exists)).Show();
    }
}

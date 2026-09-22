using System.Windows;

namespace Android投屏助手;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Android投屏助手-v2", out var created);
        if (!created)
        {
            MessageBox.Show("Android投屏助手已经在运行。", "Android投屏助手", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}

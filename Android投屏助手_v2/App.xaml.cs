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
            GlassDialog.Message(null, "Android投屏助手", "助手已经在运行，请切回现有窗口。");
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}

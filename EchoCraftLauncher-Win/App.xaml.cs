using System.Windows;
using System.Windows.Threading;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2;

public partial class App : Application
{
    public static LauncherCore Core { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Core = new LauncherCore();
        // 先注入配色，再创建窗口；后续切换主题时整窗 DynamicResource 实时跟随
        ThemeManager.Apply(Core.Config);
        Core.LogLine($"启动器已就绪，数据目录：{Core.Paths.Root}");

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Core?.LogLine("出现未处理异常：" + e.Exception.Message);
        Dialogs.AppDialog.Alert(e.Exception.Message, "出现了未处理的异常", true);
        e.Handled = true;
    }
}

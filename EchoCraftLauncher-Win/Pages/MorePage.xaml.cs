using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SwiftCraftLauncher2.Pages;

public partial class MorePage : UserControl
{
    public MorePage()
    {
        InitializeComponent();
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e) => Open(App.Core.Paths.Root);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Open(App.Core.Paths.Logs);

    private void OpenSite_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://www.minecraft.net/zh-hans/store/minecraft-java-edition");

    private void OpenPcl_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/Meloong-Git/PCL");

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "打开目录");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "打开链接");
        }
    }
}


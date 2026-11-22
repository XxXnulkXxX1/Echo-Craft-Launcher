using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Pages;

public partial class MultiplayerPage : UserControl
{
    public MultiplayerPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var instance = FindInstance();
        InstanceText.Text = instance is null
            ? "还没有实例，先在「启动」页新建一个。"
            : $"当前实例：{instance.Name}\n{instance.Directory}";
    }

    private InstanceInfo? FindInstance()
    {
        var all = App.Core.Instances;
        var last = App.Core.Config.LastInstance;
        return all.FirstOrDefault(i => i.Name == last) ?? all.FirstOrDefault();
    }

    private void OpenMods_Click(object sender, RoutedEventArgs e) => Open("mods");

    private void OpenInstance_Click(object sender, RoutedEventArgs e) => Open("");

    private void OpenSaves_Click(object sender, RoutedEventArgs e) => Open("saves");

    private void Open(string sub)
    {
        var instance = FindInstance();
        if (instance is null)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("还没有任何实例。", "联机");
            return;
        }
        var path = string.IsNullOrEmpty(sub) ? instance.Directory : System.IO.Path.Combine(instance.Directory, sub);
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "打开文件夹");
        }
    }
}


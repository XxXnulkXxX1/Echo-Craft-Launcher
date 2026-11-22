using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private bool _loadingLanguage;
    private bool _loadingTranslate;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loadingLanguage = true;
        var config = App.Core.Config;
        LanguageBox.SelectedIndex = config.Language == Localizer.English ? 1 : 0;
        _loadingTranslate = true;
        TranslateModsBox.IsChecked = config.TranslateMods;
        _loadingTranslate = false;
        _loadingLanguage = false;
        MemoryBox.Text = config.MemoryMb.ToString();
        ThreadsBox.Text = config.DownloadThreads.ToString();
        JvmBox.Text = config.JvmArgs;
        PlayerBox.Text = config.PlayerName;
        RootText.Text = App.Core.Paths.Root;
        if (config.Mirror == Net.MirrorBmcl)
        {
            MirrorBmcl.IsChecked = true;
        }
        else
        {
            MirrorOfficial.IsChecked = true;
        }
        await DetectAsync();
    }

    private async Task DetectAsync()
    {
        StatusText.Text = "正在检测 Java…";
        try
        {
            var list = await App.Core.DetectJavaAsync();
            JavaList.ItemsSource = list;
            JavaList.SelectedIndex = list.Count > 0 ? 0 : -1;
            StatusText.Text = list.Count == 0 ? "没有检测到 Java，请手动指定" : $"检测到 {list.Count} 个 Java 运行时";
        }
        catch (Exception ex)
        {
            StatusText.Text = "检测失败：" + ex.Message;
        }
    }

    private async void Detect_Click(object sender, RoutedEventArgs e) => await DetectAsync();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 javaw.exe / java.exe",
            Filter = "Java 可执行文件|javaw.exe;java.exe|所有文件|*.exe"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        App.Core.Config.JavaPath = dialog.FileName;
        App.Core.SaveConfig();
        StatusText.Text = "已指定 Java：" + dialog.FileName;
        _ = DetectAsync();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var config = App.Core.Config;
        if (int.TryParse(MemoryBox.Text.Trim(), out var memory))
        {
            config.MemoryMb = Math.Clamp(memory, 512, 32768);
            MemoryBox.Text = config.MemoryMb.ToString();
        }
        if (int.TryParse(ThreadsBox.Text.Trim(), out var threads))
        {
            config.DownloadThreads = Math.Clamp(threads, 2, 24);
            ThreadsBox.Text = config.DownloadThreads.ToString();
        }
        config.JvmArgs = JvmBox.Text.Trim();
        config.Mirror = MirrorBmcl.IsChecked == true ? Net.MirrorBmcl : Net.MirrorOfficial;
        Net.Mirror = config.Mirror;
        var player = PlayerBox.Text.Trim();
        if (player.Length > 0)
        {
            config.PlayerName = player;
            if (!config.Account.IsMicrosoft)
            {
                config.Account.Name = player;
            }
        }
        if (JavaList.SelectedItem is JavaRuntime java)
        {
            config.JavaPath = java.Path;
        }
        config.TranslateMods = TranslateModsBox.IsChecked == true;
        App.Core.SaveConfig();
        StatusText.Text = "设置已保存（镜像切换对下一次下载生效）";
    }

    private void TranslateMods_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingTranslate)
        {
            return;
        }
        // 勾选状态立即落盘：开启后详情页才会自动翻译，关闭则始终显示英文原文
        App.Core.Config.TranslateMods = TranslateModsBox.IsChecked == true;
        App.Core.SaveConfig();
        StatusText.Text = App.Core.Config.TranslateMods
            ? "已开启模组介绍自动翻译（打开详情页时联网机翻，可用「查看原文」切回）"
            : "已关闭模组介绍自动翻译（详情页显示原始英文）";
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguage)
        {
            return;
        }
        var tag = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Localizer.Chinese;
        if (App.Core.Config.Language == tag)
        {
            return;
        }
        App.Core.Config.Language = tag;
        App.Core.SaveConfig();
        Localizer.Relanguage();
    }

    private void OpenRoot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(App.Core.Paths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "无法打开目录：" + ex.Message;
        }
    }
}


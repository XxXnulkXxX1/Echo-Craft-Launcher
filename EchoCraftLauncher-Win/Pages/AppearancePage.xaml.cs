using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftCraftLauncher2.Dialogs;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Pages;

/// <summary>外观设置：主题、主色、动态效果。改动立即应用并写入配置。</summary>
public partial class AppearancePage : UserControl
{
    private bool _loading;

    public AppearancePage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadFromConfig();
    }

    private LauncherConfig Config => App.Core.Config;

    private void LoadFromConfig()
    {
        _loading = true;
        var light = Config.Theme == ThemeManager.ThemeLight;
        ThemeDarkRadio.IsChecked = !light;
        ThemeLightRadio.IsChecked = light;
        FlowCheck.IsChecked = Config.FlowBackground;
        SpringCheck.IsChecked = Config.SpringMotion;
        HighlightAccent(Config.Accent);
        _loading = false;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton radio || radio.Tag is not string theme)
        {
            return;
        }
        Config.Theme = theme;
        ApplyAndSave();
    }

    private void Accent_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string accent)
        {
            return;
        }
        Config.Accent = accent;
        HighlightAccent(accent);
        ApplyAndSave();
    }

    private void Effect_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        Config.FlowBackground = FlowCheck.IsChecked == true;
        Config.SpringMotion = SpringCheck.IsChecked == true;
        ApplyAndSave();
    }

    private void ApplyAndSave()
    {
        ThemeManager.Apply(Config);
        App.Core.SaveConfig();
    }

    private void HighlightAccent(string accent)
    {
        var borders = new Dictionary<string, Border>
        {
            ["blue"] = SwBlue,
            ["purple"] = SwPurple,
            ["green"] = SwGreen,
            ["orange"] = SwOrange,
            ["pink"] = SwPink
        };
        foreach (var pair in borders)
        {
            pair.Value.BorderBrush = pair.Key == accent
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private void PreviewInfo_Click(object sender, RoutedEventArgs e)
    {
        AppDialog.Alert("这是一条提示。弹窗会用应用的面板底色、边框与主色按钮，跟随当前主题。", "外观预览");
    }

    private void PreviewWarn_Click(object sender, RoutedEventArgs e)
    {
        AppDialog.Alert("这是一条警告示例：启动失败时会用同样的样式给出可读的原因。", "外观预览", true);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Config.Theme = ThemeManager.ThemeDark;
        Config.Accent = "blue";
        Config.FlowBackground = true;
        Config.SpringMotion = true;
        ApplyAndSave();
        LoadFromConfig();
    }
}

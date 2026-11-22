using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwiftCraftLauncher2.Services;

/// <summary>配色方案：主题 × 主色，运行时替换资源字典，所有界面用 DynamicResource 跟随。</summary>
public static class ThemeManager
{
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";

    public static readonly (string Id, string Name, string Accent, string Deep)[] Accents =
    {
        ("blue", "星蓝", "#3C9DF0", "#2A7CC0"),
        ("purple", "幻紫", "#9B6BFF", "#7B4CE0"),
        ("green", "苔绿", "#2FBE8A", "#229A6E"),
        ("orange", "熔橙", "#F28B42", "#CE6C26"),
        ("pink", "樱粉", "#F06292", "#C84B76")
    };

    public static event Action? Changed;

    public static void Apply(LauncherConfig config)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }
        var palette = Build(config.Theme, config.Accent);
        // 直接写进 Application 级资源：改变字典里的值会立刻让所有 DynamicResource 重新取值。
        // （替换 MergedDictionaries 中的字典不会触发刷新，实测切换后界面仍保持旧配色）
        foreach (var key in palette.Keys)
        {
            if (key is string name && name == "__palette")
            {
                continue;
            }
            app.Resources[key] = palette[key];
        }
        Changed?.Invoke();
    }

    private static ResourceDictionary Build(string theme, string accentId)
    {
        var accent = Accents.FirstOrDefault(a => a.Id == accentId);
        if (string.IsNullOrEmpty(accent.Id))
        {
            accent = Accents[0];
        }
        var light = theme == ThemeLight;

        string bg, panel, panel2, border, text, sub, navHover, navActive, titleBar, navPanel, input, logBg;
        if (light)
        {
            bg = "#F2F4F8"; panel = "#FFFFFF"; panel2 = "#F4F6FA"; border = "#D8DEE7";
            text = "#1B1F27"; sub = "#66707E"; navHover = "#EDF1F7"; navActive = "#E2EBF8";
            titleBar = "#FAFBFD"; navPanel = "#F7F9FC"; input = "#FFFFFF"; logBg = "#FFFFFF";
        }
        else
        {
            bg = "#16171A"; panel = "#202127"; panel2 = "#2A2C33"; border = "#33353C";
            text = "#E9EBEF"; sub = "#9AA1A9"; navHover = "#26282F"; navActive = "#2C3540";
            titleBar = "#1B1C20"; navPanel = "#1A1B1F"; input = "#2A2C33"; logBg = "#1B1C20";
        }

        var dict = new ResourceDictionary { { "__palette", true } };

        void Brush(string key, string hex) => dict[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        void ColorKey(string key, string hex) => dict[key] = (Color)ColorConverter.ConvertFromString(hex);

        Brush("BgBrush", bg);
        Brush("PanelBrush", panel);
        Brush("Panel2Brush", panel2);
        Brush("BorderBrush", border);
        Brush("TextBrush", text);
        Brush("SubTextBrush", sub);
        Brush("NavHoverBrush", navHover);
        Brush("NavActiveBrush", navActive);
        Brush("TitleBarBrush", titleBar);
        Brush("NavPanelBrush", navPanel);
        Brush("InputBrush", input);
        Brush("LogBgBrush", logBg);
        Brush("AccentBrush", accent.Accent);
        Brush("AccentDeepBrush", accent.Deep);
        ColorKey("AccentColor", accent.Accent);
        ColorKey("AccentDeepColor", accent.Deep);
        // 主题相关的“表头/浮层”底色，弹窗圆角边框使用
        Brush("DialogBorderBrush", light ? "#C9D2DE" : "#3A3D46");
        return dict;
    }
}

/// <summary>轻量弹性动效：不依赖第三方库，用 WPF 的 ElasticEase。</summary>
public static class Motion
{
    public static bool Enabled => true;

    public static void SpringIn(FrameworkElement element, bool enabled)
    {
        if (!enabled)
        {
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }
        var scale = new ScaleTransform(0.97, 0.97);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        element.Opacity = 0;

        var ease = new ElasticEase { Oscillations = 1, Springiness = 0.45, EasingMode = EasingMode.EaseOut };
        var sx = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
        var sy = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

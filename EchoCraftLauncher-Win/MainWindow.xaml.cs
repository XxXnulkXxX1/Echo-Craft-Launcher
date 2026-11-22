using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwiftCraftLauncher2.Pages;
using SwiftCraftLauncher2.Services;
namespace SwiftCraftLauncher2;

public partial class MainWindow : Window
{
    private Rect _restoreBounds;
    private bool _maximized;
    private bool _flowStarted;

    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly string? _initialPage;

    public string ActivePageKey { get; private set; } = "launch";

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? initialPage)
    {
        _initialPage = initialPage;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var key = InitialPage();
            SelectNav(key);
            ShowPage(key);
            UpdateFlow();
            Localizer.Apply(this);
        };
        StateChanged += (_, _) => UpdateMaxGlyph();
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>支持 --page=appearance 等参数直接打开指定页，便于调试与截图核对。</summary>
    private string InitialPage()
    {
        if (_initialPage is not null)
        {
            return _initialPage;
        }
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        if (arg is null)
        {
            return "launch";
        }
        var key = arg["--page=".Length..];
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "scl_page.log"), $"arg=[{arg}] key=[{key}]\n");
        return key switch
        {
            "download" or "multi" or "appearance" or "settings" or "more" => key,
            _ => "launch"
        };
    }

    private void OnThemeChanged()
    {
        UpdateFlow();
    }

    private void SelectNav(string key)
    {
        var radio = key switch
        {
            "download" => NavDownload,
            "multi" => NavMulti,
            "appearance" => NavAppearance,
            "settings" => NavSettings,
            "more" => NavMore,
            _ => NavLaunch
        };
        if (!radio.IsChecked.GetValueOrDefault())
        {
            radio.IsChecked = true;
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && button.Tag is string key)
        {
            ShowPage(key);
        }
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "download" => new DownloadPage(),
                "multi" => new MultiplayerPage(),
                "appearance" => new AppearancePage(),
                "settings" => new SettingsPage(),
                "more" => new MorePage(),
                _ => new LaunchPage()
            };
            _pages[key] = page;
            page.Loaded += (_, _) => Localizer.Apply(page);
        }
        ActivePageKey = key;
        Host.Content = page;
        Motion.SpringIn(page, App.Core.Config.SpringMotion);
    }

    // ---------------------------------------------------------------- 流动背景

    private void UpdateFlow()
    {
        if (FlowLayer is null)
        {
            return;
        }
        FlowLayer.Visibility = App.Core.Config.FlowBackground ? Visibility.Visible : Visibility.Collapsed;
        if (FlowLayer.Visibility == Visibility.Visible)
        {
            StartFlow();
        }
    }

    private void StartFlow()
    {
        if (_flowStarted)
        {
            return;
        }
        _flowStarted = true;
        // 三个色团以不同周期缓慢漂移，正弦缓动避免折返时的突兀感
        Animate(Blob1Move, 90, 60, 19);
        Animate(Blob2Move, -120, -80, 24);
        Animate(Blob3Move, 70, -50, 15);
    }

    private static void Animate(TranslateTransform transform, double toX, double toY, double seconds)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var x = new DoubleAnimation(0, toX, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };
        var y = new DoubleAnimation(0, toY, TimeSpan.FromSeconds(seconds * 0.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };
        transform.BeginAnimation(TranslateTransform.XProperty, x);
        transform.BeginAnimation(TranslateTransform.YProperty, y);
    }

    // ---------------------------------------------------------------- 窗口控制

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标已松开
            }
        }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 点右上角 X 后必须真正退出：这里显式 Shutdown，
    /// 避免 WebView2 / 计时器等后台对象让进程继续留在任务管理器里（看起来像“缩到了托盘”）。
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        try
        {
            var config = App.Core.Config;
            if (WindowState == WindowState.Normal)
            {
                config.WindowWidth = (int)Math.Round(Width);
                config.WindowHeight = (int)Math.Round(Height);
            }
            App.Core.SaveConfig();
        }
        catch
        {
            // 关闭阶段保存失败不影响退出
        }
        Application.Current.Shutdown();
    }

    private void ToggleMaximize()
    {
        if (!_maximized)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var area = SystemParameters.WorkArea;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
            _maximized = true;
        }
        else
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _maximized = false;
        }
        UpdateMaxGlyph();
    }

    private void UpdateMaxGlyph()
    {
        if (MaxButton is not null)
        {
            MaxButton.Content = _maximized ? "❐" : "□";
        }
    }
}


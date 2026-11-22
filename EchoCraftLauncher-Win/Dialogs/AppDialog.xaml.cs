using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftCraftLauncher2.Dialogs;

/// <summary>贴合应用配色的轻量弹窗，替代系统 MessageBox（系统弹窗是浅色、会和深色界面割裂）。</summary>
public partial class AppDialog : Window
{
    private bool _confirmed;

    public AppDialog()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    public static void Alert(string message, string title = "提示", bool warning = false)
    {
        var dialog = Create(title, message, "确定", null, warning);
        dialog.ShowDialog();
    }

    public static bool Confirm(string message, string title = "确认")
    {
        var dialog = Create(title, message, "确定", "取消", false);
        dialog.ShowDialog();
        return dialog._confirmed;
    }

    private static AppDialog Create(string title, string message, string okText, string? cancelText, bool warning)
    {
        var dialog = new AppDialog();
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = okText;
        if (cancelText is null)
        {
            dialog.CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            dialog.CancelButton.Content = cancelText;
        }
        if (warning)
        {
            // 用主题色描边提示，不引入系统图标，保持视觉统一
            dialog.OkButton.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0x5A, 0x3C));
        }
        dialog.Loaded += (_, _) => dialog.OkButton.Focus();
        return dialog;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = false;
        DialogResult = false;
    }
}

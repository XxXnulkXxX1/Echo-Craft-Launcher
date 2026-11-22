using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Dialogs;

public partial class MsLoginWindow : Window
{
    private readonly string _state = Guid.NewGuid().ToString("N");
    private bool _busy;

    public AccountInfo? Account { get; private set; }

    public MsLoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => Browser.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var authorizeUrl = MicrosoftAuth.BuildAuthorizeUrl(_state);
        try
        {
            // 用户数据目录放到启动器数据目录，避免安装到 Program Files 时无写权限
            var env = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(App.Core.Paths.Root, "webview2"));
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Browser.CoreWebView2.Navigate(args.Uri);
            };
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    StatusText.Text = $"页面加载失败（{args.WebErrorStatus}），可改用下方浏览器登录";
                }
            };
            Browser.Source = new Uri(authorizeUrl);
            StatusText.Text = "请在下方窗口中登录微软账号";        }
        catch (Exception ex)
        {
            StatusText.Text = "内嵌登录页不可用（" + ex.Message + "），请使用下方“在浏览器中打开登录页”";
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var code = SafeExtractCode(e.Uri);
        if (code is not null)
        {
            e.Cancel = true; // 回调地址不需要真的渲染
            CompleteAsync(code);
        }
    }

    private static string? SafeExtractCode(string url)
    {
        try
        {
            return MicrosoftAuth.ExtractCode(url);
        }
        catch (LauncherException ex)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "微软登录", true);
            return null;
        }
    }

    private async void CompleteAsync(string code)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        StatusText.Text = "已获得授权码，正在验证 Xbox / Minecraft 账号…";
        Browser.Visibility = Visibility.Collapsed;
        try
        {
            Account = await MicrosoftAuth.CompleteAsync(code, CancellationToken.None);
            StatusText.Text = $"登录成功：{Account.Name}";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "登录失败：" + ex.Message;
            Browser.Visibility = Visibility.Visible;
            _busy = false;
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "微软登录", true);
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        var url = MicrosoftAuth.BuildAuthorizeUrl(_state);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusText.Text = "已在浏览器打开登录页；登录后把跳转地址粘贴到下方输入框";
        }
        catch (Exception ex)
        {
            StatusText.Text = "无法打开浏览器：" + ex.Message;
        }
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        var text = CallbackBox.Text.Trim();
        string? code = null;
        try
        {
            code = MicrosoftAuth.ExtractCode(text);
        }
        catch (LauncherException ex)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "微软登录");
            return;
        }
        if (code is null)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("粘贴的地址里没有 code 参数。请确认复制的是登录完成后浏览器地址栏的完整地址。", "微软登录");
            return;
        }
        CompleteAsync(code);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}


using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftCraftLauncher2.Dialogs;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Pages;

public partial class LaunchPage : UserControl
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _wired;

    public LaunchPage()
    {
        InitializeComponent();
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _flushTimer.Tick += (_, _) => FlushLog();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _flushTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _flushTimer.Start();
        if (!_wired)
        {
            _wired = true;
            InstanceList.ItemsSource = App.Core.Instances;
            App.Core.Log += PushLog;
        }
        App.Core.RefreshInstances();
        SelectLastInstance();
        UpdateAccountPanel();
        _ = RefreshJavaAsync();
    }

    private void SelectLastInstance()
    {
        var last = App.Core.Config.LastInstance;
        InstanceInfo? target = null;
        if (last.Length > 0)
        {
            target = App.Core.Instances.FirstOrDefault(i => i.Name == last);
        }
        target ??= App.Core.Instances.FirstOrDefault();
        if (target is not null)
        {
            InstanceList.SelectedItem = target;
        }
        else
        {
            UpdateInstancePanel();
        }
    }

    private async Task RefreshJavaAsync()
    {
        StatusText.Text = "正在检测 Java…";
        try
        {
            var list = await App.Core.DetectJavaAsync();
            if (list.Count == 0)
            {
                StatusText.Text = "未检测到 Java";
                PushLog("[引擎] 未找到任何 Java，请到「设置」手动指定 javaw.exe");
                return;
            }
            PushLog($"[引擎] 检测到 Java：{string.Join("、", list.Select(j => j.Major))}");
            StatusText.Text = $"Java {list[0].Major} 就绪";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Java 检测失败";
            PushLog("[引擎] " + ex.Message);
        }
    }

    private void InstanceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var instance = InstanceList.SelectedItem as InstanceInfo;
        if (instance is not null)
        {
            App.Core.Config.LastInstance = instance.Name;
            App.Core.SaveConfig();
        }
        UpdateInstancePanel();
    }

    private void UpdateInstancePanel()
    {
        var instance = InstanceList.SelectedItem as InstanceInfo;
        if (instance is null)
        {
            VersionTitle.Text = "尚未选择实例";
            VersionSub.Text = "点击左侧「新建实例」选择版本开始";
            StartButton.IsEnabled = false;
            return;
        }
        StartButton.IsEnabled = !_busy;
        VersionTitle.Text = instance.Name;
        var lines = new List<string> { instance.SubTitle };
        if (instance.Directory.Length > 0)
        {
            lines.Add(instance.Directory);
        }
        if (instance.VersionMissing)
        {
            lines.Add("提示：本地缺少版本文件，启动时会自动下载补全");
        }
        VersionSub.Text = string.Join('\n', lines);
    }

    private void UpdateAccountPanel()
    {
        var account = App.Core.Config.Account;
        if (account.IsMicrosoft)
        {
            AccountName.Text = account.Name;
            AccountKind.Text = "微软正版账号";
            return;
        }
        var name = account.Name.Length > 0 ? account.Name : App.Core.Config.PlayerName;
        AccountName.Text = name.Length > 0 ? name : "未登录";
        AccountKind.Text = "离线模式（仅本地游戏）";
    }

    private void PushLog(string line)
    {
        _pending.Enqueue(line);
    }

    private void FlushLog()
    {
        if (_pending.IsEmpty)
        {
            return;
        }
        var sb = new System.Text.StringBuilder();
        var count = 0;
        while (count < 200 && _pending.TryDequeue(out var line))
        {
            sb.AppendLine(line);
            count++;
        }
        var text = sb.ToString();
        if (text.Length == 0)
        {
            return;
        }
        if (LogBox.Text.Length > 40000)
        {
            LogBox.Text = LogBox.Text[^20000..];
        }
        LogBox.AppendText(text);
        LogBox.ScrollToEnd();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            StatusText.Text = "正在取消…";
            return;
        }

        var instance = InstanceList.SelectedItem as InstanceInfo;
        if (instance is null)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("请先在左侧新建或选择一个游戏实例。", "Echo Craft Launcher");
            return;
        }

        var account = await EnsureAccountAsync();
        if (account is null)
        {
            return;
        }

        _busy = true;
        StartButton.Content = "取消准备";
        Bar.Value = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // 过期令牌先静默续期，避免直接拿过期 token 启动
            if (account.IsMicrosoft && account.TokenExpired && account.RefreshToken.Length > 0)
            {
                StatusText.Text = "正在续期微软登录…";
                try
                {
                    account = await MicrosoftAuth.RefreshAsync(account.RefreshToken, token);
                    App.Core.Config.Account = account;
                    App.Core.SaveConfig();
                    UpdateAccountPanel();
                }
                catch (Exception ex)
                {
                    PushLog("[账号] " + ex.Message);
                    SwiftCraftLauncher2.Dialogs.AppDialog.Alert("微软登录已过期，请重新登录正版账号。", "Echo Craft Launcher");
                    return;
                }
            }

            StatusText.Text = "正在补全游戏文件…";
            var installer = new Installer(App.Core.Paths, App.Core.Config.DownloadThreads, line => PushLog("[下载] " + line));
            var progress = new Progress<InstallProgress>(p =>
            {
                Bar.Value = p.Fraction * 100;
                var speed = p.Speed > 0 ? $"  {p.Speed / 1024.0 / 1024.0:F1} MB/s" : "";
                StatusText.Text = $"{p.Stage} {p.Detail}{speed}";
            });
            var result = await installer.InstallAsync(instance.VersionId, progress, token);
            token.ThrowIfCancellationRequested();

            var javas = App.Core.Java.Count > 0 ? App.Core.Java : await App.Core.DetectJavaAsync();
            var java = JavaService.Pick(javas, result.RequiredJava);
            if (java is null)
            {
                throw new LauncherException(
                    $"没有找到可用的 Java（该版本需要 Java {result.RequiredJava}+）。\n请安装对应版本的 JDK/JRE，或在「设置」里指定 javaw.exe。");
            }

            App.Core.Config.LastInstance = instance.Name;
            App.Core.SaveConfig();

            StatusText.Text = $"Java {java.Major} 就绪，正在启动游戏…";
            var handle = LaunchService.Launch(
                App.Core.Paths, instance, result.Version, java, account,
                App.Core.Config.MemoryMb, App.Core.Config.JvmArgs, 0, 0, token);
            handle.LineReceived += PushLog;
            PushLog($"[启动] 游戏已启动，日志文件：{handle.LogPath}");
            StatusText.Text = $"游戏已启动（Java {java.Major}）";
            Bar.Value = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
        }
        catch (LauncherException ex)
        {
            StatusText.Text = "启动失败";
            PushLog("[错误] " + ex.Message);
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "Echo Craft Launcher", true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "启动失败";
            PushLog("[错误] " + ex.Message);
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "Echo Craft Launcher", true);
        }
        finally
        {
            _busy = false;
            StartButton.Content = "启动游戏";
            _cts?.Dispose();
            _cts = null;
            UpdateInstancePanel();
        }
    }

    /// <summary>拿不到账号时返回 null 并提示。</summary>
    private async Task<AccountInfo?> EnsureAccountAsync()
    {
        var account = App.Core.Config.Account;
        if (account.IsMicrosoft)
        {
            return account;
        }
        if (account.Name.Length > 0)
        {
            return account;
        }
        var defaultName = App.Core.Config.PlayerName;
        if (defaultName.Length == 0)
        {
            var prompt = new PromptDialog("离线用户名", "请输入 1–16 位字母、数字或下划线", "Steve");
            if (prompt.ShowDialog() != true)
            {
                return null;
            }
            defaultName = prompt.Value;
            App.Core.Config.PlayerName = defaultName;
        }
        account = new AccountInfo { Type = "offline", Name = defaultName };
        App.Core.Config.Account = account;
        App.Core.SaveConfig();
        UpdateAccountPanel();
        await Task.CompletedTask;
        return account;
    }

    private void NewInstance_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateInstanceDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Created is not null)
        {
            App.Core.RefreshInstances();
            InstanceList.SelectedItem = App.Core.Instances.FirstOrDefault(i => i.Name == dialog.Created.Name);
        }
    }

    private void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        var instance = InstanceList.SelectedItem as InstanceInfo;
        if (instance is null)
        {
            return;
        }
        var confirm = SwiftCraftLauncher2.Dialogs.AppDialog.Confirm(
            $"确定删除实例「{instance.Name}」吗？\n只删除实例目录（存档、模组会一起删除）。", "删除实例");
        if (!confirm)
        {
            return;
        }
        InstanceStore.Delete(instance);
        App.Core.Config.LastInstance = "";
        App.Core.SaveConfig();
        App.Core.RefreshInstances();
        SelectLastInstance();
    }

    private void MsLogin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MsLoginWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Account is not null)
        {
            App.Core.Config.Account = dialog.Account;
            App.Core.Config.PlayerName = dialog.Account.Name;
            App.Core.SaveConfig();
            UpdateAccountPanel();
            PushLog($"[账号] 已登录微软账号 {dialog.Account.Name}");
        }
    }

    private void OfflineLogin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptDialog("离线登录", "请输入 1–16 位字母、数字或下划线",
            App.Core.Config.PlayerName.Length > 0 ? App.Core.Config.PlayerName : "Steve");
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var name = dialog.Value;
        App.Core.Config.Account = new AccountInfo { Type = "offline", Name = name };
        App.Core.Config.PlayerName = name;
        App.Core.SaveConfig();
        UpdateAccountPanel();
        PushLog($"[账号] 已切换为离线账号 {name}");
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        App.Core.Config.Account = new AccountInfo();
        App.Core.SaveConfig();
        UpdateAccountPanel();
        PushLog("[账号] 已退出登录");
    }
}



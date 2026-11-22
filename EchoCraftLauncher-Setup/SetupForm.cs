using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EchoCraftSetup;

/// <summary>
/// 轻量网络安装器（目标框架 .NET Framework 4.8，Win10/11 自带，朋友双击即可打开）。
/// 自身只有几百 KB，安装时再按需拉取：
///   1) .NET 10 桌面运行时（本机缺失时，征得同意后自动下载安装）
///   2) EchoCraftLauncher-1.0.0.zip（约 3 MB 的完整程序包）
/// 然后解压、创建快捷方式。若同目录存在离线 zip，则完全不联网。
/// </summary>
internal sealed class SetupForm : Form
{
    /// <summary>完整程序包的下载地址（上传到 CDN / 网盘后填这里）。</summary>
    private const string PackageUrl = "https://aka.doubaocdn.com/s/9WJyiGmukC";

    /// <summary>.NET 10 桌面运行时（x64）官方安装器地址。</summary>
    private const string DotNetRuntimeUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>WebView2 Evergreen 引导安装器地址（仅在内嵌浏览器缺失时提示）。</summary>
    private const string WebView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private const string LocalPackageName = "EchoCraftLauncher-1.0.0.zip";
    private const string AppExeName = "EchoCraftLauncher.exe";

    private readonly TextBox _pathBox = new TextBox();
    private readonly Button _browseButton = new Button();
    private readonly Button _installButton = new Button();
    private readonly Button _closeButton = new Button();
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Label _status = new Label();
    private readonly CheckBox _desktopShortcut = new CheckBox();
    private readonly CheckBox _launchAfter = new CheckBox();
    private bool _busy;
    private bool _webViewHintShown;

    public SetupForm()
    {
        // 老版本 TLS 默认设置可能连不上 CDN，这里强制启用 TLS 1.2
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        Text = "Echo Craft Launcher 安装程序";
        ClientSize = new Size(660, 372);
        MinimumSize = new Size(660, 372);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(245, 247, 251);
        Font = new Font("Microsoft YaHei UI", 9F);

        // ── 顶部品牌区 ─────────────────────────────────────────────
        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.FromArgb(32, 34, 42) };
        var iconBox = new PictureBox
        {
            Size = new Size(52, 52),
            Location = new Point(24, 22),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            iconBox.Image = new Icon(iconPath, 64, 64).ToBitmap();
        }
        var title = new Label
        {
            Text = "Echo Craft Launcher",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(90, 22)
        };
        var subtitle = new Label
        {
            Text = "轻量安装器 · 安装时自动下载完整程序（约 3 MB）",
            ForeColor = Color.FromArgb(154, 161, 169),
            AutoSize = true,
            Location = new Point(92, 54)
        };
        var version = new Label
        {
            Text = "v1.0.0",
            ForeColor = Color.FromArgb(154, 161, 169),
            AutoSize = true,
            Location = new Point(580, 26)
        };
        header.Controls.AddRange(new Control[] { iconBox, title, subtitle, version });

        // ── 安装位置 ───────────────────────────────────────────────
        var pathLabel = new Label { Text = "安装位置", AutoSize = true, Location = new Point(26, 118) };
        _pathBox.Location = new Point(26, 142);
        _pathBox.Size = new Size(536, 28);
        _pathBox.Text = DefaultInstallDir();
        _browseButton.Text = "浏览…";
        _browseButton.Location = new Point(570, 141);
        _browseButton.Size = new Size(64, 30);
        _browseButton.Click += (s, e) => BrowseInstallDir();

        var hint = new Label
        {
            Text = "游戏数据（实例、资源、配置）仍保存在 D:\\EchoCraftLauncher，重装不会影响存档。",
            ForeColor = Color.FromArgb(110, 118, 130),
            AutoSize = true,
            Location = new Point(26, 176)
        };

        _desktopShortcut.Text = "创建桌面快捷方式";
        _desktopShortcut.Checked = true;
        _desktopShortcut.AutoSize = true;
        _desktopShortcut.Location = new Point(26, 204);

        _launchAfter.Text = "安装完成后启动 Echo Craft Launcher";
        _launchAfter.Checked = true;
        _launchAfter.AutoSize = true;
        _launchAfter.Location = new Point(210, 204);

        // ── 进度与状态 ─────────────────────────────────────────────
        _progress.Location = new Point(26, 240);
        _progress.Size = new Size(608, 18);
        _status.Text = "准备就绪，点击「开始安装」。";
        _status.ForeColor = Color.FromArgb(110, 118, 130);
        _status.AutoSize = false;
        _status.Location = new Point(26, 264);
        _status.Size = new Size(608, 22);

        _installButton.Text = "开始安装";
        _installButton.Location = new Point(434, 306);
        _installButton.Size = new Size(98, 38);
        _installButton.BackColor = Color.FromArgb(60, 157, 240);
        _installButton.ForeColor = Color.White;
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += async (s, e) => await InstallAsync();

        _closeButton.Text = "退出";
        _closeButton.Location = new Point(542, 306);
        _closeButton.Size = new Size(92, 38);
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[]
        {
            header, pathLabel, _pathBox, _browseButton, hint,
            _desktopShortcut, _launchAfter, _progress, _status, _installButton, _closeButton
        });
    }

    /// <summary>默认装到 D 盘（用户偏好）；D 盘不可用时退回用户目录。</summary>
    private static string DefaultInstallDir()
    {
        try
        {
            var d = DriveInfo.GetDrives().FirstOrDefault(x =>
                x.Name.StartsWith("D:", StringComparison.OrdinalIgnoreCase) && x.IsReady);
            if (d != null)
            {
                return Path.Combine(d.Name, "EchoCraftLauncher", "Program");
            }
        }
        catch
        {
            // 驱动器枚举失败时走兜底路径
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EchoCraftLauncher", "Program");
    }

    private void BrowseInstallDir()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择 Echo Craft Launcher 的安装目录";
            dialog.SelectedPath = _pathBox.Text;
            dialog.ShowNewFolderButton = true;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.SelectedPath;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 主流程
    // ──────────────────────────────────────────────────────────────

    private async Task InstallAsync()
    {
        if (_busy)
        {
            return;
        }

        var target = _pathBox.Text.Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(this, "请填写安装位置。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        SetUiEnabled(false);
        try
        {
            // 前置条件：.NET 10 桌面运行时。缺少时询问用户是否自动安装。
            if (!await EnsureRuntimeAsync())
            {
                _status.Text = "缺少 .NET 10 桌面运行时，安装已暂停。";
                return;
            }

            EnsureWebView2Hint();

            var full = Path.GetFullPath(target);
            if (IsInsideDataRoot(full))
            {
                var answer = MessageBox.Show(this,
                    "安装目录位于游戏数据目录（D:\\EchoCraftLauncher）内部，可能与实例/资源文件混在一起。\n仍要继续吗？",
                    "路径提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            Directory.CreateDirectory(full);

            var zipPath = await AcquirePackageAsync();
            _status.Text = "正在解压程序文件…";
            _progress.Style = ProgressBarStyle.Marquee;
            ExtractPackage(zipPath, full);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;

            var exe = Path.Combine(full, AppExeName);
            if (!File.Exists(exe))
            {
                throw new InvalidOperationException("安装包内容不完整：未找到 " + AppExeName);
            }

            CreateShortcuts(exe, full);
            _status.Text = "安装完成。";

            if (_launchAfter.Checked)
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = full, UseShellExecute = true });
            }

            MessageBox.Show(this, "Echo Craft Launcher 已安装到：\n" + full, "安装完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
            MessageBox.Show(this, "安装失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetUiEnabled(true);
        }
    }

    /// <summary>检查 .NET 10 桌面运行时；缺失时征得同意后自动下载官方安装器并静默安装。</summary>
    private async Task<bool> EnsureRuntimeAsync()
    {
        if (HasDotNet10Desktop())
        {
            return true;
        }

        var answer = MessageBox.Show(this,
            "检测到本机缺少 .NET 10 桌面运行时（启动器运行必需，约 60 MB）。\n\n" +
            "是否现在自动下载并安装？安装过程可能弹出系统管理员授权窗口。",
            "需要 .NET 10 桌面运行时", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            Process.Start("https://dotnet.microsoft.com/download/dotnet/10.0");
            return false;
        }

        var installer = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-10-win-x64.exe");
        _status.Text = "正在下载 .NET 10 桌面运行时…";
        await DownloadFileAsync(DotNetRuntimeUrl, installer, "下载 .NET 10 桌面运行时");

        _status.Text = "正在安装 .NET 10 桌面运行时（请在弹出的窗口确认）…";
        using (var proc = Process.Start(new ProcessStartInfo(installer)
        {
            Arguments = "/quiet /norestart",
            UseShellExecute = true
        }))
        {
            if (proc != null)
            {
                await Task.Run(() => proc.WaitForExit());
            }
        }

        if (HasDotNet10Desktop())
        {
            _status.Text = "运行时安装完成。";
            return true;
        }

        MessageBox.Show(this,
            "运行时安装似乎未完成，请手动打开 https://dotnet.microsoft.com/download/dotnet/10.0 安装后重试。",
            "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    /// <summary>判断是否已安装 .NET 10.x 的 WindowsDesktop 共享运行时（查共享运行时目录，最快也最直观）。</summary>
    private static bool HasDotNet10Desktop()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App")
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                if (Directory.GetDirectories(root)
                    .Any(x => Path.GetFileName(x).StartsWith("10.", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // 无权限读取时当作未安装，后续让用户手动装
            }
        }
        return false;
    }

    /// <summary>缺少 WebView2 时只提示一次：启动器的内嵌浏览器（微软登录、公告）需要它。</summary>
    private void EnsureWebView2Hint()
    {
        if (_webViewHintShown || HasWebView2())
        {
            return;
        }
        _webViewHintShown = true;

        var answer = MessageBox.Show(this,
            "未检测到 Microsoft Edge WebView2 运行时（Win11 及新版 Win10 一般自带）。\n" +
            "缺少它时启动器的微软登录 / 网页区域会无法显示。\n\n是否现在打开下载页？",
            "WebView2 运行时", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
        {
            Process.Start(WebView2Url);
        }
    }

    private static bool HasWebView2()
    {
        // 常见的两处注册位置；任一存在即认为已安装
        var keys = new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7B2C4-9F0A-4C1B-9E3A-000000000000}",
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7B2C4-9F0A-4C1B-9E3A-000000000000}"
        };
        foreach (var key in keys)
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
            {
                if (k != null)
                {
                    return true;
                }
            }
        }
        // 也检查安装目录兜底
        foreach (var baseDir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            try
            {
                var dir = Path.Combine(baseDir, "Microsoft", "EdgeWebView", "Application");
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "msedgewebview2.exe", SearchOption.AllDirectories).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // 忽略权限问题，按“未检测到”处理
            }
        }
        return false;
    }

    /// <summary>同目录有离线包就直接用，否则按 PackageUrl 下载到临时目录。</summary>
    private async Task<string> AcquirePackageAsync()
    {
        var localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LocalPackageName);
        if (File.Exists(localZip))
        {
            _status.Text = "使用同目录下的离线安装包…";
            _progress.Value = 100;
            return localZip;
        }

        if (string.IsNullOrWhiteSpace(PackageUrl) ||
            !PackageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装包下载地址未配置，请把 " + LocalPackageName + " 放在本程序同目录。");
        }

        var temp = Path.Combine(Path.GetTempPath(), LocalPackageName);
        _status.Text = "正在下载安装包…";
        await DownloadFileAsync(PackageUrl, temp, "下载安装包");
        return temp;
    }

    /// <summary>带进度条的下载（先按 Content-Length 估算百分比，没有长度时显示已下载 KB）。</summary>
    private async Task DownloadFileAsync(string url, string destPath, string label)
    {
        using (var http = new HttpClient())
        {
            http.Timeout = TimeSpan.FromMinutes(30);
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                using (var source = await response.Content.ReadAsStreamAsync())
                using (var file = File.Create(destPath))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        file.Write(buffer, 0, read);
                        received += read;
                        if (total > 0)
                        {
                            _progress.Value = (int)Math.Min(100, received * 100 / total);
                            _status.Text = string.Format("{0}… {1} / {2} KB", label, received / 1024, total / 1024);
                        }
                        else
                        {
                            _status.Text = string.Format("{0}… {1} KB", label, received / 1024);
                        }
                        Application.DoEvents();
                    }
                }
            }
        }
    }

    /// <summary>解压 zip（.NET Framework 的 ExtractToDirectory 不支持覆盖，这里逐条处理并防 Zip Slip）。</summary>
    private static void ExtractPackage(string zipPath, string destDir)
    {
        var root = Path.GetFullPath(destDir);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (name.Trim().Length == 0)
                {
                    continue;
                }

                var full = Path.GetFullPath(Path.Combine(root, name));
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 跳过越界条目，避免 Zip Slip
                }

                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(full);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(full));
                entry.ExtractToFile(full, overwrite: true);
            }
        }
    }

    /// <summary>防止把程序装进数据目录内部（D:\EchoCraftLauncher\Program 除外），避免和实例/资源混在一起。</summary>
    private static bool IsInsideDataRoot(string dir)
    {
        const string dataRoot = "D:\\EchoCraftLauncher";
        var normalized = Path.GetFullPath(dir).TrimEnd('\\');
        var programDir = Path.Combine(dataRoot, "Program");

        if (normalized.Equals(dataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!normalized.StartsWith(dataRoot + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // Program 子目录是默认安装位置，不算“混进数据目录”
        return !(normalized.Equals(programDir, StringComparison.OrdinalIgnoreCase)
                 || normalized.StartsWith(programDir + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>桌面 + 开始菜单快捷方式；用 WScript.Shell 写 .lnk，避免额外依赖。</summary>
    private void CreateShortcuts(string exe, string workingDir)
    {
        if (_desktopShortcut.Checked)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            WriteShortcut(Path.Combine(desktop, "Echo Craft Launcher.lnk"), exe, workingDir);
        }

        try
        {
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Echo Craft Launcher");
            Directory.CreateDirectory(startMenu);
            WriteShortcut(Path.Combine(startMenu, "Echo Craft Launcher.lnk"), exe, workingDir);
        }
        catch
        {
            // 开始菜单写入失败不影响安装结果
        }
    }

    private static void WriteShortcut(string lnkPath, string exe, string workingDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }
        var shell = Activator.CreateInstance(shellType);
        dynamic link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
            null, shell, new object[] { lnkPath });
        link.TargetPath = exe;
        link.WorkingDirectory = workingDir;
        link.IconLocation = exe + ",0";
        link.Description = "Echo Craft Launcher";
        link.Save();
    }

    private void SetUiEnabled(bool enabled)
    {
        _installButton.Enabled = enabled;
        _browseButton.Enabled = enabled;
        _pathBox.Enabled = enabled;
        _desktopShortcut.Enabled = enabled;
        _launchAfter.Enabled = enabled;
        _closeButton.Enabled = !enabled; // 安装过程中禁用退出，避免半途中断
    }
}

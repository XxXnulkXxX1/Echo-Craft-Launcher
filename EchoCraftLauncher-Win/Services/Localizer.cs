using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SwiftCraftLauncher2.Services;

/// <summary>
/// 轻量界面翻译：以中文原文为键查英文，加载界面时遍历可视树替换文本。
/// 这样不用给每个控件加绑定，也能覆盖现有 XAML 里的硬编码文案。
/// 切换语言时重建主窗口（保留当前页签），效果立即生效。
/// </summary>
public static class Localizer
{
    public const string Chinese = "zh";
    public const string English = "en";

    public static bool IsEnglish => App.Core.Config.Language == English;

    private static readonly Dictionary<string, string> En = new()
    {
        // 导航 / 品牌
        ["启动"] = "Play",
        ["下载"] = "Download",
        ["联机"] = "Multiplayer",
        ["外观"] = "Appearance",
        ["设置"] = "Settings",
        ["更多"] = "More",
        ["非官方 · 基于 PCL2 模块结构独立实现"] = "Unofficial · independent implementation inspired by PCL2",

        // 启动页
        ["游戏实例"] = "Instances",
        ["账号"] = "Account",
        ["未登录"] = "Not signed in",
        ["离线模式"] = "Offline mode",
        ["离线模式（仅本地游戏）"] = "Offline mode (local game only)",
        ["尚未选择实例"] = "No instance selected",
        ["点击左侧「新建实例」开始"] = "Create an instance on the left to begin",
        ["启动游戏"] = "Launch game",
        ["就绪"] = "Ready",
        ["启动日志"] = "Launch log",
        ["新建实例"] = "New instance",
        ["删除"] = "Delete",
        ["正版登录"] = "Microsoft login",
        ["离线登录"] = "Offline login",
        ["退出"] = "Sign out",
        ["选择游戏版本"] = "Choose a game version",
        ["请输入实例名字。"] = "Enter an instance name.",
        ["请选择一个游戏版本。"] = "Choose a game version.",
        ["请先在左侧新建或选择一个游戏实例。"] = "Create or select an instance first.",

        // 下载页
        ["游戏版本"] = "Game versions",
        ["模组"] = "Mods",
        ["版本库"] = "Version library",
        ["刷新清单"] = "Refresh",
        ["仅正式版"] = "Releases only",
        ["选中的版本"] = "Selected version",
        ["未选择"] = "None selected",
        ["安装为新实例"] = "Install as new instance",
        ["已安装版本"] = "Installed versions",
        ["打开 mods 文件夹"] = "Open mods folder",
        ["模组下载 · Modrinth"] = "Mods · Modrinth",
        ["输入关键词搜索（sodium、iris、jei…）"] = "Search mods (sodium, iris, jei…)",
        ["只显示与选中实例兼容的模组"] = "Only mods compatible with the selected instance",
        ["分类"] = "Category",
        ["全部"] = "All",
        ["优化"] = "Optimization",
        ["冒险"] = "Adventure",
        ["魔法"] = "Magic",
        ["装饰"] = "Decoration",
        ["科技"] = "Technology",
        ["世界生成"] = "World generation",
        ["实用"] = "Utility",
        ["装备"] = "Equipment",
        ["生物"] = "Mobs",
        ["存储"] = "Storage",
        ["运输"] = "Transportation",
        ["食物"] = "Food",
        ["管理"] = "Management",
        ["机制"] = "Game mechanics",
        ["小游戏"] = "Minigame",
        ["前置库"] = "Libraries",
        ["加载更多模组"] = "Load more mods",
        ["模组信息"] = "Mod info",
        ["搜索并选择一个模组"] = "Search and select a mod",
        ["安装到实例"] = "Install to instance",
        ["安装模组及其前置"] = "Install mod and dependencies",
        ["查看详情"] = "View details",
        ["模组详情"] = "Mod details",
        ["← 返回列表"] = "← Back to list",
        ["截图"] = "Screenshots",
        ["介绍"] = "Description",
        ["安装到选中实例"] = "Install to selected instance",
        ["源码/主页"] = "Source / homepage",
        ["问题反馈"] = "Issue tracker",
        ["搜索并选择一个模组。"] = "Search and pick a mod first.",

        // 设置页
        ["Java 运行环境"] = "Java runtime",
        ["启动时会自动匹配版本要求的 Java；这里可以手动指定"] =
            "Java is matched automatically per version; you can override it here",
        ["重新检测"] = "Rescan",
        ["指定 javaw.exe"] = "Choose javaw.exe",
        ["性能与下载"] = "Performance & downloads",
        ["内存上限"] = "Memory limit",
        ["下载线程"] = "Download threads",
        ["附加 JVM 参数（用空格分隔，可留空）"] = "Extra JVM arguments (space separated, optional)",
        ["下载镜像"] = "Download mirror",
        ["官方源"] = "Official",
        ["BMCLAPI 镜像（国内更快）"] = "BMCLAPI mirror (faster in China)",
        ["离线账号"] = "Offline account",
        ["离线用户名（仅本地使用，不连接认证服务器）"] = "Offline username (local only, no auth server)",
        ["数据目录"] = "Data folder",
        ["打开数据目录"] = "Open data folder",
        ["保存设置"] = "Save settings",
        ["界面语言"] = "Language",
        ["简体中文"] = "Simplified Chinese",
        ["更改后立即生效，所有页面会重建"] = "Applies immediately and rebuilds all pages",
        ["只显示与选中实例兼容"] = "Only compatible with selected instance",
        ["模组介绍"] = "Mod descriptions",
        ["Modrinth 上的介绍多为英文，开启后会在打开详情页时自动机翻成中文（需联网）"] =
            "Most Modrinth descriptions are English. When on, opening a mod page translates them into Chinese (needs network).",
        ["自动把英文模组介绍翻译为中文"] = "Auto-translate English mod descriptions into Chinese",
        ["查看原文"] = "Show original",
        ["查看中文"] = "Show Chinese",
        ["选择右侧实例后即可安装。"] = "Pick an instance on the right to install.",
        ["设置已保存"] = "Settings saved",

        // 外观页
        ["调整主题、主色与动态效果，修改会立即生效并自动保存。"] =
            "Adjust theme, accent color and motion; changes apply and save instantly.",
        ["主题模式"] = "Theme",
        ["深色适合夜间，浅色在明亮环境下更清晰。"] = "Dark suits night use; light is clearer in bright rooms.",
        ["深色"] = "Dark",
        ["浅色"] = "Light",
        ["主色"] = "Accent color",
        ["影响按钮、选中态、进度条与流动背景。"] = "Affects buttons, selection, progress and the flowing background.",
        ["动态效果"] = "Motion",
        ["关闭可降低低配设备的渲染开销。"] = "Turn off to reduce rendering cost on low-end machines.",
        ["流动背景（缓慢漂移的色团）"] = "Flowing background (slow drifting blobs)",
        ["弹簧动效（切页与按钮回弹）"] = "Spring motion (page and button bounce)",
        ["弹窗预览"] = "Dialog preview",
        ["所有提示、确认与错误都使用与应用同色系的自定义弹窗，不再出现白色系统对话框。"] =
            "All alerts, confirmations and errors use themed in-app dialogs instead of white system message boxes.",
        ["预览提示弹窗"] = "Preview info dialog",
        ["预览警告弹窗"] = "Preview warning dialog",
        ["恢复默认外观"] = "Reset appearance",

        // 联机 / 更多
        ["局域网联机"] = "LAN multiplayer",
        ["打开实例目录"] = "Open instance folder",
        ["打开存档目录"] = "Open saves folder",
        ["关于"] = "About",
        ["开发者"] = "Developer",
        ["版本"] = "Version",
        ["致谢"] = "Credits",
        ["请支持正版"] = "Please support the official game",

        // 通用弹窗
        ["提示"] = "Notice",
        ["确认"] = "Confirm",
        ["确定"] = "OK",
        ["取消"] = "Cancel",
        ["删除实例"] = "Delete instance",
        ["离线用户名"] = "Offline username"
    };

    public static string Translate(string source) =>
        IsEnglish && En.TryGetValue(source, out var value) ? value : source;

    /// <summary>遍历可视树，把文本节点换成目标语言。页面重建后会再次调用。</summary>
    public static void Apply(DependencyObject root)
    {
        if (!IsEnglish || root is null)
        {
            return;
        }
        Visit(root);
    }

    private static void Visit(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock text:
                // 只替换单一 Run 的简单文本，含绑定/多段的文本保持原样
                if (text.Inlines.Count == 0)
                {
                    text.Text = Translate(text.Text);
                }
                else if (text.Inlines.Count == 1 && text.Inlines.FirstInline is Run only)
                {
                    only.Text = Translate(only.Text);
                }
                break;
            case Button button when button.Content is string s:
                button.Content = Translate(s);
                break;
            case CheckBox check when check.Content is string cs:
                check.Content = Translate(cs);
                break;
            case RadioButton radio when radio.Content is string rs:
                radio.Content = Translate(rs);
                break;
            case ComboBoxItem item when item.Content is string istr:
                item.Content = Translate(istr);
                break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dependency)
            {
                Visit(dependency);
            }
        }
    }

    /// <summary>语言切换后重建主窗口，保留当前页签。</summary>
    public static void Relanguage()
    {
        var app = Application.Current;
        if (app?.MainWindow is not MainWindow old)
        {
            return;
        }
        var key = old.ActivePageKey;
        var window = new MainWindow(key);
        app.MainWindow = window;
        window.Show();
        old.Close();
    }
}


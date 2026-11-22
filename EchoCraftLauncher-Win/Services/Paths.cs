using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

/// <summary>启动器数据目录，结构与 .minecraft 一致。</summary>
public sealed class Paths
{
    public string Root { get; }
    public string Versions => Path.Combine(Root, "versions");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Assets => Path.Combine(Root, "assets");
    public string Indexes => Path.Combine(Assets, "indexes");
    public string Objects => Path.Combine(Assets, "objects");
    public string VirtualAssets => Path.Combine(Assets, "virtual");
    public string LogConfigs => Path.Combine(Assets, "log_configs");
    public string Instances => Path.Combine(Root, "instances");
    public string Logs => Path.Combine(Root, "logs");
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string ManifestFile => Path.Combine(Root, "version_manifest.json");

    public Paths(string? root = null)
    {
        Root = root ?? DefaultRoot();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Libraries);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Indexes);
        Directory.CreateDirectory(Objects);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Logs);
    }

    public static string DefaultRoot()
    {
        // 游戏数据体积较大，优先放 D 盘（用户偏好）；不可写时退回用户目录
        var d = Path.Combine("D:\\", AppInfo.Id);
        try
        {
            if (Directory.Exists("D:\\"))
            {
                MigrateLegacy(Path.Combine("D:\\", "SwiftCraftLauncher2"), d);
                Directory.CreateDirectory(d);
                return d;
            }
        }
        catch
        {
            // 盘符只读 / 被锁定，继续尝试 AppData
        }
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallback = Path.Combine(appData, AppInfo.Id);
        MigrateLegacy(Path.Combine(appData, "SwiftCraftLauncher2"), fallback);
        return fallback;
    }

    /// <summary>改名后把旧目录整体搬到新名字下，避免已下载的资源白费（同盘 Move 是秒级操作）。</summary>
    private static void MigrateLegacy(string oldDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(oldDir) || Directory.Exists(newDir))
            {
                return;
            }
            Directory.Move(oldDir, newDir);
        }
        catch
        {
            // 迁移失败不影响使用，只是会重新建一份数据目录
        }
    }

    public string VersionDir(string id) => Path.Combine(Versions, id);
    public string VersionJson(string id) => Path.Combine(VersionDir(id), id + ".json");
    public string VersionJar(string id) => Path.Combine(VersionDir(id), id + ".jar");
    public string NativesDir(string id) => Path.Combine(VersionDir(id), "natives");
    public string AssetIndex(string id) => Path.Combine(Indexes, id + ".json");
}

/// <summary>持久化的启动器设置。</summary>
public sealed class LauncherConfig
{
    public string JavaPath { get; set; } = "";
    public int MemoryMb { get; set; } = 2048;
    public string JvmArgs { get; set; } = "";
    public int DownloadThreads { get; set; } = 16;
    public string Mirror { get; set; } = Net.MirrorOfficial;
    public string PlayerName { get; set; } = "Steve";
    public string LastInstance { get; set; } = "";
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public AccountInfo Account { get; set; } = new();

    // 外观
    public string Theme { get; set; } = ThemeManager.ThemeDark;
    public string Accent { get; set; } = "blue";
    public string Language { get; set; } = "zh";
    public bool FlowBackground { get; set; } = true;
    public bool SpringMotion { get; set; } = true;

    /// <summary>模组详情里的英文介绍是否自动翻译成中文（默认关闭，需在设置里主动开启）。</summary>
    public bool TranslateMods { get; set; }

    public static LauncherConfig Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                var text = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<LauncherConfig>(text);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch
        {
            // 配置损坏时使用默认值，不阻止启动
        }
        return new LauncherConfig();
    }

    public void Save(string file)
    {
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, true);
    }
}

/// <summary>账号信息，离线或微软正版。</summary>
public sealed class AccountInfo
{
    public string Type { get; set; } = "offline"; // offline / msa
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Xuid { get; set; } = "";
    public long ExpiresAt { get; set; }

    public bool IsMicrosoft => Type == "msa" && !string.IsNullOrEmpty(AccessToken);

    /// <summary>微软令牌临近过期（留 5 分钟余量）。</summary>
    public bool TokenExpired =>
        Type == "msa" && ExpiresAt > 0 && ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
}

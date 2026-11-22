using System.Collections.ObjectModel;
using System.IO;

namespace SwiftCraftLauncher2.Services;

/// <summary>全局共享状态：目录、配置、实例列表、版本清单缓存、日志广播。</summary>
public sealed class LauncherCore
{
    public Paths Paths { get; }
    public LauncherConfig Config { get; private set; }
    public ObservableCollection<InstanceInfo> Instances { get; } = new();
    public List<ManifestVersion> Versions { get; private set; } = new();
    public List<JavaRuntime> Java { get; private set; } = new();

    public event Action<string>? Log;

    public LauncherCore()
    {
        Paths = new Paths();
        Config = LauncherConfig.Load(Paths.ConfigFile);
        Net.Mirror = Config.Mirror == Net.MirrorBmcl ? Net.MirrorBmcl : Net.MirrorOfficial;
        RefreshInstances();
    }

    public void LogLine(string message)
    {
        Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void SaveConfig()
    {
        try
        {
            Config.Save(Paths.ConfigFile);
        }
        catch (IOException)
        {
            LogLine("配置保存失败（文件被占用）");
        }
    }

    public void RefreshInstances()
    {
        var installed = InstalledVersionIds();
        var loaded = InstanceStore.LoadAll(Paths, installed);
        Instances.Clear();
        foreach (var item in loaded)
        {
            Instances.Add(item);
        }
    }

    public HashSet<string> InstalledVersionIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Paths.Versions))
        {
            return result;
        }
        foreach (var dir in Directory.GetDirectories(Paths.Versions))
        {
            var id = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, id + ".json")))
            {
                result.Add(id);
            }
        }
        return result;
    }

    public async Task<List<ManifestVersion>> LoadVersionsAsync(bool force = false)
    {
        Versions = await MojangApi.GetVersionsAsync(Paths, force, CancellationToken.None).ConfigureAwait(false);
        return Versions;
    }

    public async Task<List<JavaRuntime>> DetectJavaAsync()
    {
        Java = await Task.Run(() => JavaService.DetectAsync(Config.JavaPath, CancellationToken.None)).ConfigureAwait(false);
        return Java;
    }
}

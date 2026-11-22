using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SwiftCraftLauncher2.Services;

public sealed class LaunchHandle
{
    public Process Process { get; set; } = null!;
    public string LogPath { get; init; } = "";
    public event Action<string>? LineReceived;
    internal void Raise(string line) => LineReceived?.Invoke(line);
}

/// <summary>组装 JVM 参数并真正启动游戏进程。</summary>
public static class LaunchService
{
    public static LaunchHandle Launch(
        Paths paths,
        InstanceInfo instance,
        ResolvedVersion version,
        JavaRuntime java,
        AccountInfo account,
        int memoryMb,
        string extraJvmArgs,
        int windowWidth,
        int windowHeight,
        CancellationToken ct)
    {
        var instanceDir = instance.Directory.Length > 0
            ? instance.Directory
            : Path.Combine(paths.Instances, InstanceStore.Sanitize(instance.Name));
        Directory.CreateDirectory(instanceDir);

        var nativesDir = paths.NativesDir(version.Id);
        var classpath = VersionResolver.Classpath(version, paths, Features());
        if (classpath.Count == 0)
        {
            throw new LauncherException("classpath 为空：依赖文件不完整，请重新补全文件");
        }

        var assetsIndex = version.AssetIndexId;
        if (assetsIndex.Length == 0)
        {
            assetsIndex = "legacy";
        }

        var (uuid, token, userType) = ResolveIdentity(account);

        var classpathValue = string.Join(Path.PathSeparator, classpath);

        var context = new Dictionary<string, string>
        {
            ["auth_player_name"] = account.Name,
            ["version_name"] = version.DisplayId,
            ["game_directory"] = instanceDir,
            ["assets_root"] = paths.Assets,
            ["game_assets"] = paths.VirtualAssets,
            ["assets_index_name"] = assetsIndex,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = token,
            ["auth_session"] = token,
            ["auth_xuid"] = account.Xuid,
            ["user_type"] = userType,
            ["version_type"] = version.Type,
            ["user_properties"] = "{}",
            ["clientid"] = "00000000402b5328",
            ["library_directory"] = paths.Libraries,
            ["classpath"] = classpathValue,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = AppInfo.Id,
            ["launcher_version"] = AppInfo.Version,
            ["resolution_width"] = windowWidth.ToString(),
            ["resolution_height"] = windowHeight.ToString()
        };

        var jvmTokens = VersionResolver.JvmArguments(version, Features());
        var jvmArgs = new List<string>
        {
            $"-Xmx{memoryMb}M",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MaxGCPauseMillis=50",
            "-XX:G1HeapRegionSize=32M"
        };

        foreach (var tokenText in jvmTokens)
        {
            var replaced = VersionResolver.ReplacePlaceholders(tokenText, context);
            if (replaced.Length == 0)
            {
                continue;
            }
            jvmArgs.Add(replaced);
        }

        var cpIndex = jvmArgs.FindIndex(a => a == "-cp" || a == "-classpath");
        if (cpIndex >= 0)
        {
            jvmArgs[cpIndex + 1] = classpathValue;
        }
        else
        {
            jvmArgs.Add("-cp");
            jvmArgs.Add(classpathValue);
        }

        jvmArgs.Add("-Djava.library.path=" + nativesDir);
        jvmArgs.Add("-Dminecraft.launcher.brand=" + AppInfo.Id);
        jvmArgs.Add("-Dminecraft.launcher.version=" + AppInfo.Version);
        jvmArgs.Add("-Dlog4j2.formatMsgNoLookups=true");
        jvmArgs.Add("-Dfile.encoding=UTF-8");
        jvmArgs.Add("-Dstdout.encoding=UTF-8");
        jvmArgs.Add("-Dstderr.encoding=UTF-8");

        foreach (var extra in extraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            jvmArgs.Add(extra);
        }

        var gameArgs = new List<string> { version.MainClass };
        foreach (var tokenText in VersionResolver.GameArguments(version, Features()))
        {
            var replaced = VersionResolver.ReplacePlaceholders(tokenText, context);
            if (replaced.Length > 0)
            {
                gameArgs.Add(replaced);
            }
        }

        var logDir = Path.Combine(instanceDir, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "latest.log");
        try
        {
            File.Delete(logPath);
        }
        catch (IOException)
        {
            // 旧日志被占用时直接覆盖
        }

        var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
        writer.WriteLine($"# Echo Craft Launcher · {AppInfo.Version} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"# Java: {java.Path}");
        writer.WriteLine($"# 实例: {instance.Name} / 版本: {version.DisplayId}");
        writer.WriteLine($"# 内存: {memoryMb} MB");
        writer.Flush();

        var handle = new LaunchHandle { LogPath = logPath };

        var info = new ProcessStartInfo
        {
            FileName = java.Path,
            WorkingDirectory = instanceDir,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in jvmArgs.Concat(gameArgs))
        {
            info.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        void Append(string line)
        {
            lock (writer)
            {
                writer.WriteLine(line);
                writer.Flush();
            }
            handle.Raise(line);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append("[STDERR] " + e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            // WaitForExit() 会等待异步输出回调排空，避免关闭 writer 后被迟到的回调写入
            try
            {
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // 进程未启动
            }
            lock (writer)
            {
                writer.WriteLine($"# 游戏进程已退出，代码 {process.ExitCode}");
                writer.Dispose();
            }
            handle.Raise($"游戏进程已退出，代码 {process.ExitCode}");
        };

        ct.ThrowIfCancellationRequested();
        if (!process.Start())
        {
            throw new LauncherException("无法启动 Java 进程");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        handle.Process = process;
        return handle;
    }

    public static IReadOnlyDictionary<string, bool> Features() => new Dictionary<string, bool>
    {
        ["is_demo_user"] = false,
        ["has_custom_resolution"] = false,
        ["has_quick_plays_support"] = false,
        ["is_quick_play_singleplayer"] = false,
        ["is_quick_play_multiplayer"] = false,
        ["is_quick_play_realms"] = false
    };

    /// <summary>在线账号直接用微软令牌；离线账号生成离线 UUID（与官方离线模式一致）。</summary>
    public static (string Uuid, string Token, string UserType) ResolveIdentity(AccountInfo account)
    {
        if (account.IsMicrosoft)
        {
            if (account.TokenExpired)
            {
                throw new LauncherException("微软登录令牌已过期，请在启动页重新登录正版账号。");
            }
            return (account.Uuid, account.AccessToken, "msa");
        }
        var name = account.Name.Length == 0 ? "Steve" : account.Name;
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var uuid = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
        return (uuid, "0", "legacy");
    }
}


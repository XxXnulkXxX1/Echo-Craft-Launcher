using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SwiftCraftLauncher2.Services;

public sealed class JavaRuntime
{
    public string Path { get; init; } = "";      // javaw.exe / java.exe 的完整路径
    public string Version { get; init; } = "";   // 原始版本号，如 25.0.4
    public int Major { get; init; }              // 主版本，如 25
    public string Vendor { get; init; } = "";
    public string Source { get; init; } = "";

    public string Display => $"Java {Major}  ·  {Vendor}  ·  {System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Path))}";
}

/// <summary>Java 运行时探测。</summary>
public static class JavaService
{
    private static readonly Regex VersionRegex = new(@"version\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex MajorRegex = new(@"^(\d+)", RegexOptions.Compiled);

    public static async Task<List<JavaRuntime>> DetectAsync(string? preferredPath, CancellationToken ct = default)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            candidates.Add(preferredPath);
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", "javaw.exe"));
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0 || !trimmed.Contains("java", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            candidates.Add(Path.Combine(trimmed, "javaw.exe"));
            candidates.Add(Path.Combine(trimmed, "java.exe"));
        }

        candidates.AddRange(FromRegistry());
        candidates.AddRange(FromKnownFolders());

        var runtimes = new List<JavaRuntime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate) || !seen.Add(candidate))
            {
                continue;
            }
            var runtime = await ProbeAsync(candidate, ct).ConfigureAwait(false);
            if (runtime is not null)
            {
                runtimes.Add(runtime);
            }
        }

        // 同一路径已在上面去重；不同厂商的同主版本都保留，交给 Pick 按版本号挑选
        return runtimes
            .OrderByDescending(r => r.Major)
            .ThenByDescending(r => r.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>启动 java -version，读取标准输出解析版本与厂商。</summary>
    public static async Task<JavaRuntime?> ProbeAsync(string exePath, CancellationToken ct = default)
    {
        var consoleExe = exePath;
        if (exePath.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            var javaExe = Path.Combine(Path.GetDirectoryName(exePath)!, "java.exe");
            if (File.Exists(javaExe))
            {
                consoleExe = javaExe;
            }
        }

        var output = new List<string>();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = consoleExe,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.Add(e.Data);
                }
            }
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => exit.TrySetResult(process.ExitCode);

        try
        {
            if (!process.Start())
            {
                return null;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var completed = await Task.WhenAny(exit.Task, Task.Delay(TimeSpan.FromSeconds(12), ct)).ConfigureAwait(false);
            if (completed != exit.Task)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 进程已退出
                }
                try
                {
                    // 等一小会儿让 Exited 触发，避免残留句柄
                    await process.WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // 超时无妨
                }
                return null;
            }
        }
        catch
        {
            return null;
        }

        string text;
        lock (output)
        {
            text = string.Join('\n', output);
        }
        var match = VersionRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var version = match.Groups[1].Value;
        var majorMatch = MajorRegex.Match(version);
        if (!majorMatch.Success)
        {
            return null;
        }
        var major = int.Parse(majorMatch.Groups[1].Value);
        if (version.StartsWith("1.", StringComparison.Ordinal))
        {
            // 1.8.0_452 这类旧版编号
            var parts = version.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out var legacyMajor))
            {
                major = legacyMajor;
            }
        }

        var vendor = "Unknown";
        if (text.Contains("Temurin", StringComparison.OrdinalIgnoreCase)) vendor = "Eclipse Temurin";
        else if (text.Contains("AdoptOpenJDK", StringComparison.OrdinalIgnoreCase)) vendor = "AdoptOpenJDK";
        else if (text.Contains("GraalVM", StringComparison.OrdinalIgnoreCase)) vendor = "GraalVM";
        else if (text.Contains("Zulu", StringComparison.OrdinalIgnoreCase)) vendor = "Azul Zulu";
        else if (text.Contains("Corretto", StringComparison.OrdinalIgnoreCase)) vendor = "Amazon Corretto";
        else if (text.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) vendor = "Microsoft OpenJDK";
        else if (text.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase)) vendor = "OpenJDK";
        else if (text.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase)) vendor = "Oracle JDK";

        return new JavaRuntime { Path = exePath, Version = version, Major = major, Vendor = vendor };
    }

    private static List<string> FromRegistry()
    {
        var result = new List<string>();
        var roots = new[]
        {
            @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\JRE", @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\Eclipse Adoptium\JDK", @"SOFTWARE\Eclipse Foundation\JDK",
            @"SOFTWARE\Microsoft\JDK", @"SOFTWARE\Azul Systems\Zulu", @"SOFTWARE\Amazon\Corretto",
            @"SOFTWARE\BellSoft\Liberica"
        };

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var root in roots)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(root);
                    if (key is null)
                    {
                        continue;
                    }
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub?.GetValue("JavaHome") is string home && home.Length > 0)
                        {
                            result.Add(Path.Combine(home, "bin", "javaw.exe"));
                        }
                        if (sub?.GetValue("InstallationPath") is string install && install.Length > 0)
                        {
                            result.Add(Path.Combine(install, "bin", "javaw.exe"));
                        }
                    }
                }
                catch
                {
                    // 注册表权限异常忽略
                }
            }
        }
        return result;
    }

    private static List<string> FromKnownFolders()
    {
        var result = new List<string>();
        var roots = new List<string>();
        foreach (var drive in new[] { "C:", "D:", "E:" })
        {
            roots.Add($"{drive}\\Program Files\\Java");
            roots.Add($"{drive}\\Program Files\\Eclipse Adoptium");
            roots.Add($"{drive}\\Program Files\\Eclipse Foundation");
            roots.Add($"{drive}\\Program Files\\Amazon Corretto");
            roots.Add($"{drive}\\Program Files\\Zulu");
            roots.Add($"{drive}\\Program Files\\BellSoft");
            roots.Add($"{drive}\\Program Files\\Microsoft");
            roots.Add($"{drive}\\Program Files\\GraalVM");
            roots.Add($"{drive}\\Program Files\\Android\\Android Studio\\jbr");
            roots.Add($"{drive}\\Program Files\\JetBrains");
            roots.Add($"{drive}\\Program Files (x86)\\Java");
            roots.Add($"{drive}\\Java");
            roots.Add($"C:\\Users\\{Environment.UserName}\\.jdks");
            roots.Add($"C:\\Users\\{Environment.UserName}\\scoop\\apps");
            roots.Add($"C:\\Users\\{Environment.UserName}\\AppData\\Local\\Programs\\Eclipse Adoptium");
        }

        var depthTwoParents = new[]
        {
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\AppData\\Local\\Programs",
            "D:\\Program Files"
        };

        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                result.Add(Path.Combine(root, "bin", "javaw.exe"));
            }
            if (!Directory.Exists(root))
            {
                continue;
            }
            string[] children;
            try
            {
                children = Directory.GetDirectories(root);
            }
            catch
            {
                continue;
            }
            foreach (var child in children)
            {
                result.Add(Path.Combine(child, "bin", "javaw.exe"));
                result.Add(Path.Combine(child, "Contents", "Home", "bin", "javaw.exe"));
            }
        }

        foreach (var parent in depthTwoParents)
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }
            try
            {
                foreach (var child in Directory.GetDirectories(parent))
                {
                    result.Add(Path.Combine(child, "bin", "javaw.exe"));
                }
            }
            catch
            {
                // 忽略无权限目录
            }
        }
        return result;
    }

    /// <summary>挑选与所需主版本最匹配的 Java；没有则返回 null。</summary>
    public static JavaRuntime? Pick(List<JavaRuntime> list, int requiredMajor)
    {
        if (list.Count == 0)
        {
            return null;
        }
        var exact = list.Where(j => j.Major == requiredMajor).OrderByDescending(j => j.Version).FirstOrDefault();
        if (exact is not null)
        {
            return exact;
        }
        var higher = list.Where(j => j.Major > requiredMajor).OrderBy(j => j.Major).ThenByDescending(j => j.Version).FirstOrDefault();
        return higher ?? list.OrderByDescending(j => j.Major).First();
    }
}

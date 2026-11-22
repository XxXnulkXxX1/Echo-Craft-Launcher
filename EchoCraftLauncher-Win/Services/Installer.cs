using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

public sealed class InstallProgress
{
    public string Stage { get; set; } = "";
    public string Detail { get; set; } = "";
    public double Fraction { get; set; }
    public long Bytes { get; set; }
    public double Speed { get; set; }
}

public sealed class InstallResult
{
    public ResolvedVersion Version { get; set; } = new();
    public int RequiredJava { get; set; }
    public int DownloadTasks { get; set; }
    public long DownloadedBytes { get; set; }
}

/// <summary>补全一个版本所需的全部文件：客户端 Jar、依赖库、资源索引与资源对象、natives。</summary>
public sealed class Installer
{
    private readonly Paths _paths;
    private readonly int _threads;
    private readonly Action<string>? _log;

    public Installer(Paths paths, int threads, Action<string>? log = null)
    {
        _paths = paths;
        _threads = Math.Clamp(threads, 1, 24);
        _log = log;
    }

    private sealed class Job
    {
        public string Url { get; init; } = "";
        public string Target { get; init; } = "";
        public string Sha1 { get; init; } = "";
        public long Size { get; init; }
    }

    public async Task<InstallResult> InstallAsync(
        string versionId, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var versionFile = _paths.VersionJson(versionId);
        if (!File.Exists(versionFile))
        {
            _log?.Invoke($"正在获取 {versionId} 的版本信息…");
            await MojangApi.FetchVersionJsonTextAsync(_paths, versionId, null, ct).ConfigureAwait(false);
        }

        var version = VersionResolver.Resolve(versionId, _paths);
        _log?.Invoke($"版本 {version.DisplayId}（{version.Type}），需要 Java {version.JavaMajor}+");

        // ---------- 客户端 Jar ----------
        var jobs = new List<Job>();
        var clientArtifact = FindClientDownload(version);
        if (clientArtifact is not null)
        {
            var target = _paths.VersionJar(version.JarId);
            if (!IsComplete(target, clientArtifact.Value.Size))
            {
                jobs.Add(new Job
                {
                    Url = clientArtifact.Value.Url,
                    Target = target,
                    Sha1 = clientArtifact.Value.Sha1,
                    Size = clientArtifact.Value.Size
                });
            }
        }

        // ---------- 依赖库 + natives ----------
        var nativePaths = new List<string>();
        foreach (var lib in version.Libraries)
        {
            ct.ThrowIfCancellationRequested();
            JsonElement? rules = lib.TryGetProperty("rules", out var ruleElement) ? ruleElement : null;
            if (!VersionResolver.RuleAllows(rules))
            {
                continue;
            }
            var hasDownloads = lib.TryGetProperty("downloads", out _);
            var hasUrl = lib.TryGetProperty("url", out _);
            if (!hasDownloads && !hasUrl)
            {
                continue; // 仅用于 log4j 等无下载地址的条目
            }

            var artifactUrl = VersionResolver.LibraryUrl(lib);
            var artifactPath = VersionResolver.LibraryPath(lib, _paths);
            var size = 0L;
            var sha1 = "";
            if (hasDownloads
                && lib.TryGetProperty("downloads", out var downloads)
                && downloads.ValueKind == JsonValueKind.Object
                && downloads.TryGetProperty("artifact", out var artifact)
                && artifact.ValueKind == JsonValueKind.Object)
            {
                sha1 = VersionResolver.Str(artifact, "sha1");
                size = artifact.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed) ? parsed : 0;
            }
            if (!IsComplete(artifactPath, size))
            {
                jobs.Add(new Job { Url = artifactUrl, Target = artifactPath, Sha1 = sha1, Size = size });
            }

            foreach (var native in VersionResolver.Natives(lib))
            {
                var nativePath = Path.Combine(_paths.Libraries, native.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!nativePaths.Contains(nativePath, StringComparer.OrdinalIgnoreCase))
                {
                    nativePaths.Add(nativePath);
                }
                if (!IsComplete(nativePath, native.Size))
                {
                    jobs.Add(new Job { Url = native.Url, Target = nativePath, Sha1 = native.Sha1, Size = native.Size });
                }
            }
        }

        // ---------- 资源索引 ----------
        var assetIndexId = version.AssetIndexId;
        if (assetIndexId.Length > 0 && version.AssetIndex is not null)
        {
            var index = version.AssetIndex.Value;
            var indexUrl = VersionResolver.Str(index, "url");
            var indexSha1 = VersionResolver.Str(index, "sha1");
            var indexSize = index.TryGetProperty("size", out var indexSizeElement) && indexSizeElement.TryGetInt64(out var indexParsed)
                ? indexParsed
                : 0;
            var indexFile = _paths.AssetIndex(assetIndexId);
            if (!IsComplete(indexFile, indexSize))
            {
                jobs.Add(new Job { Url = indexUrl, Target = indexFile, Sha1 = indexSha1, Size = indexSize });
            }
        }

        Report(progress, "准备", $"待下载文件 {jobs.Count} 个", 0, 0, 0);

        // ---------- 阶段一：Jar / 库 / 索引（0 → 0.35）----------
        var bytes = await RunPoolAsync(jobs, 0.0, 0.35, progress, "下载文件", ct).ConfigureAwait(false);

        // ---------- 阶段二：资源对象（0.35 → 0.97）----------
        var assetJobs = new List<Job>();
        if (assetIndexId.Length > 0)
        {
            var indexFile = _paths.AssetIndex(assetIndexId);
            if (File.Exists(indexFile))
            {
                assetJobs.AddRange(BuildAssetJobs(indexFile));
            }
        }

        _log?.Invoke($"资源对象待下载 {assetJobs.Count} 个");
        bytes += await RunPoolAsync(assetJobs, 0.35, 0.97, progress, "下载资源", ct).ConfigureAwait(false);

        // ---------- 解压 natives ----------
        Report(progress, "解压", "正在释放 natives", 0.99, bytes, 0);
        ExtractNatives(version, nativePaths);

        var result = new InstallResult
        {
            Version = version,
            RequiredJava = version.JavaMajor,
            DownloadTasks = jobs.Count + assetJobs.Count,
            DownloadedBytes = bytes
        };
        _log?.Invoke($"文件补全完成：{result.DownloadTasks} 个任务，实下载 {bytes / 1024.0 / 1024.0:F1} MB");
        Report(progress, "完成", "文件已就绪", 1, bytes, 0);
        return result;
    }

    /// <summary>已知大小时比大小，未知大小但存在且非空也算完成，避免永远重复下载。</summary>
    private static bool IsComplete(string path, long size)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        var length = new FileInfo(path).Length;
        if (length == 0)
        {
            return false;
        }
        return size <= 0 || length == size;
    }

    private static (string Url, string Sha1, long Size)? FindClientDownload(ResolvedVersion version)
    {
        foreach (var data in version.Chain.AsEnumerable().Reverse())
        {
            if (data.TryGetProperty("downloads", out var downloads)
                && downloads.ValueKind == JsonValueKind.Object
                && downloads.TryGetProperty("client", out var client)
                && client.ValueKind == JsonValueKind.Object)
            {
                var url = VersionResolver.Str(client, "url");
                if (url.Length == 0)
                {
                    return null;
                }
                var sha1 = VersionResolver.Str(client, "sha1");
                var size = client.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed) ? parsed : 0;
                return (url, sha1, size);
            }
        }
        return null;
    }

    private List<Job> BuildAssetJobs(string indexFile)
    {
        var jobs = new List<Job>();
        using var doc = JsonDocument.Parse(File.ReadAllText(indexFile));
        if (!doc.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
        {
            return jobs;
        }
        foreach (var entry in objects.EnumerateObject())
        {
            var hash = VersionResolver.Str(entry.Value, "hash");
            if (hash.Length < 40)
            {
                continue;
            }
            var size = entry.Value.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed)
                ? parsed
                : 0;
            var target = Path.Combine(_paths.Objects, hash[..2], hash);
            if (!IsComplete(target, size))
            {
                jobs.Add(new Job
                {
                    Url = VersionResolver.ResourcesBase + hash[..2] + "/" + hash,
                    Target = target,
                    Sha1 = hash,
                    Size = size
                });
            }
        }
        return jobs;
    }

    /// <summary>并发下载；取消与失败可区分，进度按区间映射避免进度条回退。</summary>
    private async Task<long> RunPoolAsync(
        List<Job> jobs, double start, double end, IProgress<InstallProgress>? progress, string stage,
        CancellationToken outerToken)
    {
        if (jobs.Count == 0)
        {
            Report(progress, stage, "无需下载", end, 0, 0);
            return 0;
        }

        var done = 0;
        long bytes = 0;
        var failures = new List<Exception>();
        var gate = new object();
        var stopwatch = Stopwatch.StartNew();
        var lastReport = -1.0;
        long lastBytes = 0;
        double lastTime = 0;
        double speed = 0;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        var token = linked.Token;

        void Tick(bool force)
        {
            lock (gate)
            {
                var now = stopwatch.Elapsed.TotalSeconds;
                if (!force && now - lastReport < 0.25)
                {
                    return;
                }
                lastReport = now;
                if (now - lastTime >= 1.0)
                {
                    speed = (bytes - lastBytes) / Math.Max(now - lastTime, 0.001);
                    lastBytes = bytes;
                    lastTime = now;
                }
                var fraction = start + (end - start) * ((double)done / jobs.Count);
                Report(progress, stage, $"{done}/{jobs.Count}", fraction, bytes, speed);
            }
        }

        using var semaphore = new SemaphoreSlim(_threads);
        var tasks = jobs.Select(async job =>
        {
            var acquired = false;
            try
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                acquired = true;
                long local = 0;
                await Net.DownloadAsync(
                    job.Url, job.Target,
                    job.Sha1.Length > 0 ? job.Sha1 : null,
                    job.Size > 0 ? job.Size : null,
                    (read, _) =>
                    {
                        var delta = read - local;
                        local = read;
                        if (delta > 0)
                        {
                            Interlocked.Add(ref bytes, delta);
                        }
                    },
                    token).ConfigureAwait(false);
                lock (gate)
                {
                    done++;
                }
                Tick(false);
            }
            catch (OperationCanceledException)
            {
                linked.Cancel();
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    failures.Add(ex);
                }
                linked.Cancel(); // 一个失败就停同批任务，避免继续白耗流量
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }
            }
        }).ToList();

        // 所有任务都在信号量作用域内结束，Dispose 不会早于任务退出
        await Task.WhenAll(tasks).ConfigureAwait(false);
        Tick(true);

        if (outerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(outerToken);
        }
        if (failures.Count > 0)
        {
            throw new LauncherException($"有 {failures.Count} 个文件下载失败：{failures[0].Message}");
        }
        return bytes;
    }

    private static void Report(IProgress<InstallProgress>? progress, string stage, string detail,
        double fraction, long bytes, double speed)
    {
        progress?.Report(new InstallProgress
        {
            Stage = stage,
            Detail = detail,
            Fraction = Math.Clamp(fraction, 0, 1),
            Bytes = bytes,
            Speed = speed
        });
    }

    private void ExtractNatives(ResolvedVersion version, List<string> jars)
    {
        var nativesDir = _paths.NativesDir(version.Id);
        Directory.CreateDirectory(nativesDir);
        var rootPath = Path.GetFullPath(nativesDir) + Path.DirectorySeparatorChar;

        foreach (var jar in jars.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(jar))
            {
                continue;
            }
            try
            {
                using var archive = ZipFile.OpenRead(jar);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.Length == 0 || name.EndsWith('/'))
                    {
                        continue;
                    }
                    if (name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".pom", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var target = Path.GetFullPath(Path.Combine(nativesDir, name));
                    // Zip Slip 防护：拒绝写到 natives 目录之外
                    if (!target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _log?.Invoke($"跳过可疑压缩包路径：{name}");
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                }
            }
            catch (InvalidDataException ex)
            {
                _log?.Invoke($"natives 解压失败：{jar}（{ex.Message}）");
            }
        }
    }
}

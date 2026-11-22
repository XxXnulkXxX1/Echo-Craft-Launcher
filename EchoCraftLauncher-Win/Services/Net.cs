using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

/// <summary>版本清单里的一个条目。</summary>
public sealed class ManifestVersion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "release";
    public string ReleaseTime { get; set; } = "";
    public string Url { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string TypeLabel => Type == "release" ? "正式版" : Type == "snapshot" ? "快照" : Type;
}

/// <summary>网络层：镜像替换、带重试的 JSON 请求与文件下载。</summary>
public static class Net
{
    // 官方源较慢时可切换到 BMCLAPI；映射规则参考社区公开的镜像约定
    public const string MirrorOfficial = "official";
    public const string MirrorBmcl = "bmclapi";

    public static string Mirror { get; set; } = MirrorOfficial;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Id}/{AppInfo.Version}");
        return client;
    }

    private static readonly (string Host, string Replacement)[] HostMap =
    {
        ("piston-meta.mojang.com", "bmclapi2.bangbang93.com"),
        ("launchermeta.mojang.com", "bmclapi2.bangbang93.com"),
        ("launcher.mojang.com", "bmclapi2.bangbang93.com"),
        ("libraries.minecraft.net", "bmclapi2.bangbang93.com/maven"),
        ("resources.download.minecraft.net", "bmclapi2.bangbang93.com/assets"),
    };

    public static string MapUrl(string url)
    {
        if (Mirror != MirrorBmcl)
        {
            return url;
        }
        foreach (var (host, replacement) in HostMap)
        {
            var marker = $"//{host}/";
            if (url.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return url.Replace(marker, $"//{replacement}/", StringComparison.OrdinalIgnoreCase);
            }
        }
        return url;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default, int retries = 2)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                using var response = await Client.GetAsync(MapUrl(url), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                if (attempt < retries)
                {
                    await Task.Delay(700 * (attempt + 1), ct).ConfigureAwait(false);
                }
            }
        }
        throw new LauncherException($"请求失败：{url}（{last?.Message}）");
    }

    /// <summary>下载二进制内容（图片等小文件）。</summary>
    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherException($"请求失败（HTTP {(int)response.StatusCode}）：{url}");
        }
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct = default, int retries = 2)    {
        var text = await GetStringAsync(url, ct, retries).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new LauncherException($"返回内容不是合法 JSON：{url}", ex);
        }
    }

    public static async Task<JsonDocument> PostJsonAsync(string url, object payload, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherException($"请求失败（HTTP {(int)response.StatusCode}）：{url}\n{Trim(body)}");
        }
        return JsonDocument.Parse(body);
    }

    public static async Task<JsonDocument> PostFormAsync(string url, IDictionary<string, string> form, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherException($"请求失败（HTTP {(int)response.StatusCode}）：{url}\n{Trim(body)}");
        }
        return JsonDocument.Parse(body);
    }

    /// <summary>下载文件；已存在且大小/哈希匹配时直接跳过。</summary>
    public static async Task DownloadAsync(
        string url,
        string destination,
        string? sha1,
        long? size,
        Action<long, long>? progress,
        CancellationToken ct,
        int retries = 3)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destination))
        {
            var info = new FileInfo(destination);
            if (size is > 0 && info.Length == size)
            {
                return;
            }
        }

        Exception? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            var temp = destination + ".part";
            try
            {
                using var response = await Client.GetAsync(MapUrl(url), HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? size ?? 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        done += read;
                        progress?.Invoke(done, total);
                    }
                }

                var result = new FileInfo(temp);
                if (size is > 0 && result.Length != size)
                {
                    throw new IOException($"文件大小不符（期望 {size}，实际 {result.Length}）");
                }
                if (!string.IsNullOrEmpty(sha1))
                {
                    var actual = await ComputeSha1Async(temp, ct).ConfigureAwait(false);
                    if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("SHA1 校验失败");
                    }
                }

                File.Move(temp, destination, true);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                TryDelete(temp);
                if (attempt < retries)
                {
                    await Task.Delay(800 * (attempt + 1), ct).ConfigureAwait(false);
                }
            }
        }
        if (last is IOException && File.Exists(destination))
        {
            throw new LauncherException($"文件被占用或无法写入：{destination}\n如果是正在运行的游戏，请先关闭游戏后重试。", last);
        }
        throw new LauncherException($"下载失败：{url}（{last?.Message}）");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不影响重试
        }
    }

    public static async Task<string> ComputeSha1Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Trim(string text) => text.Length > 400 ? text[..400] : text;
}

/// <summary>可展示给玩家的启动器错误。</summary>
public class LauncherException : Exception
{
    public LauncherException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class AppInfo
{
    public const string Name = "Echo Craft Launcher";
    public const string Version = "1.0.0";
    public const string Developer = "XxXnulkXxX";
    public const string Id = "EchoCraftLauncher";
}


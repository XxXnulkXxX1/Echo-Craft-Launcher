using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwiftCraftLauncher2.Services;

/// <summary>Mojang、Fabric、Quilt 的元数据接口。</summary>
public static class MojangApi
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string FabricMeta = "https://meta.fabricmc.net/v2";
    public const string QuiltMeta = "https://meta.quiltmc.org/v3";

    private static string? _manifestCache;
    private static DateTime _manifestCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim ManifestLock = new(1, 1);

    public static async Task<string> GetManifestTextAsync(Paths paths, bool force, CancellationToken ct)
    {
        await ManifestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetManifestTextInnerAsync(paths, force, ct).ConfigureAwait(false);
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    private static async Task<string> GetManifestTextInnerAsync(Paths paths, bool force, CancellationToken ct)
    {
        if (!force && _manifestCache is not null && DateTime.UtcNow - _manifestCacheTime < TimeSpan.FromMinutes(30))
        {
            return _manifestCache;
        }
        if (!force && File.Exists(paths.ManifestFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(paths.ManifestFile);
            if (age < TimeSpan.FromMinutes(30))
            {
                var cached = await File.ReadAllTextAsync(paths.ManifestFile, ct).ConfigureAwait(false);
                if (cached.Contains("\"versions\""))
                {
                    _manifestCache = cached;
                    _manifestCacheTime = DateTime.UtcNow;
                    return cached;
                }
            }
        }

        var text = await Net.GetStringAsync(ManifestUrl, ct, 3).ConfigureAwait(false);
        await File.WriteAllTextAsync(paths.ManifestFile, text, ct).ConfigureAwait(false);
        _manifestCache = text;
        _manifestCacheTime = DateTime.UtcNow;
        return text;
    }

    public static async Task<List<ManifestVersion>> GetVersionsAsync(Paths paths, bool force, CancellationToken ct)
    {
        var text = await GetManifestTextAsync(paths, force, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var list = new List<ManifestVersion>();
        if (doc.RootElement.TryGetProperty("versions", out var versions))
        {
            foreach (var item in versions.EnumerateArray())
            {
                list.Add(new ManifestVersion
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Type = item.TryGetProperty("type", out var type) ? type.GetString() ?? "release" : "release",
                    ReleaseTime = item.TryGetProperty("releaseTime", out var time) ? time.GetString() ?? "" : "",
                    Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? "" : ""
                });
            }
        }
        if (list.Count == 0)
        {
            throw new LauncherException("版本清单为空或格式异常");
        }
        return list;
    }

    /// <summary>获取并缓存原版版本 JSON，返回其文本。</summary>
    public static async Task<string> FetchVersionJsonTextAsync(Paths paths, string versionId, string? fallbackUrl, CancellationToken ct)
    {
        var local = paths.VersionJson(versionId);
        if (File.Exists(local))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(local, ct).ConfigureAwait(false);
                using var probe = JsonDocument.Parse(existing);
                return existing;
            }
            catch
            {
                // 本地文件损坏则重新获取
            }
        }

        var url = fallbackUrl;
        if (string.IsNullOrEmpty(url))
        {
            var versions = await GetVersionsAsync(paths, false, ct).ConfigureAwait(false);
            url = versions.FirstOrDefault(v => v.Id == versionId)?.Url;
        }
        if (string.IsNullOrEmpty(url))
        {
            throw new LauncherException($"版本清单中找不到 {versionId}");
        }

        var text = await Net.GetStringAsync(url, ct, 3).ConfigureAwait(false);
        using (var doc = JsonDocument.Parse(text))
        {
            if (!doc.RootElement.TryGetProperty("id", out _))
            {
                throw new LauncherException($"版本 JSON 格式异常：{versionId}");
            }
        }
        Directory.CreateDirectory(paths.VersionDir(versionId));
        await File.WriteAllTextAsync(local, text, ct).ConfigureAwait(false);
        return text;
    }

    public static async Task<string> LatestReleaseAsync(Paths paths, CancellationToken ct)
    {
        var text = await GetManifestTextAsync(paths, false, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("latest", out var latest)
               && latest.TryGetProperty("release", out var release)
            ? release.GetString() ?? ""
            : "";
    }

    // ---------------------------------------------------------------- 加载器

    public static async Task<List<string>> GetFabricLoadersAsync(string gameVersion, CancellationToken ct)
    {
        using var doc = await Net.GetJsonAsync($"{FabricMeta}/versions/loader/{gameVersion}", ct).ConfigureAwait(false);
        var result = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("loader", out var loader) && loader.TryGetProperty("version", out var version))
            {
                result.Add(version.GetString() ?? "");
            }
        }
        return result.Where(v => v.Length > 0).ToList();
    }

    public static async Task<string> GetLatestFabricLoaderAsync(CancellationToken ct)
    {
        using var doc = await Net.GetJsonAsync($"{FabricMeta}/versions/loader", ct).ConfigureAwait(false);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("stable", out var stable) && stable.ValueKind == JsonValueKind.True
                && item.TryGetProperty("version", out var version))
            {
                return version.GetString() ?? "";
            }
        }
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
    }

    public static async Task<List<string>> GetQuiltLoadersAsync(string gameVersion, CancellationToken ct)
    {
        using var doc = await Net.GetJsonAsync($"{QuiltMeta}/versions/loader/{gameVersion}", ct).ConfigureAwait(false);
        var result = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("loader", out var loader) && loader.TryGetProperty("version", out var version))
            {
                result.Add(version.GetString() ?? "");
            }
        }
        return result.Where(v => v.Length > 0).ToList();
    }

    public static async Task<string> InstallLoaderProfileAsync(Paths paths, string gameVersion, string loader, string loaderVersion, CancellationToken ct)
    {
        string url;
        string fallbackId;
        if (loader == "fabric")
        {
            url = $"{FabricMeta}/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            fallbackId = $"fabric-loader-{loaderVersion}-{gameVersion}";
        }
        else if (loader == "quilt")
        {
            url = $"{QuiltMeta}/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            fallbackId = $"quilt-loader-{loaderVersion}-{gameVersion}";
        }
        else
        {
            return gameVersion;
        }

        var text = await Net.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("libraries", out _))
        {
            throw new LauncherException($"{loader} 安装档案格式异常");
        }
        // 加载器档案 inheritsFrom 指向原版；父版本 JSON 不在本地时先补，否则后续无法解析
        if (!File.Exists(paths.VersionJson(gameVersion)))
        {
            await FetchVersionJsonTextAsync(paths, gameVersion, null, ct).ConfigureAwait(false);
        }
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        id ??= fallbackId;
        Directory.CreateDirectory(paths.VersionDir(id!));
        await File.WriteAllTextAsync(paths.VersionJson(id!), text, ct).ConfigureAwait(false);
        return id!;
    }
}

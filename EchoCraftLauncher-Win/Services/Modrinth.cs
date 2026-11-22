using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;

namespace SwiftCraftLauncher2.Services;

public sealed class ModHit : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public string ProjectId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public long Downloads { get; set; }
    public string Author { get; set; } = "";
    public string Categories { get; set; } = "";
    public string GameVersions { get; set; } = "";

    /// <summary>图标，异步下载完成后通知界面刷新（列表用的是虚拟化容器，不能靠整体刷新）。</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>模组工程的详细信息，用于应用内的详情页。</summary>
public sealed class ModDetail
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public long Downloads { get; set; }
    public long Followers { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public List<string> GameVersions { get; set; } = new();
    public List<string> Gallery { get; set; } = new();
    public string License { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string IssuesUrl { get; set; } = "";
    public string Updated { get; set; } = "";
}

public sealed class ModFile
{
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>Modrinth 公开 API：搜索模组、挑选兼容文件、连同必需前置一起安装。</summary>
public static class Modrinth
{
    private const string Base = "https://api.modrinth.com/v2";

    public static async Task<List<ModHit>> SearchAsync(
        string query, string loader, string gameVersion, string category, int limit, int offset, CancellationToken ct)
    {
        var facets = new List<string> { "[\"project_type:mod\"]" };
        if (loader.Length > 0)
        {
            facets.Add($"[\"categories:{loader}\"]");
        }
        if (gameVersion.Length > 0)
        {
            facets.Add($"[\"versions:{gameVersion}\"]");
        }
        if (category.Length > 0)
        {
            facets.Add($"[\"categories:{category}\"]");
        }
        var facetsJson = "[" + string.Join(',', facets) + "]";
        // 空关键词时按下载量排序，用户才会看到一个“热门模组”列表而不是空面板
        var index = query.Length == 0 ? "downloads" : "relevance";
        var url = $"{Base}/search?query={Uri.EscapeDataString(query)}&limit={limit}&index={index}&offset={offset}"
                  + $"&facets={Uri.EscapeDataString(facetsJson)}";

        using var doc = await Net.GetJsonAsync(url, ct).ConfigureAwait(false);
        var list = new List<ModHit>();
        if (!doc.RootElement.TryGetProperty("hits", out var hits))
        {
            return list;
        }
        foreach (var hit in hits.EnumerateArray())
        {
            var versions = new List<string>();
            if (hit.TryGetProperty("versions", out var versionArray) && versionArray.ValueKind == JsonValueKind.Array)
            {
                versions.AddRange(versionArray.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0));
            }
            list.Add(new ModHit
            {
                ProjectId = Str(hit, "project_id"),
                Slug = Str(hit, "slug"),
                Title = Str(hit, "title"),
                Description = Str(hit, "description"),
                IconUrl = Str(hit, "icon_url"),
                Downloads = hit.TryGetProperty("downloads", out var d) && d.TryGetInt64(out var downloads) ? downloads : 0,
                Author = Str(hit, "author"),
                Categories = string.Join(" / ", StrArray(hit, "display_categories")),
                GameVersions = string.Join(" ", versions.TakeLast(4))
            });
        }
        return list;
    }

    private static IEnumerable<string> StrArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0)
            : Enumerable.Empty<string>();

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>拉取工程详情（含完整介绍正文、截图、分类、支持的加载器/版本）。</summary>
    public static async Task<ModDetail> GetProjectAsync(string projectId, CancellationToken ct)
    {
        using var doc = await Net.GetJsonAsync($"{Base}/project/{projectId}", ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var detail = new ModDetail
        {
            Id = Str(root, "id"),
            Title = Str(root, "title"),
            Description = Str(root, "description"),
            Body = Str(root, "body"),
            IconUrl = Str(root, "icon_url"),
            Downloads = root.TryGetProperty("downloads", out var d) && d.TryGetInt64(out var dl) ? dl : 0,
            Followers = root.TryGetProperty("followers", out var f) && f.TryGetInt64(out var fol) ? fol : 0,
            SourceUrl = Str(root, "source_url"),
            IssuesUrl = Str(root, "issues_url"),
            Updated = Str(root, "updated"),
            Categories = StrArray(root, "categories").ToList(),
            Loaders = StrArray(root, "loaders").ToList(),
            GameVersions = StrArray(root, "game_versions").ToList()
        };
        if (root.TryGetProperty("license", out var license) && license.ValueKind == JsonValueKind.Object)
        {
            detail.License = Str(license, "name");
        }
        if (root.TryGetProperty("gallery", out var gallery) && gallery.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in gallery.EnumerateArray())
            {
                var url = Str(item, "url");
                if (url.Length > 0)
                {
                    detail.Gallery.Add(url);
                }
            }
        }
        return detail;
    }

    /// <summary>拉取某个工程对指定加载器/游戏版本的版本列表。</summary>
    public static async Task<List<(string VersionId, string Name, ModFile? File, List<string> RequiredIds)>> GetVersionsAsync(
        string projectId, string loader, string gameVersion, CancellationToken ct)
    {
        var url = $"{Base}/project/{projectId}/version";
        var query = new List<string>();
        if (loader.Length > 0)
        {
            query.Add("loaders=[\"" + loader + "\"]");
        }
        if (gameVersion.Length > 0)
        {
            query.Add("game_versions=[\"" + gameVersion + "\"]");
        }
        if (query.Count > 0)
        {
            url += "?" + string.Join('&', query.Select(q => q.Split('=')[0] + "=" + Uri.EscapeDataString(q[(q.IndexOf('=') + 1)..])));
        }

        using var doc = await Net.GetJsonAsync(url, ct, 3).ConfigureAwait(false);
        var list = new List<(string, string, ModFile?, List<string>)>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var versionId = Str(item, "id");
            var name = Str(item, "name");
            var file = PickFile(item);
            var required = new List<string>();
            if (item.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    if (Str(dep, "dependency_type") != "required")
                    {
                        continue;
                    }
                    var depVersion = Str(dep, "version_id");
                    if (depVersion.Length > 0)
                    {
                        required.Add(depVersion);
                    }
                }
            }
            list.Add((versionId, name.Length > 0 ? name : versionId, file, required));
        }
        return list;
    }

    private static ModFile? PickFile(JsonElement version)
    {
        if (!version.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        JsonElement? primary = null;
        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("primary", out var isPrimary) && isPrimary.ValueKind == JsonValueKind.True)
            {
                primary = file;
                break;
            }
        }
        primary ??= files.GetArrayLength() > 0 ? files[0] : null;
        if (primary is not JsonElement chosen)
        {
            return null;
        }
        return new ModFile
        {
            Url = Str(chosen, "url"),
            FileName = Str(chosen, "filename"),
            Sha1 = chosen.TryGetProperty("hashes", out var hashes) ? Str(hashes, "sha1") : "",
            Size = chosen.TryGetProperty("size", out var size) && size.TryGetInt64(out var parsed) ? parsed : 0
        };
    }

    /// <summary>把一个工程安装到实例的 mods 目录，连同必需前置。</summary>
    public static async Task<List<string>> InstallAsync(
        string projectId, string instanceDir, string loader, string gameVersion,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var versions = await GetVersionsAsync(projectId, loader, gameVersion, ct).ConfigureAwait(false);
        if (versions.Count == 0)
        {
            throw new LauncherException(
                $"这个模组没有适配当前实例（{gameVersion} / {(loader.Length == 0 ? "原版" : loader)}）的版本。\n" +
                "可以换一个实例，或先在「下载」里创建一个 Fabric / Quilt 实例。");
        }

        var modsDir = Path.Combine(instanceDir, "mods");
        Directory.CreateDirectory(modsDir);

        var installed = new List<string>();
        var queue = new Queue<(string VersionId, ModFile? File, List<string> Required)>();
        queue.Enqueue((versions[0].VersionId, versions[0].File, versions[0].RequiredIds));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fetchedRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var total = Math.Max(1, 4);
        var done = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (versionId, file, required) = queue.Dequeue();
            if (versionId.Length == 0 || !seen.Add(versionId))
            {
                continue;
            }

            // 部分依赖只给了 version_id，需要再查一次文件与它自己的前置
            if (file is null || file.Url.Length == 0)
            {
                var detail = await FetchVersionDetailAsync(versionId, ct).ConfigureAwait(false);
                file = detail.File;
                required = detail.Required;
            }
            if (file is null || file.Url.Length == 0 || file.FileName.Length == 0)
            {
                continue;
            }

            var target = Path.Combine(modsDir, file.FileName);
            progress?.Report(new InstallProgress
            {
                Stage = "模组",
                Detail = file.FileName,
                Fraction = (double)done / total,
                Bytes = file.Size
            });

            var needDownload = !File.Exists(target) || (file.Size > 0 && new FileInfo(target).Length != file.Size);
            if (needDownload)
            {
                await Net.DownloadAsync(file.Url, target,
                    file.Sha1.Length > 0 ? file.Sha1 : null,
                    file.Size > 0 ? file.Size : null, null, ct).ConfigureAwait(false);
            }
            installed.Add(file.FileName);
            done++;
            total = Math.Max(total, done + queue.Count);

            foreach (var requiredId in required)
            {
                if (fetchedRequired.Add(requiredId))
                {
                    queue.Enqueue((requiredId, null, new List<string>()));
                }
            }
            progress?.Report(new InstallProgress
            {
                Stage = "模组",
                Detail = $"已安装 {installed.Count} 个文件",
                Fraction = Math.Clamp((double)done / total, 0, 1),
                Bytes = file.Size
            });
        }

        if (installed.Count == 0)
        {
            throw new LauncherException("没有下载到任何模组文件，可能是接口异常或网络问题。");
        }
        return installed;
    }

    private static async Task<(ModFile? File, List<string> Required)> FetchVersionDetailAsync(
        string versionId, CancellationToken ct)
    {
        using var doc = await Net.GetJsonAsync($"{Base}/version/{versionId}", ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var required = new List<string>();
        if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var dep in deps.EnumerateArray())
            {
                if (Str(dep, "dependency_type") == "required")
                {
                    var id = Str(dep, "version_id");
                    if (id.Length > 0)
                    {
                        required.Add(id);
                    }
                }
            }
        }
        return (PickFile(root), required);
    }
}

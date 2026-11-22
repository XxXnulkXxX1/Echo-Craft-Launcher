using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

public sealed class InstanceInfo
{
    public string Name { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string Loader { get; set; } = "";          // "" | fabric | quilt
    public string LoaderVersion { get; set; } = "";
    public string Created { get; set; } = "";
    public string Description { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Directory { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool VersionMissing { get; set; }

    public string LoaderLabel => Loader.Length == 0 ? "原版" : Loader == "fabric" ? "Fabric" : "Quilt";

    [System.Text.Json.Serialization.JsonIgnore]
    public string SubTitle => $"{VersionId} · {LoaderLabel}";
}

/// <summary>实例（独立游戏目录）管理。</summary>
public static class InstanceStore
{
    public static List<InstanceInfo> LoadAll(Paths paths, IReadOnlyCollection<string> installedVersions)
    {
        var result = new List<InstanceInfo>();
        if (!Directory.Exists(paths.Instances))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(paths.Instances))
        {
            var meta = Path.Combine(dir, "instance.json");
            InstanceInfo? info = null;
            if (File.Exists(meta))
            {
                try
                {
                    info = JsonSerializer.Deserialize<InstanceInfo>(File.ReadAllText(meta));
                }
                catch (JsonException)
                {
                    info = null;
                }
            }
            info ??= new InstanceInfo { Name = Path.GetFileName(dir), VersionId = Path.GetFileName(dir) };
            info.Directory = dir;
            if (info.Name.Length == 0)
            {
                info.Name = Path.GetFileName(dir);
            }
            if (info.VersionId.Length == 0)
            {
                info.VersionId = Path.GetFileName(dir);
            }
            // 版本文件缺失只作为运行时标记，不写入 instance.json 污染数据
            if (installedVersions.Count > 0 && !installedVersions.Contains(info.VersionId))
            {
                info.VersionMissing = true;
            }
            result.Add(info);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "instance" : cleaned;
    }

    public static InstanceInfo Create(Paths paths, string name, string versionId, string loader, string loaderVersion, string description)
    {
        var folder = Path.Combine(paths.Instances, Sanitize(name));
        if (Directory.Exists(folder))
        {
            var suffix = 2;
            while (Directory.Exists(folder + suffix))
            {
                suffix++;
            }
            folder += suffix;
            name = Path.GetFileName(folder);
        }
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "mods"));
        Directory.CreateDirectory(Path.Combine(folder, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(folder, "saves"));

        var info = new InstanceInfo
        {
            Name = name,
            VersionId = versionId,
            Loader = loader,
            LoaderVersion = loaderVersion,
            Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Description = description,
            Directory = folder
        };
        Save(info);
        return info;
    }

    public static void Save(InstanceInfo info)
    {
        if (string.IsNullOrEmpty(info.Directory))
        {
            return;
        }
        Directory.CreateDirectory(info.Directory);
        var payload = new
        {
            name = info.Name,
            versionId = info.VersionId,
            loader = info.Loader,
            loaderVersion = info.LoaderVersion,
            created = info.Created,
            description = info.Description
        };
        File.WriteAllText(Path.Combine(info.Directory, "instance.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(InstanceInfo info)
    {
        try
        {
            if (Directory.Exists(info.Directory))
            {
                Directory.Delete(info.Directory, true);
            }
        }
        catch (IOException)
        {
            // 文件被占用，忽略
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足，忽略
        }
    }
}

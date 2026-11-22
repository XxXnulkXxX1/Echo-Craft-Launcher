using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwiftCraftLauncher2.Services;

public sealed class NativeRef
{
    public string Classifier { get; init; } = "";
    public string Path { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public long Size { get; init; }
}

/// <summary>合并 inheritsFrom 之后的版本信息。</summary>
public sealed class ResolvedVersion
{
    public string Id { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Type { get; set; } = "release";
    public string MainClass { get; set; } = "";
    public string JarId { get; set; } = "";
    public int JavaMajor { get; set; }
    public string MinecraftArguments { get; set; } = "";
    public List<JsonElement> Libraries { get; } = new();
    public List<JsonElement> GameArgs { get; } = new();
    public List<JsonElement> JvmArgs { get; } = new();
    public JsonElement? AssetIndex { get; set; }
    public JsonElement? Logging { get; set; }
    public List<JsonElement> Chain { get; } = new();

    public string AssetIndexId =>
        AssetIndex is { ValueKind: JsonValueKind.Object } index && index.TryGetProperty("id", out var id)
            ? id.GetString() ?? ""
            : "";
}

/// <summary>版本 JSON 解析：继承合并、规则判断、依赖路径、参数收集。</summary>
public static class VersionResolver
{
    public const string LibraryBase = "https://libraries.minecraft.net/";
    public const string ResourcesBase = "https://resources.download.minecraft.net/";

    public static string OsName =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    public static string OsArch
    {
        get
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            if (arch == System.Runtime.InteropServices.Architecture.Arm64)
            {
                return "arm64";
            }
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }
    }

    // ---------------------------------------------------------------- 规则

    public static bool RuleAllows(JsonElement? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is not { ValueKind: JsonValueKind.Array } array)
        {
            return true;
        }
        var allowed = false;
        foreach (var rule in array.EnumerateArray())
        {
            if (RuleMatches(rule, features))
            {
                allowed = Str(rule, "action") == "allow";
            }
        }
        return allowed;
    }

    private static bool RuleMatches(JsonElement rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
        {
            var name = Str(os, "name");
            if (name.Length > 0 && name != OsName)
            {
                return false;
            }
            var arch = Str(os, "arch");
            if (arch.Length > 0 && arch != OsArch)
            {
                return false;
            }
            var version = Str(os, "version");
            if (version.Length > 0)
            {
                try
                {
                    if (!Regex.IsMatch(Environment.OSVersion.Version.ToString(), version))
                    {
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }
        if (rule.TryGetProperty("features", out var featureElement) && featureElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var feature in featureElement.EnumerateObject())
            {
                var expected = feature.Value.ValueKind == JsonValueKind.True;
                var actual = features is not null && features.TryGetValue(feature.Name, out var value) && value;
                if (actual != expected)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- Maven

    public static string MavenPath(string name)
    {
        var ext = "jar";
        var at = name.IndexOf('@');
        if (at > 0)
        {
            ext = name[(at + 1)..];
            name = name[..at];
        }
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return name;
        }
        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? parts[3] : "";
        var file = $"{artifact}-{version}" + (classifier.Length > 0 ? $"-{classifier}" : "") + $".{ext}";
        return string.Join('/', group.Split('.').Append(artifact).Append(version).Append(file));
    }

    public static string LibraryPath(JsonElement lib, Paths paths)
    {
        var relative = "";
        if (lib.TryGetProperty("downloads", out var downloads)
            && downloads.ValueKind == JsonValueKind.Object
            && downloads.TryGetProperty("artifact", out var artifact)
            && artifact.ValueKind == JsonValueKind.Object)
        {
            relative = Str(artifact, "path");
        }
        if (relative.Length == 0)
        {
            relative = MavenPath(Str(lib, "name"));
        }
        return Path.Combine(paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string LibraryUrl(JsonElement lib)
    {
        if (lib.TryGetProperty("downloads", out var downloads)
            && downloads.ValueKind == JsonValueKind.Object
            && downloads.TryGetProperty("artifact", out var artifact)
            && artifact.ValueKind == JsonValueKind.Object
            && Str(artifact, "url").Length > 0)
        {
            return Str(artifact, "url");
        }
        var baseUrl = Str(lib, "url");
        if (baseUrl.Length == 0)
        {
            baseUrl = LibraryBase;
        }
        return baseUrl.TrimEnd('/') + "/" + MavenPath(Str(lib, "name"));
    }

    public static IEnumerable<NativeRef> Natives(JsonElement lib)
    {
        if (!lib.TryGetProperty("natives", out var natives) || natives.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        var key = OsName;
        if (!natives.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            yield break;
        }
        var classifier = (value.GetString() ?? "").Replace("${arch}", OsArch == "x86" ? "32" : "64");
        if (classifier.Length == 0)
        {
            yield break;
        }

        var name = Str(lib, "name");
        string path;
        string url;
        string sha1 = "";
        long size = 0;

        if (lib.TryGetProperty("downloads", out var downloads)
            && downloads.ValueKind == JsonValueKind.Object
            && downloads.TryGetProperty("classifiers", out var classifiers)
            && classifiers.ValueKind == JsonValueKind.Object
            && classifiers.TryGetProperty(classifier, out var artifact)
            && artifact.ValueKind == JsonValueKind.Object)
        {
            path = Str(artifact, "path");
            url = Str(artifact, "url");
            sha1 = Str(artifact, "sha1");
            size = artifact.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed) ? parsed : 0;
        }
        else
        {
            path = MavenPath(name + ":" + classifier);
            var baseUrl = Str(lib, "url");
            if (baseUrl.Length == 0)
            {
                baseUrl = LibraryBase;
            }
            url = baseUrl.TrimEnd('/') + "/" + path;
        }

        if (path.Length == 0)
        {
            path = MavenPath(name + ":" + classifier);
        }
        yield return new NativeRef { Classifier = classifier, Path = path, Url = url, Sha1 = sha1, Size = size };
    }

    // ---------------------------------------------------------------- 合并

    public static ResolvedVersion Resolve(string versionId, Paths paths)
    {
        var chain = new List<JsonElement>();
        var current = versionId;
        for (var depth = 0; depth < 12; depth++)
        {
            var file = paths.VersionJson(current);
            if (!File.Exists(file))
            {
                throw new LauncherException($"找不到版本 {current} 的 JSON：{file}");
            }
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new LauncherException($"版本 JSON 解析失败：{file}", ex);
            }
            chain.Add(root);
            var parent = Str(root, "inheritsFrom");
            if (parent.Length == 0 || parent == current)
            {
                break;
            }
            current = parent;
        }

        if (chain.Count == 0)
        {
            throw new LauncherException($"无法解析版本 {versionId}");
        }

        var result = new ResolvedVersion { Id = versionId };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (data, index) in chain.Select((value, index) => (value, index)))
        {
            if (data.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
            {
                foreach (var lib in libraries.EnumerateArray())
                {
                    var name = Str(lib, "name");
                    if (name.Length == 0 || !seen.Add(name))
                    {
                        continue;
                    }
                    result.Libraries.Add(lib.Clone());
                }
            }

            if (data.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
            {
                AppendArguments(arguments, "game", result.GameArgs);
                AppendArguments(arguments, "jvm", result.JvmArgs);
            }

            if (result.MainClass.Length == 0)
            {
                result.MainClass = Str(data, "mainClass");
            }
            if (result.AssetIndex is null && data.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object)
            {
                result.AssetIndex = assetIndex.Clone();
            }
            if (result.JavaMajor <= 0 && data.TryGetProperty("javaVersion", out var javaVersion)
                && javaVersion.ValueKind == JsonValueKind.Object
                && javaVersion.TryGetProperty("majorVersion", out var major)
                && major.TryGetInt32(out var majorValue))
            {
                result.JavaMajor = majorValue;
            }
            if (result.JarId.Length == 0
                && data.TryGetProperty("downloads", out var downloads)
                && downloads.ValueKind == JsonValueKind.Object
                && downloads.TryGetProperty("client", out _))
            {
                result.JarId = Str(data, "id");
                if (result.JarId.Length == 0)
                {
                    result.JarId = versionId;
                }
            }
            if (result.Logging is null && data.TryGetProperty("logging", out var logging) && logging.ValueKind == JsonValueKind.Object)
            {
                result.Logging = logging.Clone();
            }
            if (result.MinecraftArguments.Length == 0)
            {
                result.MinecraftArguments = Str(data, "minecraftArguments");
            }
            if (index == 0)
            {
                result.Type = Str(data, "type");
                if (result.Type.Length == 0)
                {
                    result.Type = "release";
                }
                result.DisplayId = Str(data, "id");
            }
            result.Chain.Add(data);
        }

        if (result.JarId.Length == 0)
        {
            result.JarId = Str(chain[^1], "id");
            if (result.JarId.Length == 0)
            {
                result.JarId = versionId;
            }
        }
        if (result.DisplayId.Length == 0)
        {
            result.DisplayId = versionId;
        }
        if (result.JavaMajor <= 0)
        {
            result.JavaMajor = 8;
        }
        if (result.MainClass.Length == 0)
        {
            throw new LauncherException("版本 JSON 缺少 mainClass，文件可能不完整");
        }
        return result;
    }

    private static void AppendArguments(JsonElement arguments, string key, List<JsonElement> target)
    {
        if (arguments.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                target.Add(item.Clone());
            }
        }
    }

    // ---------------------------------------------------------------- 启动参数

    public static List<string> CollectArguments(IEnumerable<JsonElement> items, IReadOnlyDictionary<string, bool>? features)
    {
        var result = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString() ?? "");
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                JsonElement? rules = item.TryGetProperty("rules", out var ruleElement) ? ruleElement : null;
                if (!RuleAllows(rules, features))
                {
                    continue;
                }
                if (!item.TryGetProperty("value", out var value))
                {
                    continue;
                }
                if (value.ValueKind == JsonValueKind.String)
                {
                    result.Add(value.GetString() ?? "");
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    result.AddRange(value.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0));
                }
            }
        }
        return result;
    }

    public static List<string> GameArguments(ResolvedVersion version, IReadOnlyDictionary<string, bool>? features)
    {
        var args = CollectArguments(version.GameArgs, features);
        if (args.Count == 0 && version.MinecraftArguments.Length > 0)
        {
            args = version.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return args;
    }

    public static List<string> JvmArguments(ResolvedVersion version, IReadOnlyDictionary<string, bool>? features)
    {
        var args = CollectArguments(version.JvmArgs, features);
        if (args.Count == 0)
        {
            args = new List<string> { "-Djava.library.path=${natives_directory}", "-cp", "${classpath}" };
        }
        if (!args.Contains("-cp") && !args.Contains("-classpath"))
        {
            args.InsertRange(0, new[] { "-cp", "${classpath}" });
        }
        return args;
    }

    public static List<string> Classpath(ResolvedVersion version, Paths paths, IReadOnlyDictionary<string, bool>? features)
    {
        var result = new List<string>();
        foreach (var lib in version.Libraries)
        {
            JsonElement? rules = lib.TryGetProperty("rules", out var ruleElement) ? ruleElement : null;
            if (!RuleAllows(rules, features))
            {
                continue;
            }
            var path = LibraryPath(lib, paths);
            if (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                result.Add(path);
            }
        }
        var jar = paths.VersionJar(version.JarId);
        if (File.Exists(jar))
        {
            result.Add(jar);
        }
        return result;
    }

    private static readonly Regex PlaceholderPattern = new(@"\$\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public static string ReplacePlaceholders(string token, IReadOnlyDictionary<string, string> context) =>
        PlaceholderPattern.Replace(token, match => context.TryGetValue(match.Groups[1].Value, out var value) ? value : "");

    public static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

/// <summary>
/// 模组介绍的英文→中文翻译。
/// 说明：
/// 1. Modrinth 不提供本地化文本，这里用公开的 MyMemory 接口做机翻，属于尽力而为的增强；
/// 2. 接口对单次请求长度有限制，所以按段落/句子切成 ~450 字符的小块提交；
/// 3. 翻译结果按原文哈希缓存，同一模组反复打开不会重复请求。
/// </summary>
public static class Translator
{
    private const int MaxChunk = 450;
    private const int MaxTotal = 12000; // 太长的介绍只翻前面部分，避免耗时过久

    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly SemaphoreSlim Gate = new(2, 2);

    /// <summary>判断文本是否以英文为主（ASCII 字母占比超过 35%）。</summary>
    public static bool NeedsTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        int letters = 0;
        int cjk = 0;
        foreach (var ch in text)
        {
            if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z')
            {
                letters++;
            }
            else if (ch >= 0x4E00 && ch <= 0x9FFF)
            {
                cjk++;
            }
        }
        return letters > cjk * 8 && letters > 80;
    }

    /// <summary>把整段 Markdown 文本翻成中文；失败的部分保留原文。</summary>
    public static async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        if (!NeedsTranslation(text))
        {
            return text;
        }

        var head = text.Length > MaxTotal ? text[..MaxTotal] : text;
        var chunks = Split(head);
        var results = new string[chunks.Count];

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            // 纯符号/纯代码块片段不必翻译
            if (!NeedsTranslation(chunk) && chunk.Any(char.IsLetter) == false)
            {
                results[i] = chunk;
                continue;
            }
            results[i] = await TranslateChunkAsync(chunk, ct);
        }

        var builder = new StringBuilder();
        foreach (var piece in results)
        {
            builder.Append(piece);
        }
        if (text.Length > MaxTotal)
        {
            builder.Append(text[MaxTotal..]);
        }
        return builder.ToString();
    }

    /// <summary>按空行优先、其次句号切分，尽量不破坏 Markdown 结构。</summary>
    private static List<string> Split(string text)
    {
        var chunks = new List<string>();
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length > 0)
            {
                chunks.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        foreach (var paragraph in text.Split('\n'))
        {
            var line = paragraph + "\n";
            if (buffer.Length + line.Length > MaxChunk)
            {
                Flush();
            }
            // 单行过长时再按句号切
            if (line.Length > MaxChunk)
            {
                foreach (var sentence in SplitLongLine(line))
                {
                    if (buffer.Length + sentence.Length > MaxChunk)
                    {
                        Flush();
                    }
                    buffer.Append(sentence);
                }
            }
            else
            {
                buffer.Append(line);
            }
        }
        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitLongLine(string line)
    {
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is '.' or '!' or '?' or '。' && i + 1 - start >= 80)
            {
                yield return line[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < line.Length)
        {
            yield return line[start..];
        }
    }

    /// <summary>排查日志：临时写到临时目录，便于无界面核对翻译链路。</summary>
    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "echo_translate.log"),
                $"{DateTime.Now:HH:mm:ss} [translator] {line}\n");
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>翻译单个片段：优先 MyMemory（质量较好），限流或失败时退到有道演示接口。</summary>
    private static async Task<string> TranslateChunkAsync(string chunk, CancellationToken ct)
    {
        var key = chunk;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        // 没有英文字母的片段直接返回（比如表格线、空白）
        if (chunk.Any(char.IsLetter) == false)
        {
            return chunk;
        }

        await Gate.WaitAsync(ct);
        try
        {
            var text = chunk.Trim();
            var translated = "";
            try
            {
                var url = "https://api.mymemory.translated.net/get?q=" +
                          Uri.EscapeDataString(text) + "&langpair=en|zh-CN";
                using var doc = await Net.GetJsonAsync(url, ct, retries: 1);
                var root = doc.RootElement;
                translated = root.TryGetProperty("responseData", out var data) &&
                             data.TryGetProperty("translatedText", out var value)
                    ? value.GetString() ?? ""
                    : "";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"mymemory 不可用（{ex.Message[..Math.Min(60, ex.Message.Length)]}），改用有道");
            }

            if (translated.Length == 0 || translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            {
                using var doc2 = await Net.PostFormAsync("https://aidemo.youdao.com/trans", new Dictionary<string, string>
                {
                    ["q"] = text,
                    ["from"] = "en",
                    ["to"] = "zh-CHS",
                }, ct);
                if (doc2.RootElement.TryGetProperty("translation", out var arr) && arr.ValueKind == JsonValueKind.Array &&
                    arr.GetArrayLength() > 0)
                {
                    translated = arr[0].GetString() ?? "";
                }
            }

            if (translated.Length == 0)
            {
                return chunk;
            }
            // 保留原有结尾换行，避免 Markdown 段落被压平
            var result = translated + (chunk.EndsWith('\n') ? "\n" : "");
            Cache[key] = result;
            Log($"ok {chunk.Length}->{translated.Length} {translated[..Math.Min(20, translated.Length)]}");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"fail: {ex.GetType().Name} {ex.Message}");
            return chunk;
        }
        finally
        {
            Gate.Release();
        }
    }
}

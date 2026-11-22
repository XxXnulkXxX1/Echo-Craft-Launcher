using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SwiftCraftLauncher2.Services;

/// <summary>
/// 把 Modrinth 的 Markdown 介绍渲染成 FlowDocument。
/// 只做标题、段落、列表、粗体、行内代码与链接这些常见语法，避免引入第三方 Markdown 包。
/// </summary>
public static class MarkdownDoc
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex ImageSyntax = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);

    public static FlowDocument Build(string markdown, Brush textBrush, Brush subBrush, Color accent)
    {
        var doc = new FlowDocument
        {
            Background = Brushes.Transparent,
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            FontSize = 13,
            Foreground = textBrush
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            doc.Blocks.Add(new Paragraph(new Run("这个模组没有提供详细介绍。")) { Foreground = subBrush });
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var bullet = new List<Paragraph>();
        var inCode = false;
        var code = new List<string>();

        void FlushBullets()
        {
            foreach (var p in bullet)
            {
                doc.Blocks.Add(p);
            }
            bullet.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            // 代码块：整段用等宽字体展示，避免出现空白块
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode)
                {
                    FlushBullets();
                    var codeText = string.Join("\n", code);
                    doc.Blocks.Add(new Paragraph(new Run(codeText))
                    {
                        FontFamily = new FontFamily("Consolas, Cascadia Mono"),
                        FontSize = 12,
                        Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)),
                        Foreground = subBrush,
                        Margin = new Thickness(0, 4, 0, 6)
                    });
                    code.Clear();
                }
                inCode = !inCode;
                continue;
            }
            if (inCode)
            {
                code.Add(raw);
                continue;
            }

            if (line.Length == 0)
            {
                FlushBullets();
                continue;
            }

            // 图片语法交给上面的截图区域展示
            line = ImageSyntax.Replace(line, "").Trim();
            line = HtmlTag.Replace(line, "").Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var heading = Regex.Match(line, @"^(#{1,4})\s+(.*)$");
            if (heading.Success)
            {
                FlushBullets();
                var level = heading.Groups[1].Value.Length;
                doc.Blocks.Add(new Paragraph(Inline(heading.Groups[2].Value, textBrush, accent))
                {
                    FontSize = level switch { 1 => 19, 2 => 16, 3 => 14, _ => 13 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 4 : 12, 0, 4)
                });
                continue;
            }

            var item = Regex.Match(line, @"^(\s*)(?:[-*+]|\d+\.)\s+(.*)$");
            if (item.Success)
            {
                var indent = Math.Min(item.Groups[1].Value.Length / 2, 2);
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(14 + indent * 14, 0, 0, 3),
                    TextIndent = -12
                };
                paragraph.Inlines.Add(new Run("•  ") { Foreground = subBrush });
                paragraph.Inlines.Add(Inline(item.Groups[2].Value, textBrush, accent));
                bullet.Add(paragraph);
                continue;
            }

            FlushBullets();
            doc.Blocks.Add(new Paragraph(Inline(line, textBrush, accent)) { Margin = new Thickness(0, 0, 0, 7) });
        }

        FlushBullets();
        return doc;
    }

    /// <summary>处理 **粗体**、`代码`、[链接](url) 三种行内语法。</summary>
    private static Inline Inline(string source, Brush textBrush, Color accent)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var span = new Span();
        paragraph.Inlines.Add(span);

        var index = 0;
        while (index < source.Length)
        {
            if (index + 1 < source.Length && source[index] == '*' && source[index + 1] == '*')
            {
                var end = source.IndexOf("**", index + 2, StringComparison.Ordinal);
                if (end > index)
                {
                    span.Inlines.Add(new Bold(new Run(source[(index + 2)..end])));
                    index = end + 2;
                    continue;
                }
            }
            if (source[index] == '`')
            {
                var end = source.IndexOf('`', index + 1);
                if (end > index)
                {
                    span.Inlines.Add(new Run(source[(index + 1)..end])
                    {
                        FontFamily = new FontFamily("Consolas, Cascadia Mono"),
                        Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80))
                    });
                    index = end + 1;
                    continue;
                }
            }
            if (source[index] == '[')
            {
                var close = source.IndexOf(']', index + 1);
                if (close > index && close + 1 < source.Length && source[close + 1] == '(')
                {
                    var paren = source.IndexOf(')', close + 2);
                    if (paren > close)
                    {
                        var label = source[(index + 1)..close];
                        span.Inlines.Add(new Run(label)
                        {
                            Foreground = new SolidColorBrush(accent),
                            TextDecorations = TextDecorations.Underline
                        });
                        index = paren + 1;
                        continue;
                    }
                }
            }
            span.Inlines.Add(new Run(source[index].ToString()));
            index++;
        }
        // 外层 Paragraph 只是为了复用 Inline 构造逻辑，这里取出唯一的 Span
        paragraph.Inlines.Clear();
        return span;
    }
}

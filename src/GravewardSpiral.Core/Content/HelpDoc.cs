namespace GravewardSpiral.Core.Content;

/// <summary>ヘルプの1つのかたまり。見出し(# と ##)、段落、箇条書きの1項目。</summary>
public enum HelpBlockKind { Topic, Heading, Paragraph, Bullet }

public sealed record HelpBlock(HelpBlockKind Kind, string Text);

/// <summary>
/// ゲームの中のヘルプ。<c>Content/help/&lt;言語&gt;/*.md</c> を名前の順に読み、1つの長い文書にする。
/// 読める md は小さく、<c># 題</c>(ファイルごとの話題)、<c>## 小見出し</c>、段落(空の行で区切る)、<c>- 箇条書き</c> だけ。
/// <c>**太字**</c> の印は外して読む。目次は # と ## の見出し。
/// </summary>
public sealed class HelpDoc
{
    public IReadOnlyList<HelpBlock> Blocks { get; }

    public HelpDoc(IReadOnlyList<HelpBlock> blocks) => Blocks = blocks;

    /// <summary>目次にする見出しの、Blocks の中の位置。</summary>
    public IEnumerable<int> TocIndexes() =>
        Blocks.Select((b, i) => (b, i)).Where(x => x.b.Kind is HelpBlockKind.Topic or HelpBlockKind.Heading).Select(x => x.i);

    /// <summary>言語のフォルダを読む。無ければ英語、それも無ければ日本語。どれも無ければ空。</summary>
    public static HelpDoc Load(string helpRoot, string language)
    {
        foreach (var lang in new[] { language, "en", "ja" })
        {
            var dir = Path.Combine(helpRoot, lang);
            if (!Directory.Exists(dir)) continue;
            var files = Directory.GetFiles(dir, "*.md").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
            return new HelpDoc(files.SelectMany(f => Parse(File.ReadAllText(f))).ToList());
        }
        return new HelpDoc([]);
    }

    public static List<HelpBlock> Parse(string markdown)
    {
        var blocks = new List<HelpBlock>();
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count > 0) blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, Join(paragraph)));
            paragraph.Clear();
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("## ")) { Flush(); blocks.Add(new HelpBlock(HelpBlockKind.Heading, Plain(line[3..]))); continue; }
            if (line.StartsWith("# ")) { Flush(); blocks.Add(new HelpBlock(HelpBlockKind.Topic, Plain(line[2..]))); continue; }
            if (line.StartsWith("- ") || line.StartsWith("* ")) { Flush(); blocks.Add(new HelpBlock(HelpBlockKind.Bullet, Plain(line[2..]))); continue; }
            // 箇条書きの続きの行(字下げした行)は、その項目に足す
            if (paragraph.Count == 0 && raw.StartsWith("  ") && blocks.Count > 0 && blocks[^1].Kind == HelpBlockKind.Bullet)
            {
                blocks[^1] = blocks[^1] with { Text = Join([blocks[^1].Text, Plain(line)]) };
                continue;
            }
            paragraph.Add(Plain(line));
        }
        Flush();
        return blocks;
    }

    private static string Plain(string text) => text.Replace("**", "").Trim();

    /// <summary>折り返した行をつなぐ。日本語どうしは詰め、英語などは空白を挟む。</summary>
    private static string Join(IEnumerable<string> lines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0 && sb[^1] < 0x3000 && line.Length > 0 && line[0] < 0x3000) sb.Append(' ');
            sb.Append(line);
        }
        return sb.ToString();
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 決まりの言葉(glossary.json の小休憩・有利・セーヴなど)を、画面の文の中から見つける。
/// 長い言葉を先に探し(「習熟ボーナス」を「習熟」より先に)、英語は語の切れ目だけで、大文字小文字を区別しない。
/// </summary>
public static class Glossary
{
    /// <summary>今のゲームの道具(毎フレーム入れる。シナリオでデータが変わっても、その言葉を使う)。</summary>
    public static GameServices? Services { get; set; }

    private static (GameData Data, string Language, List<(string Word, GlossaryTerm Term)> Words)? _cache;

    private static List<(string Word, GlossaryTerm Term)> Words(GameServices sv)
    {
        string lang = GameServices.Language;
        if (_cache is { } c && ReferenceEquals(c.Data, sv.Data) && c.Language == lang) return c.Words;
        var words = new List<(string, GlossaryTerm)>();
        foreach (var t in sv.Data.Glossary.Values)
        {
            words.Add((sv.Name(t.Name), t));
            if (t.Aliases.TryGetValue(lang, out var aliases)) words.AddRange(aliases.Select(a => (a, t)));
        }
        // 技能(skills.json の名前と説明。装備の「隠密 +1」など)。用語集に同じ名前があれば、そちらを使う
        var named = words.Select(w => w.Item1).ToHashSet();
        foreach (var sk in sv.Data.Skills.Values.OrderBy(k => k.Id, StringComparer.Ordinal))
        {
            string name = sv.Name(sk.Name);
            if (name.Length == 0 || named.Contains(name)) continue;
            words.Add((name, new GlossaryTerm { Id = SkillPrefix + sk.Id, Name = sk.Name, Description = sk.Description }));
        }
        var list = words.Where(w => w.Item1.Length > 0).OrderByDescending(w => w.Item1.Length).ToList();
        _cache = (sv.Data, lang, list);
        return list;
    }

    /// <summary>技能から作った言葉の id の頭。</summary>
    public const string SkillPrefix = "skill:";

    /// <summary>かな・漢字か(技能の名前は、すぐ後ろにこれが続くときは拾わない。「自然に」「歴史の」などのふつうの言葉と分けるため)。</summary>
    private static bool Cjk(char ch) => ch is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿';

    /// <summary>文の中の言葉の場所(重ならないもの、前から順)。</summary>
    public static List<(int Start, int Length, GlossaryTerm Term)> Find(GameServices sv, string text)
    {
        var found = new List<(int, int, GlossaryTerm)>();
        if (text.Length == 0) return found;
        var taken = new bool[text.Length];
        foreach (var (word, term) in Words(sv))
        {
            bool latin = word.All(ch => ch < 0x3000);
            int from = 0;
            while (from < text.Length)
            {
                int i = text.IndexOf(word, from, latin ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                if (i < 0) break;
                from = i + 1;
                // 英語は語の途中では見つけない(「saves」の中の「save」は、長い方の言葉が先に取る)
                if (latin && (i > 0 && char.IsLetterOrDigit(text[i - 1]) || i + word.Length < text.Length && char.IsLetterOrDigit(text[i + word.Length]))) continue;
                if (term.Id.StartsWith(SkillPrefix) && i + word.Length < text.Length && Cjk(text[i + word.Length])) continue;
                if (Enumerable.Range(i, word.Length).Any(k => taken[k])) continue;
                for (int k = i; k < i + word.Length; k++) taken[k] = true;
                found.Add((i, word.Length, term));
            }
        }
        return found.OrderBy(f => f.Item1).ToList();
    }
}

/// <summary>
/// 決まりの言葉に点線の下線を付けて、幅で折り返して描く(Draw2D.TextWrapped と同じ形)。言葉にマウスを載せると説明の窓を出す。
/// 折り返しで2行にまたがった言葉には付けない。
/// </summary>
public static class LinkText
{
    /// <summary>
    /// 文の中で色を変える所の印: Mark・色の字・文・MarkEnd(Colored で作る)。色の字は d(暗く。追加効果の「T1/T3」の「/T3」)、
    /// y(2/3 に弱まった数字)、r(1/3 に弱まった数字)。1行にいくつでも書ける。
    /// </summary>
    public const char Mark = '', MarkEnd = '';

    public const char DimColor = 'd', TwoThirdsColor = 'y', OneThirdColor = 'r';

    public static string Colored(string text, char code) => $"{Mark}{code}{text}{MarkEnd}";

    private static Color? CodeColor(char code) => code switch
    {
        DimColor => Theme.Dim,
        TwoThirdsColor => Theme.HpCaution,
        OneThirdColor => Theme.Warn,
        _ => null,
    };

    /// <summary>印を取り除いた文と、色を変える所(印を取り除いた文での始め・終わりと色)。</summary>
    private static (string Plain, List<(int Start, int End, Color Color)> Spans) Parse(string text)
    {
        var spans = new List<(int, int, Color)>();
        if (text.IndexOf(Mark) < 0) return (text, spans);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == Mark && i + 1 < text.Length)
            {
                int close = text.IndexOf(MarkEnd, i + 2);
                if (close < 0) close = text.Length;
                int from = sb.Length;
                sb.Append(text, i + 2, close - i - 2);
                if (CodeColor(text[i + 1]) is { } c) spans.Add((from, sb.Length, c));
                i = close;
                continue;
            }
            if (text[i] != MarkEnd) sb.Append(text[i]);
        }
        return (sb.ToString(), spans);
    }

    /// <summary>色の印を取り除いた文(幅を測るとき)。</summary>
    public static string Plain(string text) => Parse(text).Plain;

    public static float Draw(Draw2D d, string text, Vector2 position, float width, float size, Color color, float lineGap = 8f)
    {
        var (plain, spans) = Parse(text);
        if (Glossary.Services is not { } sv) return d.TextWrapped(plain, position, width, size, color, lineGap);
        float y = position.Y;
        int cursor = 0;
        foreach (var line in d.WrapLines(plain, width, size))
        {
            // 折り返した行が元の文のどこからか(行の頭の空白は折り返しで落ちることがあるので、探して合わせる)
            int at = line.Length > 0 ? Math.Max(cursor, plain.IndexOf(line, cursor, StringComparison.Ordinal)) : cursor;
            var mine = spans.Where(sp => sp.End > at && sp.Start < at + line.Length)
                .Select(sp => (Math.Max(0, sp.Start - at), Math.Min(line.Length, sp.End - at), sp.Color)).ToList();
            Line(d, sv, line, new Vector2(position.X, y), size, color, mine);
            cursor = at + line.Length;
            y += size + lineGap;
        }
        return y;
    }

    /// <summary>1行を描く(折り返さない)。spans はその行の中の色を変える所(始め・終わりと色)。</summary>
    public static void Line(Draw2D d, GameServices sv, string line, Vector2 at, float size, Color color, List<(int Start, int End, Color Color)>? spans = null)
    {
        float x = at.X;
        // ふつうの文の一切れ(色を変える所に掛かれば、そこだけその色で)
        void Plain(int from, int to)
        {
            var cuts = new SortedSet<int> { from, to };
            foreach (var r in spans ?? []) { if (r.Start > from && r.Start < to) cuts.Add(r.Start); if (r.End > from && r.End < to) cuts.Add(r.End); }
            var at2 = cuts.ToList();
            for (int i = 0; i + 1 < at2.Count; i++)
            {
                string piece = line[at2[i]..at2[i + 1]];
                var c = (spans ?? []).Where(q => at2[i] >= q.Start && at2[i] < q.End).Select(q => (Color?)q.Color).FirstOrDefault() ?? color;
                d.Text(piece, new Vector2(x, at.Y), size, c);
                x += d.Fonts.Measure(piece, size).X;
            }
        }
        int pos = 0;
        foreach (var (start, length, term) in Glossary.Find(sv, line))
        {
            if (start > pos) Plain(pos, start);
            var word = new LinkWord(line.Substring(start, length), sv.Name(term.Name), term.Description is { } desc ? sv.Name(desc) : null);
            var wordColor = (spans ?? []).Where(q => start >= q.Start && start < q.End).Select(q => (Color?)q.Color).FirstOrDefault() ?? color;   // 色を変える所の中の言葉は、その色で
            x += LinkFlow.Word(d, word, new Vector2(x, at.Y), size, wordColor);
            pos = start + length;
        }
        if (pos < line.Length) Plain(pos, line.Length);
    }
}

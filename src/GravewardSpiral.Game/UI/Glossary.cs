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
    public static float Draw(Draw2D d, string text, Vector2 position, float width, float size, Color color, float lineGap = 8f)
    {
        if (Glossary.Services is not { } sv) return d.TextWrapped(text, position, width, size, color, lineGap);
        float y = position.Y;
        foreach (var line in d.WrapLines(text, width, size))
        {
            Line(d, sv, line, new Vector2(position.X, y), size, color);
            y += size + lineGap;
        }
        return y;
    }

    /// <summary>1行を描く(折り返さない)。</summary>
    public static void Line(Draw2D d, GameServices sv, string line, Vector2 at, float size, Color color)
    {
        var hits = Glossary.Find(sv, line);
        if (hits.Count == 0)
        {
            d.Text(line, at, size, color);
            return;
        }
        float x = at.X;
        int pos = 0;
        foreach (var (start, length, term) in hits)
        {
            if (start > pos)
            {
                string plain = line[pos..start];
                d.Text(plain, new Vector2(x, at.Y), size, color);
                x += d.Fonts.Measure(plain, size).X;
            }
            var word = new LinkWord(line.Substring(start, length), sv.Name(term.Name), term.Description is { } desc ? sv.Name(desc) : null);
            x += LinkFlow.Word(d, word, new Vector2(x, at.Y), size, color);
            pos = start + length;
        }
        if (pos < line.Length) d.Text(line[pos..], new Vector2(x, at.Y), size, color);
    }
}

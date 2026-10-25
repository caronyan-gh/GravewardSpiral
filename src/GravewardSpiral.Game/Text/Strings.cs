namespace GravewardSpiral.Game.Text;

/// <summary>
/// 画面の文言。コンテンツパックの text/&lt;言語&gt;.json({"キー": "文言"})を読み合わせたもの。
/// その言語に無いキーは、fallbacks の順(英語、日本語)に探す。どこにも無ければ、キーそのものを [ ] で囲んで出す(抜けに気づけるように)。
/// </summary>
public sealed class Strings
{
    private readonly IReadOnlyDictionary<string, string> _texts;
    private readonly IReadOnlyDictionary<string, string>[] _fallbacks;

    public Strings(IReadOnlyDictionary<string, string> texts, params IReadOnlyDictionary<string, string>[] fallbacks)
    {
        _texts = texts;
        _fallbacks = fallbacks;
    }

    public string this[string key]
    {
        get
        {
            if (_texts.TryGetValue(key, out var s)) return s;
            foreach (var f in _fallbacks)
            {
                if (f.TryGetValue(key, out var t)) return t;
            }
            return $"[{key}]";
        }
    }

    /// <summary>並べた名前(日本語は「・」、英語は「, 」。text の common.list_sep)。</summary>
    public string List(IEnumerable<string> items) => string.Join(this["common.list_sep"], items);

    public string Format(string key, params object[] args) => string.Format(this[key], args);
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 技・能力・回数の名前と説明。データ(features.json の name・description、クラスと種族の pools の name・description)を使い、
/// 書いていなければ前の形の文言(技は feature.&lt;id&gt; と feature.about.&lt;id&gt;、能力は passive.&lt;id&gt; と passive.about.&lt;id&gt;、回数は pool.&lt;id&gt;)。
/// 拾う順は、今の言語のデータ → 今の言語の文言 → 代わりの言語のデータ → 代わりの言語の文言(読み込みの検査と同じく、言語ごとに見る。空の文は無いのと同じ)。
/// </summary>
public static class FeatureText
{
    private static string? Pick(GameServices sv, LocalizedText? data, string key)
    {
        string lang = GameServices.Language;
        if (data != null && data.Texts.TryGetValue(lang, out var own) && !string.IsNullOrWhiteSpace(own)) return own;
        if (sv.Strings.HasOwn(key)) return sv.Strings[key];
        if (data != null && data.Texts.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) is { } other) return sv.Name(data) is { Length: > 0 } n && !string.IsNullOrWhiteSpace(n) ? n : other;
        return sv.Strings.Has(key) && !string.IsNullOrWhiteSpace(sv.Strings[key]) ? sv.Strings[key] : null;
    }

    private static string Prefix(ClassFeatureDef? f) => f?.Kind == "action" ? "feature." : "passive.";

    public static string Name(GameServices sv, string id)
    {
        var f = sv.Data.Features.GetValueOrDefault(id);
        return Pick(sv, f?.Name, Prefix(f) + id) ?? $"[{Prefix(f) + id}]";
    }

    /// <summary>説明(無ければ null)。回数を使う技は、回数の決まり(いくつ・いつ戻る)の行を足す(クラスを選ぶときに分かるように)。</summary>
    public static string? About(GameServices sv, string id)
    {
        if (!sv.Data.Features.TryGetValue(id, out var f)) return null;
        var about = Pick(sv, f.Description, Prefix(f) + "about." + id);
        if (f.Pool != null && PoolRule(sv, f.Pool, Name(sv, id)) is { } rule) about = about == null ? rule : about + "\n" + rule;
        return about;
    }

    /// <summary>
    /// 回数の決まり: 「回数: 魅力の修正値(最低 1)。大休憩で戻る」のような1行(キャラによらない書き方)。
    /// 回数の名前が技の名前(feature)と違えば(気・癒しの手など)、「回数」の代わりに回数の名前を出す。
    /// </summary>
    public static string? PoolRule(GameServices sv, string pool, string? feature = null)
    {
        var def = sv.Data.Classes.Values.SelectMany(c => c.Pools).Concat(sv.Data.Races.Values.SelectMany(x => x.Pools)).FirstOrDefault(x => x.Id == pool);
        if (def == null) return null;
        var s = sv.Strings;
        string rest = def.Recharge == "short" ? s["pool.rule.short"]
            : def.ShortWith != null ? s.Format("pool.rule.long_short_with", Name(sv, def.ShortWith)) : s["pool.rule.long"];
        string label = PoolName(sv, pool) is var name && name != feature ? name : s["pool.rule.uses"];
        return s.Format("pool.rule", label, Count(def.Max), rest);

        string Num(double x) => x >= 99 ? s["pool.rule.unlimited"] : x.ToString("0.##");
        string Count(LevelNumber n)
        {
            if (n.Value is int v) return v >= 99 ? s["pool.rule.unlimited"] : s.Format("pool.rule.times", v);
            if (n.ByLevel != null) return string.Join(s["pool.rule.sep"], n.ByLevel.Select(kv => s.Format("pool.rule.at_level", kv.Key, Num(kv.Value))));
            var terms = new List<string>();
            if (n.Base != 0) terms.Add(Num(n.Base));
            if (n.ClassLevel != 0) terms.Add(n.ClassLevel == 1 ? s["pool.rule.class_level"] : s.Format("pool.rule.class_level_x", Num(n.ClassLevel)));
            if (n.TotalLevel != 0) terms.Add(n.TotalLevel == 1 ? s["pool.rule.total_level"] : s.Format("pool.rule.total_level_x", Num(n.TotalLevel)));
            if (n.Ability is { } ab) terms.Add(s.Format("pool.rule.ability_mod", s["ability." + ab.ToString().ToLowerInvariant()]));
            if (n.Proficiency != 0) terms.Add(s["pool.rule.proficiency"]);
            string text = string.Join(" + ", terms);
            if (n.Min is int min) text += s.Format("pool.rule.min", min);
            return text;
        }
    }

    /// <summary>説明の窓を出せる、技か能力の名前。</summary>
    public static LinkWord Link(GameServices sv, string id) => new(Name(sv, id), null, About(sv, id));

    public static string PoolName(GameServices sv, string pool) => Pick(sv, sv.Data.PoolName(pool), "pool." + pool) ?? $"[pool.{pool}]";

    /// <summary>回数の説明: 書いたものがあればそれ、無ければその回数を使う技(持っているもの)の名前と説明。どちらも無ければ null。</summary>
    public static string? PoolAbout(GameServices sv, Character who, string pool)
    {
        if (Pick(sv, sv.Data.PoolDescription(pool), "pool.about." + pool) is { } d) return d;
        var uses = ClassFeatures.ActiveFor(sv.Data, who).Where(f => f.Pool == pool).ToList();
        if (uses.Count == 0) return null;
        if (uses.Count == 1) return About(sv, uses[0].Id);
        return string.Join("\n", uses.Select(f => $"{Name(sv, f.Id)}: {About(sv, f.Id)}"));
    }

    /// <summary>
    /// 戦闘の文が持つ技と能力の名前のキー(feature.&lt;id&gt; と passive.&lt;id&gt;。コアは名前を知らないので、このキーで渡す)を名前にする。
    /// 技や能力のキーでなければ null。
    /// </summary>
    public static string? FromKey(GameServices sv, string key)
    {
        string? id = key.StartsWith("feature.") ? key["feature.".Length..] : key.StartsWith("passive.") ? key["passive.".Length..] : null;
        return id != null && !id.StartsWith("about.") ? Name(sv, id) : null;
    }
}

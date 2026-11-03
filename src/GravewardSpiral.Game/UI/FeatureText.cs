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

    /// <summary>説明(無ければ null)。</summary>
    public static string? About(GameServices sv, string id) =>
        sv.Data.Features.TryGetValue(id, out var f) ? Pick(sv, f.Description, Prefix(f) + "about." + id) : null;

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

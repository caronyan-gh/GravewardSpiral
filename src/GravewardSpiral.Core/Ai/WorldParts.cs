using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// 自由な迷宮の由来を組み立てる部品(パックの world_parts.json)。AI が無くても、部品をつないで背景・階の名前・入ったときの文・書き付けを作る。
/// kind ごとの中身:
/// - place(昔ここは何だったか): tags、types(出る敵の種類の重み。浅い階ほど効く)、past(昔の文)、floors(階の名前)、enter(入ったときの文)、docs(書き付け)
/// - calamity(何が起きたか): fits(合う場所の tags。空ならどこでも)、types(中ほどの階に効く)、text、floors、enter、docs
/// - depth(底に何が眠るか): fits、types(深い階ほど効く)、text(噂)、floors(深い階の名前)、docs
/// - request(今の町の頼み): text
/// - town(町の名前): name
/// 文の中の {town} は町の名前、{founder} は人の名前(名前辞書の人間)に置き換える。
/// </summary>
public sealed class WorldPart
{
    public const string Collection = "world_parts";

    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<string> Fits { get; set; } = [];
    public Dictionary<string, double> Types { get; set; } = [];
    public List<LocalizedText> Past { get; set; } = [];
    public List<LocalizedText> Text { get; set; } = [];
    public List<LocalizedText> Floors { get; set; } = [];
    public List<LocalizedText> Enter { get; set; } = [];
    public List<LocalizedText> Docs { get; set; } = [];
    public LocalizedText? Name { get; set; }

    public bool FitsPlace(WorldPart place) => Fits.Count == 0 || Fits.Any(place.Tags.Contains);
}

/// <summary>この世界で選んだ由来(セーブに入れる)。部品の id と、差し込んだ名前。</summary>
public sealed class WorldOrigin
{
    public string Place { get; set; } = "";
    public string Calamity { get; set; } = "";
    public string Depth { get; set; } = "";
    public string Request { get; set; } = "";
    public string Town { get; set; } = "";
    public LocalizedText? Founder { get; set; }
}

public static class WorldBuilder
{
    /// <summary>部品を種類ごとに(id の順)。</summary>
    private static List<WorldPart> Parts(GameData data, string kind) =>
        data.WorldParts.Values.Where(p => p.Kind == kind).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    private static WorldPart? Find(GameData data, string id) => data.WorldParts.GetValueOrDefault(id);

    /// <summary>由来を選ぶ(世界のシードで決まる)。部品が足りなければ null。</summary>
    public static WorldOrigin? Pick(GameData data, ulong seed)
    {
        var rng = new Pcg32(seed, 53);
        var places = Parts(data, "place");
        var requests = Parts(data, "request");
        var towns = Parts(data, "town");
        if (places.Count == 0 || requests.Count == 0 || towns.Count == 0) return null;
        var place = places[rng.Next(0, places.Count)];
        var calamities = Parts(data, "calamity").Where(c => c.FitsPlace(place)).ToList();
        var depths = Parts(data, "depth").Where(d => d.FitsPlace(place)).ToList();
        if (calamities.Count == 0 || depths.Count == 0) return null;
        var founder = RandomNames.Pick(data, "human", "en", rng) is { } en
            ? data.NameLists.GetValueOrDefault("human")?.Names.FirstOrDefault(n => n.Get("en") == en)
            : null;
        return new WorldOrigin
        {
            Place = place.Id,
            Calamity = calamities[rng.Next(0, calamities.Count)].Id,
            Depth = depths[rng.Next(0, depths.Count)].Id,
            Request = requests[rng.Next(0, requests.Count)].Id,
            Town = towns[rng.Next(0, towns.Count)].Id,
            Founder = founder,
        };
    }

    /// <summary>
    /// その深さで出る敵の種類の重み。浅い階は場所、中ほどは災い、深い階は底が強く効く。
    /// どれにも書いていない種類は少しだけ出る("*")。
    /// </summary>
    public static FloorTheme? Theme(GameData data, WorldOrigin origin, double depth)
    {
        var place = Find(data, origin.Place);
        var calamity = Find(data, origin.Calamity);
        var bottom = Find(data, origin.Depth);
        if (place == null || calamity == null || bottom == null) return null;
        double d = Math.Clamp(depth, 0, 1);
        double wPlace = 1.0 - 0.6 * d, wCalamity = 0.4 + 0.6 * (1 - Math.Abs(d - 0.5) * 2), wDepth = 1.5 * d * d;
        var types = new Dictionary<string, double>(StringComparer.Ordinal) { ["*"] = 0.15 };
        void Add(Dictionary<string, double> from, double w)
        {
            foreach (var (k, v) in from) types[k] = types.GetValueOrDefault(k) + v * w;
        }
        Add(place.Types, wPlace);
        Add(calamity.Types, wCalamity);
        Add(bottom.Types, wDepth);
        return new FloorTheme { Id = $"origin:{origin.Place}", MinDepth = 0, MaxDepth = 1, Types = types };
    }

    /// <summary>
    /// 部品をつないで世界の文を作る(AI が無いとき、AI が失敗したとき)。同じ世界なら同じ文になる。
    /// 序文は「昔ここは」+「何が起きたか」の段落と、「底の噂」+「町の頼み」の段落。
    /// 階の名前は浅い階から場所 → 災い → 底の順に、入ったときの文と書き付けも同じ割り振りで選ぶ(同じものは使い回さない)。
    /// </summary>
    public static WorldText? Build(GameData data, WorldOrigin origin, int floorCount, ulong seed)
    {
        var place = Find(data, origin.Place);
        var calamity = Find(data, origin.Calamity);
        var bottom = Find(data, origin.Depth);
        var request = Find(data, origin.Request);
        var town = Find(data, origin.Town)?.Name;
        if (place == null || calamity == null || bottom == null || request == null || town == null) return null;
        var rng = new Pcg32(seed, 59);
        var founder = origin.Founder ?? LocalizedText.Of("Aldric", "アルドリック");
        LocalizedText Fill(LocalizedText t) => LocalizedText.Of(
            Slot(t.Get("en"), town.Get("en"), founder.Get("en")),
            Slot(t.Get("ja"), town.Get("ja"), founder.Get("ja")));
        LocalizedText? One(List<LocalizedText> list) => list.Count == 0 ? null : Fill(list[rng.Next(0, list.Count)]);
        LocalizedText Join(string sepEn, string sepJa, params LocalizedText?[] parts)
        {
            var ok = parts.OfType<LocalizedText>().ToList();
            return LocalizedText.Of(string.Join(sepEn, ok.Select(p => p.Get("en"))), string.Join(sepJa, ok.Select(p => p.Get("ja"))));
        }

        var world = new WorldText
        {
            Model = "parts",
            Prologue = Join("\n\n", "\n\n",
                Join(" ", "", One(place.Past), One(calamity.Text)),
                Join(" ", "", One(bottom.Text), One(request.Text))),
        };
        int floors = Math.Clamp(floorCount, 1, WorldWriter.MaxFloors);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedDocs = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < floors; i++)
        {
            double d = floors <= 1 ? 0 : (double)i / (floors - 1);
            // 浅い 4 割は場所、次の 3 割は災い、深い 3 割は底(無ければ前の段)
            var band = d < 0.4 ? new[] { place, calamity, bottom } : d < 0.7 ? [calamity, place, bottom] : [bottom, calamity, place];
            world.Floors.Add(new WorldFloorText
            {
                Name = Unused(band.Select(p => p.Floors), usedNames),
                Enter = Unused(band.Select(p => p.Enter), null),
                Documents = Enumerable.Range(0, WorldWriter.DocumentsPerFloor)
                    .Select(_ => Unused(band.Select(p => p.Docs), usedDocs)).OfType<LocalizedText>().ToList(),
            });
        }
        return world;

        // 帯の部品から、まだ使っていないものを1つ(先の部品から順に探す)
        LocalizedText? Unused(IEnumerable<List<LocalizedText>> lists, HashSet<string>? used)
        {
            foreach (var list in lists)
            {
                var fresh = list.Where(t => used == null || !used.Contains(t.Get("en"))).ToList();
                if (fresh.Count == 0) continue;
                var pick = fresh[rng.Next(0, fresh.Count)];
                used?.Add(pick.Get("en"));
                return Fill(pick);
            }
            // 使い切ったら、使ったものからもう一度(階が多い世界)
            var any = lists.FirstOrDefault(l => l.Count > 0);
            return any == null ? null : Fill(any[rng.Next(0, any.Count)]);
        }
    }

    private static string Slot(string text, string town, string founder) =>
        text.Replace("{town}", town).Replace("{founder}", founder);

    /// <summary>AI に渡す由来の説明(英語。部品の最初の文を並べる)。</summary>
    public static string Describe(GameData data, WorldOrigin origin)
    {
        string First(string id, Func<WorldPart, List<LocalizedText>> list) =>
            Find(data, id) is { } p && list(p).FirstOrDefault() is { } t ? t.Get("en") : "";
        var town = Find(data, origin.Town)?.Name?.Get("en") ?? "the town";
        string founder = origin.Founder?.Get("en") ?? "its founder";
        return Slot($"Town: {town}. What the dungeon was: {First(origin.Place, p => p.Past)} What went wrong: {First(origin.Calamity, p => p.Text)} " +
                    $"What lies at the bottom: {First(origin.Depth, p => p.Text)} The town's request: {First(origin.Request, p => p.Text)}", town, founder);
    }
}

using System.Collections;
using GravewardSpiral.Core.Maze;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>選ぶ欄の1つ(中身は id、見せるのは今の言語の名前と id)。</summary>
public sealed record Choice(string Key, string Label)
{
    public override string ToString() => Key.Length == 0 ? Label : Label.Length == 0 || Label == Key ? Key : $"{Label}  ({Key})";
}

/// <summary>
/// id → 名前の表(日本語と English の両方を持ち、引くと今の言語の名前を返す。言語はその場で変わるので、持つのは両方)。
/// 並びは、作ったときの順。
/// </summary>
public sealed class PartTable<TKey> : IReadOnlyDictionary<TKey, string> where TKey : notnull
{
    private readonly List<TKey> _keys = [];
    private readonly Dictionary<TKey, (string Ja, string En)> _names = [];

    public PartTable(params (TKey Key, string Ja, string En)[] items)
    {
        foreach (var (key, ja, en) in items)
        {
            _keys.Add(key);
            _names[key] = (ja, en);
        }
    }

    private static string Pick((string Ja, string En) n) => T(n.Ja, n.En);

    public string this[TKey key] => Pick(_names[key]);
    public IEnumerable<TKey> Keys => _keys;
    public IEnumerable<string> Values => _keys.Select(k => Pick(_names[k]));
    public int Count => _keys.Count;
    public bool ContainsKey(TKey key) => _names.ContainsKey(key);

    public bool TryGetValue(TKey key, out string value)
    {
        if (_names.TryGetValue(key, out var n))
        {
            value = Pick(n);
            return true;
        }
        value = "";
        return false;
    }

    public IEnumerator<KeyValuePair<TKey, string>> GetEnumerator() => _keys.Select(k => KeyValuePair.Create(k, Pick(_names[k]))).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// イベントの部品・迷宮の仕掛けなどの名前(エディタで見せるだけ。ファイルには id を書く)。画面の言語の名前を返す。
/// 並びは、選ぶ欄に出す順(似たものを近くに)。ここに無い id は、id のまま出す。
/// </summary>
public static class PartNames
{
    public static readonly IReadOnlyDictionary<string, string> Steps = new PartTable<string>(
        ("text", "文を見せる", "Show text"), ("choice", "選択肢", "Choice"), ("if", "もし", "If"), ("check", "技能の判定", "Skill check"), ("random", "どれか1つ", "Pick one at random"),
        ("flag", "フラグを立てる", "Set flag"), ("unflag", "フラグを消す", "Clear flag"), ("counter", "数を足す", "Add to counter"), ("counter_set", "数を決める", "Set counter"),
        ("give_item", "品を渡す", "Give item"), ("take_item", "品を取る", "Take item"), ("treasure", "宝", "Treasure"), ("gold", "お金", "Gold"), ("xp", "経験値", "Experience"),
        ("heal", "回復", "Heal"), ("damage", "ダメージ", "Damage"), ("condition", "状態にする", "Inflict condition"), ("cure", "状態を治す", "Cure condition"), ("light", "明かり", "Light"),
        ("combat", "戦闘", "Combat"), ("open", "門を開ける", "Open gate"), ("sound", "効果音", "Sound effect"), ("run", "別のイベント", "Run another event"), ("world_doc", "AI の書き付け", "AI-written note"),
        ("town", "町へ戻る", "Return to town"), ("end", "終わる", "End"), ("again", "終わる(また起こる)", "End (can happen again)"),
        ("ending", "エンディングへ", "Go to the ending"));

    /// <summary>手順の部品の組(足すとき、まず組を選んで絞る)。</summary>
    public static readonly (string Ja, string En, string[] Keys)[] StepGroups =
    [
        ("文と選択", "Text & choices", ["text", "choice", "world_doc"]),
        ("流れ", "Flow", ["if", "check", "random", "run", "end", "again", "town", "ending"]),
        ("フラグと数", "Flags & counters", ["flag", "unflag", "counter", "counter_set"]),
        ("品とお金", "Items & gold", ["give_item", "take_item", "treasure", "gold", "xp"]),
        ("パーティ", "Party", ["heal", "damage", "condition", "cure", "light"]),
        ("迷宮と戦闘", "Maze & combat", ["combat", "open", "sound"]),
    ];

    public static readonly IReadOnlyDictionary<string, string> Conditions = new PartTable<string>(
        ("flag", "フラグが立っている", "Flag is set"), ("done", "イベントが済んだ", "Event is done"), ("item", "品を持っている", "Has item"), ("gold", "お金がある", "Has gold"),
        ("level", "レベル", "Level"), ("class", "クラスの人がいる", "Class in party"), ("race", "種族の人がいる", "Race in party"), ("counter", "数を比べる", "Compare counter"),
        ("chance", "確率", "Chance"), ("key", "この階の鍵", "Key for this floor"), ("alive", "動ける人の数", "Members able to act"), ("depth", "階の深さ", "Floor depth"),
        ("facing", "向いている方角", "Facing"), ("turned", "向きを変えて起きた", "Fired by turning"));

    public static readonly IReadOnlyDictionary<string, string> Facings = new PartTable<string>(
        ("north", "北(地図の上)", "North (up on the map)"), ("east", "東", "East"), ("south", "南", "South"), ("west", "西", "West"));

    public static readonly IReadOnlyDictionary<string, string> Triggers = new PartTable<string>(
        ("enter", "マスに着いたとき", "On entering the cell"), ("turn", "そのマスで向きを変えたとき", "On turning in the cell"), ("both", "着いたときと、向きを変えたとき", "On entering and on turning"));

    public static readonly IReadOnlyDictionary<string, string> Places = new PartTable<string>(("maze", "迷宮", "Maze"), ("town", "町", "Town"));
    public static readonly IReadOnlyDictionary<string, string> Once = new PartTable<string>(("", "何度でも", "Any number of times"), ("game", "世界で一度", "Once per world"), ("place", "そのマスで一度", "Once per cell"));
    public static readonly IReadOnlyDictionary<string, string> Where = new PartTable<string>(("any", "どこでも", "Anywhere"), ("dead_end", "行き止まり", "Dead end"), ("room", "部屋", "Room"), ("corridor", "通路", "Corridor"));
    public static readonly IReadOnlyDictionary<string, string> Rarity = new PartTable<string>(("common", "ふつう", "Common"), ("uncommon", "少し珍しい", "Uncommon"), ("rare", "珍しい", "Rare"), ("legendary", "伝説", "Legendary"));

    /// <summary>モンスターの種類(creature_type)。</summary>
    public static readonly IReadOnlyDictionary<string, string> CreatureTypes = new PartTable<string>(
        ("undead", "アンデッド", "Undead"), ("beast", "獣", "Beasts"), ("humanoid", "人型", "Humanoids"), ("swarm", "群れ", "Swarms"),
        ("monstrosity", "怪物", "Monstrosities"), ("ooze", "粘体", "Oozes"), ("construct", "人造", "Constructs"), ("fiend", "悪魔", "Fiends"),
        ("elemental", "精霊", "Elementals"), ("giant", "巨人", "Giants"), ("dragon", "竜", "Dragons"), ("plant", "植物", "Plants"),
        ("fey", "妖精", "Fey"), ("celestial", "天上の者", "Celestials"), ("aberration", "異形", "Aberrations"));

    public static readonly IReadOnlyDictionary<CellFeature, string> Features = new PartTable<CellFeature>(
        (CellFeature.None, "なし", "None"), (CellFeature.StairsUp, "上り階段", "Stairs up"), (CellFeature.StairsDown, "下り階段", "Stairs down"), (CellFeature.Spinner, "回転床", "Spinner"),
        (CellFeature.Teleporter, "テレポーター", "Teleporter"), (CellFeature.Pit, "落とし穴", "Pit"), (CellFeature.Chute, "シュート", "Chute"), (CellFeature.Solid, "岩盤", "Solid rock"),
        (CellFeature.Elevator, "エレベーター", "Elevator"));

    /// <summary>id の並びを、名前の表の順(表に無いものは後ろに id の順)で、選ぶ欄の中身にする。</summary>
    public static List<Choice> Choices(IEnumerable<string> keys, IReadOnlyDictionary<string, string>? names)
    {
        var set = keys.ToHashSet();
        var known = names?.Keys.Where(set.Contains).ToList() ?? [];
        return known.Concat(set.Except(known).Order(StringComparer.Ordinal))
            .Select(k => new Choice(k, names?.GetValueOrDefault(k) ?? "")).ToList();
    }

    /// <summary>名前(無ければ id)。</summary>
    public static string Name(IReadOnlyDictionary<string, string> names, string key) => names.GetValueOrDefault(key) ?? key;
}

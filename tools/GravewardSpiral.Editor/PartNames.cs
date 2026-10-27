using System.Collections;
using GravewardSpiral.Core.Maze;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>選ぶ欄の1つ(中身は id、見せるのは今の言語の名前と id)。</summary>
public sealed record Choice(string Key, string Label)
{
    public override string ToString() => Key.Length == 0 ? Label : Label.Length == 0 || Label == Key ? Key : $"{Label}  ({Key})";

    /// <summary>欄に出す文字(ToString と同じ。打てる欄の文字の元に使う)。</summary>
    public string Display => ToString();
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
    /// <summary>
    /// イベントの流れの部品の名前。イベントを組む人が見慣れた RPG ツクールのイベントコマンドの言い方に寄せる
    /// (文章の表示・条件分岐・スイッチ〔ここではフラグ〕の操作・所持金の増減・戦闘の処理・場所移動 など)。ツクールに無いものは、同じ形の言い方にする。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Steps = new PartTable<string>(
        ("text", "文章の表示", "Show text"), ("choice", "選択肢の表示", "Show choices"), ("world_doc", "書き付けの表示", "Show a note"),
        ("if", "条件分岐", "Conditional branch"), ("check", "技能判定(成功・失敗で分岐)", "Skill check (success / failure)"), ("random", "ランダム分岐", "Random branch"),
        ("run", "別のイベントの呼び出し", "Call another event"), ("timer", "歩数タイマーの開始", "Start step timer"), ("timer_stop", "歩数タイマーの停止", "Stop step timer"),
        ("end", "イベント処理の中断", "Exit event"), ("again", "イベント処理の中断(また起こる)", "Exit event (happens again)"), ("town", "町へ戻る", "Return to town"),
        ("ending", "エンディング(クリア)", "Ending (clear)"),
        ("flag", "フラグを立てる", "Set flag"), ("unflag", "フラグを消す", "Clear flag"),
        ("counter", "変数の増減", "Change variable"), ("counter_set", "変数の代入", "Set variable"),
        ("give_item", "アイテムを増やす", "Gain item"), ("take_item", "アイテムを減らす", "Lose item"), ("treasure", "宝の入手", "Get treasure"),
        ("gold", "所持金の増減", "Change gold"), ("xp", "経験値の増減", "Change XP"),
        ("pick", "メンバーを1人選ぶ(その人だけに)", "Choose a member (to them only)"), ("heal", "HP の回復", "Recover HP"), ("damage", "ダメージ", "Damage"),
        ("condition", "状態異常にする", "Add condition"), ("cure", "状態異常を治す", "Remove condition"), ("spend_slot", "呪文スロットの消費", "Use a spell slot"),
        ("light", "明かりの操作", "Control light"),
        ("combat", "戦闘の処理", "Battle processing"), ("open", "門を開ける", "Open gate"), ("close", "門を閉める", "Close gate"),
        ("warp", "場所移動", "Transfer party"), ("sound", "SE の演奏", "Play SE"));

    /// <summary>手順の部品の組(足すとき、まず組を選んで絞る)。</summary>
    public static readonly (string Ja, string En, string[] Keys)[] StepGroups =
    [
        ("文と選択", "Text & choices", ["text", "choice", "world_doc"]),
        ("流れ", "Flow", ["if", "check", "random", "run", "timer", "timer_stop", "end", "again", "town", "ending"]),
        ("フラグと数", "Flags & counters", ["flag", "unflag", "counter", "counter_set"]),
        ("品とお金", "Items & gold", ["give_item", "take_item", "treasure", "gold", "xp"]),
        ("パーティ", "Party", ["pick", "heal", "damage", "condition", "cure", "spend_slot", "light"]),
        ("迷宮と戦闘", "Maze & combat", ["combat", "open", "close", "warp", "sound"]),
    ];

    public static readonly IReadOnlyDictionary<string, string> Conditions = new PartTable<string>(
        ("flag", "フラグが立っている", "Flag is set"), ("done", "イベントが済んだ", "Event is done"), ("item", "品を持っている", "Has item"), ("gold", "お金がある", "Has gold"),
        ("level", "レベル", "Level"), ("class", "クラスの人がいる", "Class in party"), ("race", "種族の人がいる", "Race in party"), ("counter", "変数を比べる", "Compare variable"),
        ("chance", "確率", "Chance"), ("key", "この階の鍵", "Key for this floor"), ("alive", "動ける人の数", "Members able to act"), ("depth", "階の深さ", "Floor depth"),
        ("facing", "向いている方角", "Facing"), ("turned", "向きを変えて起きた", "Fired by turning"), ("entered", "入り方", "How the party entered"),
        ("equipped", "品を装備している", "Has item equipped"), ("slots", "呪文スロットが残っている", "Has a spell slot left"),
        ("status", "状態になっている", "Has a condition"), ("hp", "HP の割合", "HP percent"), ("armed", "武器を手に持っている", "Holds a weapon"),
        ("lit", "明かりがついている", "Light is on"), ("zone", "このマスの区域", "Zone of this cell"), ("cast", "唱えた呪文", "Spell cast"), ("rested", "休んで起きた", "Fired by resting"),
        ("timer", "タイマーが動いている", "Timer running"));

    public static readonly IReadOnlyDictionary<string, string> Abilities = new PartTable<string>(
        ("str", "筋力", "Strength"), ("dex", "敏捷力", "Dexterity"), ("con", "耐久力", "Constitution"), ("int", "知力", "Intelligence"), ("wis", "判断力", "Wisdom"), ("cha", "魅力", "Charisma"));

    public static readonly IReadOnlyDictionary<string, string> Compare = new PartTable<string>(
        (">=", "以上", "at least"), ("<=", "以下", "at most"), ("==", "ちょうど", "exactly"), ("!=", "でない", "not"), (">", "より多い", "more than"), ("<", "より少ない", "less than"));

    public static readonly IReadOnlyDictionary<string, string> Zones = new PartTable<string>(
        ("dark", "暗闇", "Darkness"), ("fog", "霧", "Fog"), ("antimagic", "魔法禁止", "Anti-magic"));

    public static readonly IReadOnlyDictionary<string, string> Entries = new PartTable<string>(
        ("forward", "前へ歩いて", "Walking forward"), ("back", "後ろへ下がって", "Stepping back"), ("left", "左へ横歩きで", "Sidestepping left"), ("right", "右へ横歩きで", "Sidestepping right"));

    public static readonly IReadOnlyDictionary<string, string> Facings = new PartTable<string>(
        ("north", "北(地図の上)", "North (up on the map)"), ("east", "東", "East"), ("south", "南", "South"), ("west", "西", "West"));

    public static readonly IReadOnlyDictionary<string, string> Triggers = new PartTable<string>(
        ("enter", "マスに着いたとき", "On entering the cell"), ("turn", "そのマスで向きを変えたとき", "On turning in the cell"), ("both", "着いたときと、向きを変えたとき", "On entering and on turning"),
        ("cast", "そのマスで呪文を唱えたとき", "On casting a spell in the cell"), ("enter,cast", "着いたときと、呪文を唱えたとき", "On entering and on casting"),
        ("rest", "そのマスで休んだとき", "On resting in the cell"), ("enter,rest", "着いたときと、休んだとき", "On entering and on resting"));

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

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Events;

/// <summary>イベントの検査(読み込みのとき。エディタからも呼ぶ)。部品の種類、参照する id、ダイス式、文言のキーを確かめる。</summary>
public static class EventCheck
{
    public static readonly HashSet<string> Places = ["maze", "town"];
    public static readonly HashSet<string> OnceKinds = ["game", "place"];
    public static readonly HashSet<string> ScatterWhere = ["any", "dead_end", "room", "corridor"];
    /// <summary>エディタで選べる起こるとき(並べたものも)。検査は TriggerParts の組み合わせなら通す。</summary>
    public static readonly List<string> Triggers = ["enter", "turn", "both", "cast", "enter,cast", "rest", "enter,rest"];
    public static readonly HashSet<string> Facings = ["north", "east", "south", "west"];
    public static readonly HashSet<string> Entries = ["forward", "back", "left", "right"];
    public static readonly HashSet<string> Zones = ["dark", "fog", "antimagic"];
    public static readonly HashSet<string> TriggerParts = ["enter", "turn", "both", "cast", "rest"];

    public static List<string> Check(GameData data, EventDef e, ContentLibrary? content = null)
    {
        var problems = new List<string>();
        string where = $"events '{e.Id}'";
        void Add(string text) => problems.Add($"{where}: {text}");
        if (e.OwnFlag != null && string.IsNullOrWhiteSpace(e.OwnFlag)) Add("own_flag に空の名前は使えません");
        if (e.OwnFlag != null && data.Events.Values.Any(other => other.Id != e.Id && other.OwnFlag == e.OwnFlag)) Add("専用フラグの名前が、ほかのイベントと重複しています");
        if (!Places.Contains(e.Place)) Add($"place '{e.Place}' は {string.Join(" / ", Places)} のどれかです");
        if (e.Once != null && !OnceKinds.Contains(e.Once)) Add($"once '{e.Once}' は game / place のどちらかです");
        if (e.Place == "town" && e.Title == null) Add("町のイベントには title が要ります");
        if (e.Trigger.Split(',', StringSplitOptions.TrimEntries).Any(t => !TriggerParts.Contains(t)))
            Add($"trigger '{e.Trigger}' は {string.Join(" / ", TriggerParts)} か、それをカンマで並べたものです");
        if (e.Scatter != null && !ScatterWhere.Contains(e.Scatter.Where)) Add($"scatter の where '{e.Scatter.Where}' は {string.Join(" / ", ScatterWhere)} のどれかです");
        // 深さは階の数ではなく割合(1〜5 と書くと、最下層にしか置かれない)
        if (e.Scatter is { } sc && (sc.MinDepth is < 0 or > 1 || sc.MaxDepth is < 0 or > 1))
            Add($"scatter の min_depth / max_depth({sc.MinDepth} / {sc.MaxDepth})は 0〜1 の割合です(0 が B1、1 が最下層。階の数ではない)");
        else if (e.Scatter is { } s2 && s2.MinDepth > s2.MaxDepth) Add($"scatter の min_depth({s2.MinDepth})が max_depth({s2.MaxDepth})より大きい");
        if (e.Steps.Count == 0) Add("steps がありません");
        if (e.Bgm != null && content != null && content.FindFile(e.Bgm) == null)
            Add($"bgm '{e.Bgm}' がどのパックにもありません");
        var texts = content?.Texts("ja");

        void Conditions(IEnumerable<EventCondition> list)
        {
            foreach (var c in list)
            {
                if (!EventCondition.KnownTypes.Contains(c.Type)) Add($"条件の種類 '{c.Type}' がありません");
                if (c.Type is "flag" or "done" or "item" or "class" or "race" or "counter" && string.IsNullOrEmpty(c.Id)) Add($"条件 {c.Type} に id がありません");
                if (c.Type == "item" && c.Id != null && !data.Items.ContainsKey(c.Id)) Add($"条件 item: items に '{c.Id}' がありません");
                if (c.Type == "class" && c.Id != null && !data.Classes.ContainsKey(c.Id)) Add($"条件 class: classes に '{c.Id}' がありません");
                if (c.Type == "race" && c.Id != null && !data.Races.ContainsKey(c.Id)) Add($"条件 race: races に '{c.Id}' がありません");
                if (c.Type == "done" && c.Id != null && !data.Events.ContainsKey(c.Id)) Add($"条件 done: events に '{c.Id}' がありません");
                if (c.Type == "facing" && !Facings.Contains(c.Id ?? "")) Add($"条件 facing の id '{c.Id}' は {string.Join(" / ", Facings)} のどれかです");
                if (c.Type == "equipped" && (c.Id == null || !data.Items.ContainsKey(c.Id))) Add($"条件 equipped: items に '{c.Id}' がありません");
                if (c.Type == "entered" && !Entries.Contains(c.Id ?? "")) Add($"条件 entered の id '{c.Id}' は {string.Join(" / ", Entries)} のどれかです");
                if (c.Type == "zone" && !Zones.Contains(c.Id ?? "")) Add($"条件 zone の id '{c.Id}' は {string.Join(" / ", Zones)} のどれかです");
                if (c.Type == "status" && !Enum.TryParse<Condition>((c.Id ?? "").Replace("_", ""), true, out _)) Add($"条件 status: 状態 '{c.Id}' がありません");
                if (c.Type == "cast" && !string.IsNullOrEmpty(c.Id) && !data.Spells.ContainsKey(c.Id)) Add($"条件 cast: spells に '{c.Id}' がありません");
            }
        }

        void Steps(IEnumerable<EventStep> list, int depth)
        {
            if (depth > 30) { Add("手順の入れ子が深すぎます"); return; }
            foreach (var s in list)
            {
                if (!EventStep.KnownTypes.Contains(s.Type)) Add($"手順の種類 '{s.Type}' がありません");
                bool needId = s.Type is "flag" or "unflag" or "counter" or "counter_set" or "give_item" or "take_item" or "open" or "close" or "warp" or "sound" or "run" or "timer" or "timer_stop";
                if (needId && string.IsNullOrEmpty(s.Id)) Add($"手順 {s.Type} に id がありません");
                if (s.Type is "give_item" or "take_item" && s.Id != null && !data.Items.ContainsKey(s.Id)) Add($"手順 {s.Type}: items に '{s.Id}' がありません");
                if (s.Type == "run" && s.Id != null && !data.Events.ContainsKey(s.Id)) Add($"手順 run: events に '{s.Id}' がありません");
                if (s.Type == "timer" && s.Id != null && (!data.Events.TryGetValue(s.Id, out var due) || due.Place != "maze"))
                    Add($"手順 timer: 時が来たら起こす迷宮のイベント '{s.Id}' がありません");
                if (s.Type == "timer" && s.Value < 1) Add("手順 timer の value(歩数)は 1 以上です");
                if (s.Type == "text" && s.Text == null && s.Key == null) Add("手順 text に text も key もありません");
                if (s.Key != null && texts != null && !texts.ContainsKey(s.Key)) Add($"文言のキー '{s.Key}' がありません");
                if (s.Type == "choice" && s.Options.Count == 0) Add("手順 choice に options がありません");
                if (s.Type == "choice" && s.Options.Any(o => o.Label == null)) Add("手順 choice の選択肢に label がありません");
                if (s.Type == "random" && s.Options.Count == 0) Add("手順 random に options がありません");
                if (s.Type == "check")
                {
                    if (s.Skill != null && !data.Skills.ContainsKey(s.Skill)) Add($"手順 check: skills に '{s.Skill}' がありません");
                    if (s.Skill == null && s.Ability == null) Add("手順 check に skill も ability もありません");
                }
                if (s.Ability != null)
                {
                    try { Abilities.Parse(s.Ability); }
                    catch (FormatException ex) { Add(ex.Message); }
                }
                if (s.Dice != null && !DiceExpression.TryParse(s.Dice, out _, out var err)) Add($"手順 {s.Type} の dice '{s.Dice}': {err}");
                if (s.Type is "condition" or "cure" && s.Id != null && !Enum.TryParse<Condition>(s.Id.Replace("_", ""), true, out _))
                    Add($"手順 {s.Type}: 状態 '{s.Id}' がありません");
                if (s.Type == "condition" && s.Id == null) Add("手順 condition に id がありません");
                if (s.Type == "damage" && s.DamageType != null && !DamageTypes.All.Contains(s.DamageType)) Add($"手順 damage: ダメージの種類 '{s.DamageType}' がありません");
                if (s.Type is "damage" or "condition" && !Items.Traps.ValidWho(s.Who)) Add($"手順 {s.Type}: who '{s.Who}' は party / trigger / tag:<名前> / race:<id> です");
                if (s.Save is { } sv)
                {
                    try { Abilities.Parse(sv.Ability); }
                    catch (FormatException ex) { Add($"手順 {s.Type} の save: {ex.Message}"); }
                }
                if (s.Type == "trap")
                {
                    if ((s.Id == null) == (s.Set == null)) Add("手順 trap には、id(罠)か set(罠の束)のどちらか1つを書きます");
                    if (s.Id != null && !data.Traps.ContainsKey(s.Id)) Add($"手順 trap: traps に '{s.Id}' がありません");
                    if (s.Set != null && !data.TrapSets.ContainsKey(s.Set)) Add($"手順 trap: trapsets に '{s.Set}' がありません");
                }
                if (s.Type == "combat")
                {
                    foreach (var g in s.Groups.Where(g => !data.Monsters.ContainsKey(g.Monster))) Add($"手順 combat: monsters に '{g.Monster}' がありません");
                    if (s.Boss != null && !data.Bosses.ContainsKey(s.Boss)) Add($"手順 combat: bosses に '{s.Boss}' がありません");
                }
                Conditions(s.Conditions());
                foreach (var child in s.Children()) Steps(child, depth + 1);
            }
        }

        Conditions(e.When);
        Steps(e.Steps, 0);
        if (RunCycle(data, e) is { } cycle) Add($"run が輪になっています({string.Join(" → ", cycle)})。選択や戦闘を挟まずに戻ると、止まらなくなります");
        return problems;
    }

    /// <summary>
    /// 選択も戦闘も挟まずに run でたどると自分に戻る、止まらない輪(あれば、たどった id の並び)。
    /// 選択肢の中の run は、選ぶたびに止まるので数えない。
    /// </summary>
    public static List<string>? RunCycle(GameData data, EventDef e)
    {
        IEnumerable<string> Runs(IEnumerable<EventStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type == "run" && s.Id != null) yield return s.Id;
                if (s.Type is "choice" or "combat" || s.Type == "pick" && s.Value != 1) continue;   // 待つところの先は、輪でも止まる(くじの pick は待たない)
                foreach (var child in s.Children()) foreach (var id in Runs(child)) yield return id;
            }
        }
        var path = new List<string> { e.Id };
        var seen = new HashSet<string>();
        List<string>? Visit(EventDef cur)
        {
            foreach (var id in Runs(cur.Steps).Distinct())
            {
                if (id == e.Id) return [.. path, id];
                if (!seen.Add(id) || !data.Events.TryGetValue(id, out var next)) continue;
                path.Add(id);
                if (Visit(next) is { } found) return found;
                path.RemoveAt(path.Count - 1);
            }
            return null;
        }
        return Visit(e);
    }
}

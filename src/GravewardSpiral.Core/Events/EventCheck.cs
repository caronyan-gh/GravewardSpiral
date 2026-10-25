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

    public static List<string> Check(GameData data, EventDef e, ContentLibrary? content = null)
    {
        var problems = new List<string>();
        string where = $"events '{e.Id}'";
        void Add(string text) => problems.Add($"{where}: {text}");
        if (!Places.Contains(e.Place)) Add($"place '{e.Place}' は {string.Join(" / ", Places)} のどれかです");
        if (e.Once != null && !OnceKinds.Contains(e.Once)) Add($"once '{e.Once}' は game / place のどちらかです");
        if (e.Place == "town" && e.Title == null) Add("町のイベントには title が要ります");
        if (e.Scatter != null && !ScatterWhere.Contains(e.Scatter.Where)) Add($"scatter の where '{e.Scatter.Where}' は {string.Join(" / ", ScatterWhere)} のどれかです");
        if (e.Steps.Count == 0) Add("steps がありません");
        if (e.Bgm != null && content != null && !content.Packs.Any(p => File.Exists(Path.Combine(p.Directory, e.Bgm))))
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
            }
        }

        void Steps(IEnumerable<EventStep> list, int depth)
        {
            if (depth > 30) { Add("手順の入れ子が深すぎます"); return; }
            foreach (var s in list)
            {
                if (!EventStep.KnownTypes.Contains(s.Type)) Add($"手順の種類 '{s.Type}' がありません");
                bool needId = s.Type is "flag" or "unflag" or "counter" or "counter_set" or "give_item" or "take_item" or "open" or "sound" or "run";
                if (needId && string.IsNullOrEmpty(s.Id)) Add($"手順 {s.Type} に id がありません");
                if (s.Type is "give_item" or "take_item" && s.Id != null && !data.Items.ContainsKey(s.Id)) Add($"手順 {s.Type}: items に '{s.Id}' がありません");
                if (s.Type == "run" && s.Id != null && !data.Events.ContainsKey(s.Id)) Add($"手順 run: events に '{s.Id}' がありません");
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
                if (s.Type == "combat")
                {
                    foreach (var g in s.Groups.Where(g => !data.Monsters.ContainsKey(g.Monster))) Add($"手順 combat: monsters に '{g.Monster}' がありません");
                    if (s.Boss != null && !data.Bosses.ContainsKey(s.Boss)) Add($"手順 combat: bosses に '{s.Boss}' がありません");
                }
                Conditions(s.When);
                foreach (var o in s.Options) Conditions(o.When);
                foreach (var child in s.Children()) Steps(child, depth + 1);
            }
        }

        Conditions(e.When);
        Steps(e.Steps, 0);
        return problems;
    }
}

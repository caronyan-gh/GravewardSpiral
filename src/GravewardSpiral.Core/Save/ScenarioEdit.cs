using System.Text.Json.Nodes;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// シナリオの編集で、参照をまとめて直す操作(エディタのタブから呼ぶ)。id を変える・階を消す・階を動かすとき、
/// それを指しているところ(迷宮の印、ほかのイベントの条件と手順、ボスや戦闘の組、名のある敵、世界の文、行き先など)も一緒に直す。
/// </summary>
public static class ScenarioEdit
{
    /// <summary>イベントの id を変える。迷宮の印 event:、run の手順、done の条件(イベント・選択肢・手順・噂の条件)も直す。</summary>
    public static void RenameEvent(ScenarioPack pack, string old, string id)
    {
        if (pack.Events.FirstOrDefault(e => e.Id == old) is { } ev) ev.Id = id;
        foreach (var part in pack.EventSets.SelectMany(s => s.Parts).Where(p => p.Kind == "event" && p.Target == old)) part.Target = id;
        foreach (var f in pack.Floors)
            foreach (var c in f.AllCells())
                if (f[c].Mark == EventPlacer.MarkPrefix + old) f[c].Mark = EventPlacer.MarkPrefix + id;
        void Conditions(IEnumerable<EventCondition> when)
        {
            foreach (var c in when) if (c.Type == "done" && c.Id == old) c.Id = id;
        }
        void Steps(List<EventStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type == "run" && s.Id == old) s.Id = id;
                Conditions(s.Conditions());
                foreach (var child in s.Children()) Steps(child);
            }
        }
        foreach (var e in pack.Events)
        {
            Conditions(e.When);
            Steps(e.Steps);
        }
        foreach (var r in pack.Rumors) Conditions(r.When);
    }

    /// <summary>
    /// このシナリオのモンスターの id を変える。階の顔ぶれ、最下層の主とほかのボスの組、イベントの戦闘の組、
    /// ほかのモンスターの extends、絵のファイル(art/monsters/&lt;id&gt;.png)も直す。
    /// </summary>
    public static void RenameMonster(ScenarioPack pack, string old, string id)
    {
        foreach (var o in pack.Monsters)
        {
            if ((string?)o["id"] == old) o["id"] = id;
            if ((string?)o["extends"] == old) o["extends"] = id;
        }
        foreach (var r in pack.Rosters)
            for (int k = 0; k < r.Count; k++) if (r[k] == old) r[k] = id;
        foreach (var g in pack.BossGroups ?? []) if (g.Monster == old) g.Monster = id;
        foreach (var b in pack.Bosses)
            foreach (var g in (b["groups"] as JsonArray)?.OfType<JsonObject>() ?? [])
                if ((string?)g["monster"] == old) g["monster"] = id;
        pack.RenameInWorldBossPicks(old, id);
        void Steps(List<EventStep> steps)
        {
            foreach (var s in steps)
            {
                foreach (var g in s.Groups) if (g.Monster == old) g.Monster = id;
                foreach (var child in s.Children()) Steps(child);
            }
        }
        foreach (var e in pack.Events) Steps(e.Steps);
        string art = Path.Combine(pack.Directory, "art", "monsters");
        if (File.Exists(Path.Combine(art, old + ".png")))
            File.Move(Path.Combine(art, old + ".png"), Path.Combine(art, id + ".png"), overwrite: true);
    }

    /// <summary>
    /// 階を消す。その階の顔ぶれ・世界の文・名のある敵は一緒に消し、下の階を指していたもの(名のある敵の階、テレポーターとシュートの行き先、
    /// エレベーターの縦穴の範囲)は1つ上へずらす。消した階を行き先にしていたテレポーターとシュートは、行き先を外す(検査で分かる)。
    /// </summary>
    public static void RemoveFloor(ScenarioPack pack, int floor)
    {
        if (floor < 0 || floor >= pack.Floors.Count) return;
        pack.Floors.RemoveAt(floor);
        if (floor < pack.Rosters.Count) pack.Rosters.RemoveAt(floor);
        if (floor < pack.Text.Floors.Count) pack.Text.Floors.RemoveAt(floor);
        if (pack.Named is { } named)
        {
            named.RemoveAll(n => n.Floor == floor);
            foreach (var n in named) if (n.Floor > floor) n.Floor--;
        }
        foreach (var f in pack.Floors)
            foreach (var c in f.AllCells())
                if (f[c].Target is { Floor: int t } target)
                    f[c].Target = t == floor ? null : t > floor ? target with { Floor = t - 1 } : target;
        // 縦穴の範囲(1 が B1): 消した階より下の端は1つ上へ。範囲が1階以下になった縦穴は消す
        var shafts = new List<ElevatorShaft>();
        int removed = floor + 1;
        foreach (var s in pack.Elevators)
        {
            int from = s.From > removed ? s.From - 1 : s.From, to = s.To >= removed ? s.To - 1 : s.To;
            if (to > from) shafts.Add(s with { From = from, To = to });
        }
        pack.SetElevators(shafts);
    }

    /// <summary>
    /// 階を from から to へ動かす(間の階はずれる)。顔ぶれ・世界の文・名のある敵の階、テレポーターとシュートの行き先も一緒に動かす。
    /// エレベーターの縦穴は深さ(何階から何階まで)で決まるので、そのまま(止まる階は、入れ替わった階で決め直す)。
    /// </summary>
    public static void MoveFloor(ScenarioPack pack, int from, int to)
    {
        int count = pack.Floors.Count;
        if (from < 0 || from >= count || to < 0 || to >= count || from == to) return;
        // 古い番号 → 新しい番号
        var order = Enumerable.Range(0, count).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);
        var map = new int[count];
        for (int i = 0; i < count; i++) map[order[i]] = i;
        foreach (var s in pack.Elevators) Maze.Elevators.Clear(pack.Floors, s);
        static List<T> Reorder<T>(List<T> list, List<int> order, Func<T> blank)
        {
            while (list.Count < order.Count) list.Add(blank());
            return order.Select(i => list[i]).Concat(list.Skip(order.Count)).ToList();
        }
        pack.Floors = order.Select(i => pack.Floors[i]).ToList();
        pack.Rosters = Reorder(pack.Rosters, order, () => []);
        if (pack.Text.Floors.Count > 0) pack.Text.Floors = Reorder(pack.Text.Floors, order, () => new Ai.WorldFloorText());
        foreach (var n in pack.Named ?? []) if (n.Floor >= 0 && n.Floor < count) n.Floor = map[n.Floor];
        foreach (var f in pack.Floors)
            foreach (var c in f.AllCells())
                if (f[c].Target is { Floor: int t } target && t >= 0 && t < count) f[c].Target = target with { Floor = map[t] };
        pack.SetElevators(pack.Elevators.ToList());
    }
}

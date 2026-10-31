using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Core.MazeGen;

/// <summary>
/// 全階を自動で歩くボット。ゲームと同じ MazeNavigator で、東西南北に1歩ずつ試し、階段とエレベーターを使い、
/// 鍵の印を踏んだら鍵を拾う(拾うまで錠は開けない。イベントで開く扉 gate: は開けられることにする)。到達の検査(ReachabilityValidator)が見ない、
/// 「鍵を取る前に錠の向こうへ行く必要がある」「回転床やテレポーターの順番で詰む」などを探す。
/// </summary>
public static class MazeBot
{
    public sealed record Report(int Standing, int Visited, bool ReachedBoss, bool CanReturn, List<string> Problems)
    {
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>イベントで開く扉(印 gate: のマスの錠)。ボットは仕掛けを解けたことにして通る。</summary>
    private static bool IsGate(MazeFloor f, GridPos cell, Direction d) =>
        f[cell].Mark?.StartsWith("gate:") == true || f.Neighbor(cell, d) is { } n && f[n].Mark?.StartsWith("gate:") == true;

    public static Report Explore(Dungeon dungeon)
    {
        var start = dungeon.Floors[0].FindFeature(CellFeature.StairsUp) ?? dungeon.Floors[0].Start;
        var state = new ExplorationState(dungeon, new PartyPosition(0, start, Direction.North));
        var nav = new MazeNavigator(state, new Rng.Pcg32(1, 1));
        var visited = new HashSet<(int Floor, GridPos Cell)>();
        var queue = new Queue<(int Floor, GridPos Cell)>();
        bool town = false;

        void Visit(PartyPosition p)
        {
            if (!visited.Add((p.Floor, p.Cell))) return;
            queue.Enqueue((p.Floor, p.Cell));
            var cell = dungeon.Floors[p.Floor][p.Cell];
            if (cell.Mark == "key" && state.Keys.Add(p.Floor))
            {
                // 鍵を拾ったら、その階で錠の前まで来ていた所から、もう一度試す
                foreach (var v in visited.Where(v => v.Floor == p.Floor)) queue.Enqueue(v);
            }
        }

        // 開けられなかった錠の辺(行けないマスの原因の見当に使う)
        var stuck = new HashSet<(int Floor, GridPos Cell, Direction Dir)>();
        Visit(state.Position);
        while (queue.Count > 0)
        {
            var (floor, cell) = queue.Dequeue();
            foreach (var d in DirectionExtensions.All)
            {
                state.Position = new PartyPosition(floor, cell, d);
                nav.Search();   // 隠し扉は、調べれば見つかる
                state.Position = new PartyPosition(floor, cell, d);
                var r = nav.Forward();
                if (r == MoveResult.Locked && (state.Keys.Contains(floor) || IsGate(dungeon.Floors[floor], cell, d)))
                {
                    state.Unlock(floor, cell, d);
                    state.Position = new PartyPosition(floor, cell, d);
                    r = nav.Forward();
                }
                if (r == MoveResult.Locked) stuck.Add((floor, cell, d));
                if (r is not (MoveResult.Bumped or MoveResult.Locked)) Visit(state.Position);
            }
            var here = dungeon.Floors[floor][cell];
            if (here.Feature is CellFeature.StairsUp or CellFeature.StairsDown)
            {
                state.Position = new PartyPosition(floor, cell, Direction.North);
                var r = nav.UseStairs();
                if (r == MoveResult.ExitToTown) town = true;
                else if (r == MoveResult.ChangedFloor) Visit(state.Position);
            }
            if (here.Feature == CellFeature.Elevator)
            {
                state.Position = new PartyPosition(floor, cell, Direction.North);
                foreach (int f in nav.ElevatorFloors().Where(f => f != floor))
                {
                    state.Position = new PartyPosition(floor, cell, Direction.North);
                    if (nav.TakeElevator(f) == MoveResult.TookElevator) Visit(state.Position);
                }
            }
        }

        var problems = new List<string>();
        int standing = 0;
        for (int f = 0; f < dungeon.Floors.Count; f++)
        {
            var fl = dungeon.Floors[f];
            var missed = fl.AllCells().Where(p => !fl[p].IsSolid && fl[p].Feature is not (CellFeature.Teleporter or CellFeature.Chute)
                && !visited.Contains((f, p))).ToList();
            standing += fl.AllCells().Count(p => !fl[p].IsSolid);
            if (missed.Count > 0)
            {
                // 行けないマスの手前で開けられなかった錠があれば、原因かもしれないと添える(門の印 gate: を外して L だけ残った、など)
                var missedSet = missed.ToHashSet();
                var locks = stuck.Where(s => s.Floor == f && fl.Neighbor(s.Cell, s.Dir) is { } n && missedSet.Contains(n))
                    .Select(s => $"({s.Cell.X},{s.Cell.Y}) {s.Dir.JaName()}").Distinct().Take(3).ToList();
                string hint = locks.Count > 0 ? $"。手前で開かない錠の辺(L)がある: {string.Join(" ", locks)}。門の印 gate: もその階の鍵 key も無い L が原因かもしれない" : "";
                problems.Add($"B{f + 1}: 歩いて行けないマスが {missed.Count} 個(例: {string.Join(" ", missed.Take(5).Select(p => $"({p.X},{p.Y})"))}){hint}");
            }
        }
        var last = dungeon.Floors.Count - 1;
        bool boss = !dungeon.Floors[last].AllCells().Any(p => dungeon.Floors[last][p].Mark?.StartsWith("boss:") == true)
            || dungeon.Floors[last].AllCells().Any(p => dungeon.Floors[last][p].Mark?.StartsWith("boss:") == true && visited.Contains((last, p)));
        if (!boss) problems.Add("最下層のボスのねぐらに行けません");
        if (!town) problems.Add("町へ戻る階段に行けません");
        return new Report(standing, visited.Count, boss, town, problems);
    }
}

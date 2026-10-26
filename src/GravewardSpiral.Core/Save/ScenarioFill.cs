using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// シナリオの中身を、自動で選んでパックに書き込む(シナリオエディタの「新しく作る」と「足りないところを自動で埋める」・「選び直す」)。
/// シナリオは出来上がった世界なので、ゲームが新しいゲームのたびに選ぶのではなく、ここで1回選んで書き込み、作者が気に入らないところを直す。
/// 選び方はゲームの新しい世界と同じ(GameSession の BuildRosters・PlaceNamed・PlaceEvents)。data はシナリオを重ねて読んだもの。
/// </summary>
public static class ScenarioFill
{
    [Flags]
    public enum Parts { None = 0, Rosters = 1, Named = 2, Events = 4, All = Rosters | Named | Events }

    /// <summary>
    /// what の中身を選んで pack に書き込む。redo でなければ、まだ決めていないところ(空の顔ぶれの階、Named が null、EventsPlaced でない)だけ。
    /// redo なら、決めてあるものも選び直す(イベントは、散らすイベントの印を消してから置き直す。手で置いた散らさないイベントの印は残す)。
    /// 何を埋めたかを返す。
    /// </summary>
    public static Parts Fill(ScenarioPack pack, GameData data, ContentLibrary content, ulong seed, Parts what, bool redo = false)
    {
        var done = Parts.None;
        var dungeon = new Dungeon(pack.Id, pack.Floors);
        var session = GameSession.New(0, DungeonLoader.RequestFor(dungeon, seed), dungeon);
        var enc = Combat.EncounterSettings.Load(content);
        enc.FirstLevel = pack.FirstLevel;
        enc.LastLevel = pack.LastLevel;
        session.BuildRosters(data, enc);   // 顔ぶれと名のある敵を、この種で選ぶ
        if (what.HasFlag(Parts.Rosters))
        {
            for (int i = 0; i < pack.Floors.Count; i++)
            {
                var roster = pack.RosterOf(i);
                if (roster.Count > 0 && !redo) continue;
                roster.Clear();
                roster.AddRange(session.FloorRosters[i]);
                done |= Parts.Rosters;
            }
        }
        if (what.HasFlag(Parts.Named) && (pack.Named == null || redo))
        {
            pack.Named = session.Named.Select(n => new Monsters.NamedSpawn { Id = n.Id, Floor = n.Floor, Lair = n.Lair, X = n.X, Y = n.Y, Rumored = n.Rumored }).ToList();
            done |= Parts.Named;
        }
        if (what.HasFlag(Parts.Events) && (!pack.EventsPlaced || redo))
        {
            if (redo) ClearScattered(pack, data);
            // 散らすのは迷宮の階そのもの(pack.Floors)。標準のイベントを混ぜるかは、シナリオの設定(manifest の standard_events)
            Func<EventDef, bool>? allow = pack.Manifest.StandardEvents ? null : e => pack.Events.Any(x => x.Id == e.Id);
            session.PlaceEvents(data, MazeGenSettings.Load(content), enc, allow);
            pack.EventsPlaced = true;
            done |= Parts.Events;
        }
        return done;
    }

    /// <summary>散らすイベント(scatter のあるもの)の印を、迷宮から消す。消した数。</summary>
    public static int ClearScattered(ScenarioPack pack, GameData data)
    {
        int n = 0;
        foreach (var f in pack.Floors)
            foreach (var p in f.AllCells())
                if (f[p].Mark is { } m && m.StartsWith(EventPlacer.MarkPrefix) && data.Events.GetValueOrDefault(m[EventPlacer.MarkPrefix.Length..])?.Scatter != null)
                {
                    f[p].Mark = null;
                    n++;
                }
        return n;
    }
}

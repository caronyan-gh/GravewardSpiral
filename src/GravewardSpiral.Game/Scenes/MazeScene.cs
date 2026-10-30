using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 迷宮を歩く画面。左に線画の 3D 表示、右上にオートマップの小窓、その下に階の名前とメッセージ、一番下にパーティの一覧。
/// 操作: 上=前進 / 下=後退 / 左右=回転 / 決定=階段を使う・正面を調べる / 地図=全体の地図 / 取消=戻る。
/// パーティがいれば、歩くたびに遭遇を判定して戦闘の画面を積む。
/// </summary>
public sealed class MazeScene : Scene
{
    private static readonly RectangleF ViewArea = new(24, 24, 812, 540);
    private static readonly RectangleF MiniMapArea = new(860, 24, 396, 330);
    private static readonly RectangleF InfoArea = new(860, 366, 396, 198);
    private static readonly RectangleF PartyArea = new(24, 590, 1232, 106);
    private const int MiniMapRadius = 5;
    private const int MessageLines = 4;

    private readonly MazeNavigator _nav;
    private readonly List<string> _messages = [];
    private GridPos _lastKnown;
    private int _lastKnownFloor;
    private double _time;

    /// <param name="seed">迷宮のシード(回転床の乱数に使う)。</param>
    public MazeScene(ExplorationState state, ulong seed = 0)
    {
        _nav = new MazeNavigator(state, Core.Rng.RngStreams.Create(seed, "maze/spinner"));
        _lastKnown = state.Position.Cell;
        _lastKnownFloor = state.Position.Floor;
    }

    private ExplorationState State => _nav.State;

    public const string TestDungeonId = "devtest";

    /// <summary>迷宮の BGM(「Tectonic Patience」。低音だけで盛り上がらない曲)。ほかの曲より小さく流す。</summary>
    public const string Bgm = "audio/bgm/maze.mp3";
    public const float BgmGain = 0.6f;

    /// <summary>試しの迷宮(devtest パック)の B1 の着く場所から始める。</summary>
    public static MazeScene StartTestDungeon(GameServices services)
    {
        var dungeon = DungeonLoader.Load(services.Content, TestDungeonId);
        var b1 = dungeon.Floors[0];
        return new MazeScene(new ExplorationState(dungeon, new PartyPosition(0, b1.Start, b1.StartFacing)));
    }

    public override void Enter()
    {
        _lastSteps = State.Steps;
        Say(Services.Strings.Format("maze.enter", FloorLabel()));
        SayFloorEnter();
        _version = Stack.Version;
        // 戦闘の途中でやめていたら、その戦闘から始め直す(厳しいセーブ)
        if (Services.Session?.PendingCombat is { } pending)
        {
            var groups = pending.Groups.Where(g => Services.Data.Monsters.ContainsKey(g.Monster))
                .Select(g => (Services.Data.Monsters[g.Monster], g.Count)).ToList();
            Say(Services.Strings["maze.resume_combat"]);
            StartEncounter(groups, new MonsterScale(pending.HpMult, pending.DamageMult, pending.AcBonus, pending.HitBonus));
            return;
        }
        AfterMove();
    }

    private int _version;

    /// <summary>上に積んだ画面から戻ったところ。移動のキーを押し直すまで歩かない(戦闘のあとに前進が入りっぱなしにならないように)。</summary>
    private bool _waitMoveRelease;

    private static readonly GameAction[] MoveActions = [GameAction.Up, GameAction.Down, GameAction.StrafeLeft, GameAction.StrafeRight];

    /// <summary>厳しいセーブの難易度なら、今の状態を保存する(上に積んだ画面から戻ったとき・階を移ったとき)。</summary>
    private void AutoSave()
    {
        if (Services.Session is { Difficulty.StrictSave: true } && Services.Session.Slot != 0) Services.SaveSession();
    }

    private long _lastSteps;
    private readonly Queue<string> _dueTimers = new();

    /// <summary>
    /// 歩数のタイマー(イベントの手順 timer): 歩いた分(休んだ分も)だけ進め、時が来たイベントを、今いるマスで起こす。
    /// いくつも来たら、1つずつ(前のイベントが閉じてから)。起こしたら true。
    /// </summary>
    private bool RunTimers()
    {
        if (Services.Session is not { } session) return false;
        long walked = State.Steps - _lastSteps;
        _lastSteps = State.Steps;
        if (walked > 0) foreach (var id in session.AdvanceTimers((int)Math.Min(walked, int.MaxValue))) _dueTimers.Enqueue(id);
        while (_dueTimers.TryDequeue(out var due))
        {
            if (!Services.Data.Events.TryGetValue(due, out var def)) continue;
            var ctx = EventCtx(turned: false, null, false);
            if (!EventRunner.Available(def, ctx)) continue;
            Stack.Push(new EventScene(new EventRunner(def, ctx), EventCallbacks()));
            return true;
        }
        return false;
    }

    public override void Update(GameTime time, InputState input)
    {
        _time += time.ElapsedGameTime.TotalSeconds;
        if (RunTimers()) return;
        // 上に積んだ画面(戦闘・宝箱・キャンプ・イベントなど)から戻ってきたら保存する
        if (_version != Stack.Version)
        {
            _version = Stack.Version;
            AutoSave();
            _waitMoveRelease = true;
        }
        // 戻ってきたあとは、移動のキーを全部放すか、新しく押すまで、押しっぱなしでは歩かない
        if (_waitMoveRelease && (!MoveActions.Any(input.Held) || MoveActions.Any(input.Pressed))) _waitMoveRelease = false;
        // 休憩中に来た敵(キャンプが閉じてから戦う)
        if (_ambushPending)
        {
            _ambushPending = false;
            MaybeEncounter(force: true);
            return;
        }
        if (Services.Debug && DebugKeys()) return;
        bool walk = !_waitMoveRelease;   // 押しっぱなしのまま戻ってきたあいだは、歩かない(回転やメニューはできる)
        bool backTurns = BackTurns(input);
        if (walk && input.Repeat(GameAction.Up))
        {
            bool door = ThroughDoor(State.Position.Facing);   // 動く前に、これから通る辺を見る
            _moveDir = State.Position.Facing;
            _stepFrom = State.Position.Cell;
            _entered = "forward";
            Report(_nav.Forward(), door);
        }
        else if (backTurns && input.Pressed(GameAction.Down)) Turned(_nav.TurnAround());
        else if (!backTurns && walk && input.Repeat(GameAction.Down))
        {
            bool door = ThroughDoor(State.Position.Facing.Opposite());
            _moveDir = State.Position.Facing.Opposite();
            _stepFrom = State.Position.Cell;
            _entered = "back";
            Report(_nav.Backward(), door);
        }
        else if (walk && (input.Repeat(GameAction.StrafeLeft) || input.Repeat(GameAction.StrafeRight)))
        {
            var dir = input.Repeat(GameAction.StrafeLeft) ? State.Position.Facing.Left() : State.Position.Facing.Right();
            bool door = ThroughDoor(dir);
            _moveDir = dir;
            _stepFrom = State.Position.Cell;
            _entered = dir == State.Position.Facing.Left() ? "left" : "right";
            Report(dir == State.Position.Facing.Left() ? _nav.StrafeLeft() : _nav.StrafeRight(), door);
        }
        else if (input.Pressed(GameAction.Left)) Turned(_nav.TurnLeft());
        else if (input.Pressed(GameAction.Right)) Turned(_nav.TurnRight());
        else if (input.Pressed(GameAction.Confirm)) UseOrSearch();
        else if (input.Pressed(GameAction.Map) && !Services.Options.AllowMap) Say(Services.Strings["maze.no_map"]);
        else if (input.Pressed(GameAction.Map))
        {
            Services.Audio.PlaySe(Sounds.MapOpen);
            Stack.Push(new MapScene(State, _lastKnown, InFog ? null : State.Position));
        }
        else if (input.Pressed(GameAction.Menu) && Services.Session != null)
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            OpenCamp();
        }
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            var s = Services.Strings;
            if (Services.Session != null) Stack.Push(new SystemMenuScene(s["system.suspend"], Suspend, (s["system.camp"], OpenCamp)));
            else Stack.Push(new SystemMenuScene(s["system.leave"], Suspend));
        }
        UpdateLastKnown();
    }

    private bool InFog => State.Cell.Zone.HasFlag(Zone.Fog);

    /// <summary>後ろのキーで振り向くか(設定で「振り向く」なら振り向き、Shift か L2 を押しながらだと逆)。</summary>
    private bool BackTurns(InputState input) => (Services.Options.BackKey == "turn") != input.Held(GameAction.Reverse);

    private void OpenCamp() => Stack.Push(new CampScene(RestInterrupted, () =>
    {
        Services.Audio.PlaySe(Sounds.Stairs);
        Stack.Replace(new TownScene());
    }, CampEvent));

    /// <summary>キャンプで唱えた・休んだとき、このマスのイベントがそれで起こるなら、始める処理を返す。</summary>
    private Action? CampEvent(string? cast, bool rested)
    {
        if (Services.Session == null || State.Cell.Mark is not { } mark || !mark.StartsWith(EventPlacer.MarkPrefix)) return null;
        if (!Services.Data.Events.TryGetValue(mark[EventPlacer.MarkPrefix.Length..], out var ev)) return null;
        if (!(cast != null && ev.OnCast || rested && ev.OnRest)) return null;
        var ctx = EventCtx(turned: false, cast, rested);
        if (State.IsNoted(ctx.At!.Value.Floor, ctx.At.Value.Cell, ev.Id) || !EventRunner.Available(ev, ctx)) return null;
        return () => Stack.Push(new EventScene(new EventRunner(ev, ctx), EventCallbacks()));
    }

    /// <summary>中断: セーブしてタイトルへ(試しの迷宮では、セーブせずにタイトルへ)。</summary>
    private void Suspend()
    {
        var s = Services.Strings;
        bool hasSession = Services.Session != null;
        Stack.Push(new ConfirmScene(s[hasSession ? "maze.confirm_suspend" : "maze.confirm_leave"], () =>
        {
            if (hasSession)
            {
                Services.SaveSession();
                Services.Session = null;
            }
            Stack.Replace(new TitleScene());
        }));
    }

    /// <summary>その向きに進むと扉を通るか(足音を扉の音にするため)。</summary>
    private bool ThroughDoor(Direction d) => MazeView.LooksLikeDoor(State.Apparent(State.Position.Floor, State.Position.Cell, d));   // 一方通行の扉も扉の音

    private void UpdateLastKnown()
    {
        if (InFog) return;
        _lastKnown = State.Position.Cell;
        _lastKnownFloor = State.Position.Floor;
    }

    private Direction _moveDir;
    private GridPos _stepFrom;

    /// <summary>このマスへどう動いて入ったか(イベントの条件 entered)。歩いて来たのでなければ null。</summary>
    private string? _entered;

    /// <summary>今の階の、罠や錠の強さの段階(目安のレベル / 4)。</summary>
    private int Tier
    {
        get
        {
            _encounters ??= Services.Encounters();
            int floors = State.Dungeon.Floors.Count;
            double depth = floors <= 1 ? 0 : (double)State.Position.Floor / (floors - 1);
            return (int)(_encounters.TargetLevel(depth) / 4);
        }
    }

    private void UseOrSearch()
    {
        if (State.Cell.Feature == CellFeature.Elevator)
        {
            // スイッチ待ちの縦穴は動かない(シナリオの dungeons.json の elevators の power)
            if (DungeonLoader.ScenarioDef(Services.Content)?.Elevators is { } shafts && Services.Session is { } session
                && Elevators.Unpowered(shafts, State.Cell, session.Flags) != null)
            {
                Say(Services.Strings["elevator.no_power"]);
                return;
            }
            Stack.Push(new ElevatorScene(_nav, r => Report(r)));
            return;
        }
        if (State.Cell.Feature is CellFeature.StairsDown or CellFeature.StairsUp)
        {
            string key = State.Cell.Feature == CellFeature.StairsDown ? "maze.confirm_down" : "maze.confirm_up";
            Stack.Push(new ConfirmScene(Services.Strings[key], () => Report(_nav.UseStairs())));
            return;
        }
        if (Services.Session?.BodiesAt(State.Position) is { Count: > 0 } bodies)
        {
            var session = Services.Session;
            Stack.Push(new ConfirmScene(Services.Strings.Format("maze.confirm_bodies", Services.Strings.List(bodies.Select(c => c.Name))), () =>
            {
                var taken = session.RecoverBodies(State.Position);
                Say(taken.Count > 0 ? Services.Strings.Format("maze.bodies_taken", Services.Strings.List(taken.Select(c => c.Name)))
                    : Services.Strings["maze.bodies_full"]);
            }));
            return;
        }
        Report(_nav.Search());
    }

    private void Report(MoveResult result, bool throughDoor = false)
    {
        // 歩いて着いたときだけ、どう動いて入ったかを残す(階段・テレポーター・落とし穴などで来たら、どれでもない)
        if (result != MoveResult.Moved) _entered = null;
        var s = Services.Strings;
        var audio = Services.Audio;
        switch (result)
        {
            case MoveResult.Bumped:
                audio.PlaySe(Sounds.Bump);
                Say(s["maze.bump"]);
                break;
            case MoveResult.Locked:
                audio.PlaySe(Sounds.Bump);
                if (IsGate(_moveDir))
                {
                    // 仕掛けで開く扉は、鍵や鍵開けでは開かない
                    Say(s["maze.sealed"]);
                    break;
                }
                if (Services.Session == null)
                {
                    Say(s["lock.found"]);
                    break;
                }
                Stack.Push(new LockScene(State, _moveDir, Tier, () => Say(s["lock.opened"]), () =>
                {
                    Say(s["lock.noise"]);
                    MaybeEncounter(force: true);
                }));
                break;
            case MoveResult.TookElevator:
                audio.PlaySe(Sounds.Stairs);
                Say(s.Format("maze.changed_floor", FloorLabel()));
                SayFloorEnter();
                AfterMove();
                break;
            case MoveResult.FoundSecretDoor:
                audio.PlaySe(Sounds.SecretFound);
                Say(s["maze.found_secret"]);
                break;
            case MoveResult.NothingFound:
                audio.PlaySe(Sounds.Search);
                Say(s["maze.nothing"]);
                break;
            case MoveResult.ChangedFloor:
                audio.PlaySe(Sounds.Stairs);
                Say(s.Format("maze.changed_floor", FloorLabel()));
                SayFloorEnter();
                AfterMove();
                AutoSave();
                break;
            case MoveResult.ExitToTown:
                audio.PlaySe(Sounds.Stairs);
                // 町へ(試しの迷宮には町が無いので、タイトルへ)
                if (Services.Session != null) Stack.Replace(new TownScene());
                else Stack.Replace(new TitleScene());
                break;
            case MoveResult.Moved:
                audio.PlaySe(throughDoor ? Sounds.Door : Sounds.Step);
                AfterMove();
                PoisonStep();
                if (!RoomEncounter()) MaybeEncounter();
                break;
            case MoveResult.Teleported:
                audio.PlaySe(Sounds.Step);
                Say(s["maze.teleported"]);
                AfterMove();
                break;
            case MoveResult.FellIntoPit:
                audio.PlaySe(Sounds.Bump);
                PitDamage();
                AfterMove();
                break;
            case MoveResult.FellDownChute:
                audio.PlaySe(Sounds.Stairs);
                Say(s.Format("maze.chute", FloorLabel()));
                SayFloorEnter();
                AfterMove();
                break;
        }
    }

    private Zone _lastZone = Zone.None;

    /// <summary>着いたマスの様子を知らせる(暗闇・霧に入った、階段がある)。</summary>
    private void AfterMove()
    {
        var s = Services.Strings;
        RollRoomsIfNew();
        var zone = State.Cell.Zone;
        if (zone != _lastZone)
        {
            if (zone.HasFlag(Zone.Dark) && !_lastZone.HasFlag(Zone.Dark)) Say(s[State.InDark ? "maze.enter_dark" : "maze.enter_dark_lit"]);
            if (zone.HasFlag(Zone.Fog) && !_lastZone.HasFlag(Zone.Fog)) Say(s["maze.enter_fog"]);
            _lastZone = zone;
        }
        CheckMark();
        if (Services.Session?.NamedLairAt(State.Position.Floor, State.Position.Cell) is { } lair) NamedFight(lair);
        if (Services.Session?.BodiesAt(State.Position) is { Count: > 0 } bodies)
            Say(s.Format("maze.bodies", Services.Strings.List(bodies.Select(c => c.Name))));
        if (State.Cell.Feature == CellFeature.Elevator) Say(s["maze.elevator"]);
        if (State.Cell.Feature == CellFeature.StairsDown) Say(s["maze.stairs_down"]);
        else if (State.Cell.Feature == CellFeature.StairsUp) Say(s[State.Position.Floor == 0 ? "maze.stairs_up_town" : "maze.stairs_up"]);
    }

    private EncounterSettings? _encounters;

    /// <summary>落とし穴: 全員がダメージを受ける(1 より下にはならない)。フェザー・フォールがあれば何も無い。</summary>
    private void PitDamage()
    {
        var s = Services.Strings;
        if (State.FeatherMinutes > 0)
        {
            Say(s["maze.pit_feather"]);
            return;
        }
        Say(s["maze.pit"]);
        if (Services.Session == null) return;
        var rng = Services.Session.Rng[RngStreams.Names.Loot];
        foreach (var c in Services.Session.Roster.PartyMembers.Where(c => c.IsAlive))
        {
            int dmg = Core.Rules.DiceExpression.Parse($"{1 + Tier}d6").Roll(rng).Total;
            c.Hp = Math.Max(1, c.Hp - dmg);
        }
    }

    /// <summary>向きを変えた。このマスのイベントが向きを変えたときに起こるもの(trigger が turn / both)なら始める。</summary>
    private void Turned(MoveResult result)
    {
        if (result != MoveResult.Turned || Services.Session == null) return;
        if (State.Cell.Mark is { } mark && mark.StartsWith(EventPlacer.MarkPrefix)
            && Services.Data.Events.TryGetValue(mark[EventPlacer.MarkPrefix.Length..], out var ev) && ev.OnTurn) StartEvent(ev, turned: true);
    }

    /// <summary>着いたマスの印(鍵を拾う、メッセージ)。一度で済むものは済んだと覚える。</summary>
    private void CheckMark()
    {
        var s = Services.Strings;
        var mark = State.Cell.Mark;
        if (mark == null) return;
        var here = (State.Position.Floor, State.Position.Cell);
        if (mark == "key" && !State.UsedMarks.Contains(here))
        {
            State.UsedMarks.Add(here);
            State.Keys.Add(State.Position.Floor);
            Services.Audio.PlaySe(Sounds.SecretFound);
            Say(s["maze.key_found"]);
        }
        else if (mark.StartsWith("message:")) Say(s[mark["message:".Length..]]);
        else if (mark.StartsWith(EventPlacer.MarkPrefix) && Services.Session != null
            && Services.Data.Events.TryGetValue(mark[EventPlacer.MarkPrefix.Length..], out var ev) && ev.OnEnter) StartEvent(ev);
        else if (mark.StartsWith("boss:") && !State.UsedMarks.Contains(here) && Services.Session != null
            && Services.Session.Boss(Services.Data, mark["boss:".Length..]) is { } boss)
        {
            // 決まった場所の敵。倒すまで、来るたびに戦う
            if (boss.IntroText != null) Say(Services.Name(boss.IntroText));
            else if (boss.Intro != null) Say(s[boss.Intro]);
            var groups = boss.Fighters(Services.Data.Monsters);
            Stack.Push(new CombatScene(groups, noMagic: State.Cell.Zone.HasFlag(Zone.AntiMagic), scale: boss.Scale.Times(Services.Session!.FixedScale(_encounters ??= Services.Encounters())), bgm: CombatScene.BossBgm, onEnd: outcome =>
            {
                if (outcome != CombatOutcome.Victory) return;
                State.UsedMarks.Add(here);
                if (boss.RewardFlag != null) Services.Session!.Flags.Add(boss.RewardFlag);
                if (boss.VictoryText != null) Say(Services.Name(boss.VictoryText));
                else if (boss.Victory != null) Say(s[boss.Victory]);
                MaybeChest(Depth, (_encounters ??= Services.Encounters()).TargetLevel(Depth), groups, randomChest: false, boss: boss.Id);
            }));
        }
    }

    /// <summary>今いるマスか、dir の隣のマスが、仕掛けで開く扉(印 gate:)か。</summary>
    private bool IsGate(Direction dir)
    {
        var f = State.Floor;
        bool Gate(GridPos p) => f[p].Mark?.StartsWith("gate:") == true;
        return Gate(State.Position.Cell) || f.Neighbor(State.Position.Cell, dir) is { } n && Gate(n);
    }

    private double Depth
    {
        get
        {
            int floors = State.Dungeon.Floors.Count;
            return floors <= 1 ? 0 : (double)State.Position.Floor / (floors - 1);
        }
    }

    /// <summary>このマスのイベントを始める(条件が合わない・済んだものは何もしない)。turned は向きを変えて起こしたとき。</summary>
    private void StartEvent(EventDef def, bool turned = false)
    {
        var ctx = EventCtx(turned, null, false);
        // 書き留めた場所では起こさず、見覚えがあることだけを言う(メモの最初の文を添えて)
        if (State.Notes.FirstOrDefault(n => n.Floor == ctx.At.Value.Floor && n.Cell == ctx.At.Value.Cell && n.EventId == def.Id) is { } note)
        {
            // 向きを変えるたびに言うとうるさいので、向きを変えたときは黙っている
            if (turned) return;
            string first = note.Lines.Count > 0 ? Services.Name(note.Lines[0]) : "";
            Say(Services.Strings.Format("memo.recall", first));
            return;
        }
        if (!EventRunner.Available(def, ctx)) return;
        Stack.Push(new EventScene(new EventRunner(def, ctx), EventCallbacks()));
    }

    private EventScene.Callbacks EventCallbacks() => new(EventCombat, () => Stack.Replace(new TownScene()), Warp: Warped, Alarm: Alarmed);

    /// <summary>イベントの警報(手順 alarm か、イベントの罠): この階の敵が襲ってくる。</summary>
    private void Alarmed()
    {
        Say(Services.Strings["maze.alarm"]);
        MaybeEncounter(force: true);
    }

    /// <summary>このマスのイベントの場の情報。</summary>
    private EventContext EventCtx(bool turned, string? cast, bool rested)
    {
        var session = Services.Session!;
        _encounters ??= Services.Encounters();
        _treasure ??= TreasureSettings.Load(Services.Content);
        return new EventContext
        {
            Data = Services.Data,
            Session = session,
            Rng = session.Rng[RngStreams.Names.Loot],
            Maze = State,
            At = (State.Position.Floor, State.Position.Cell),
            Depth = Depth,
            TargetLevel = _encounters.TargetLevel(Depth),
            Treasure = _treasure,
            Turned = turned,
            Entered = turned || cast != null || rested ? null : _entered,
            Cast = cast,
            Rested = rested,
        };
    }

    /// <summary>イベントで別のマスへ移された(手順 warp)。着いたマスの様子を知らせる(そこのイベントも起こる)。</summary>
    private void Warped(Core.Events.EventWarp warp)
    {
        _entered = null;
        Services.Audio.PlaySe(warp.ChangedFloor ? Sounds.Stairs : Sounds.Step);
        if (warp.ChangedFloor)
        {
            Say(Services.Strings.Format("maze.changed_floor", FloorLabel()));
            SayFloorEnter();
        }
        else Say(Services.Strings["maze.teleported"]);
        AfterMove();
        UpdateLastKnown();
    }

    /// <summary>イベントの戦闘。決めた敵か、ボスか、この階の敵。</summary>
    private void EventCombat(Core.Events.EventCombat fight, Action<bool> after)
    {
        var session = Services.Session!;
        _encounters ??= Services.Encounters();
        var data = Services.Data;
        List<(Core.Monsters.MonsterDef, int)> groups;
        MonsterScale? scale;
        string? bgm = null;
        if (fight.Boss != null && session.Boss(data, fight.Boss) is { } boss)
        {
            groups = boss.Fighters(data.Monsters);
            scale = boss.Scale.Times(session.FixedScale(_encounters));
            bgm = CombatScene.BossBgm;
        }
        else if (fight.Groups.Count > 0)
        {
            groups = fight.Groups.Where(g => data.Monsters.ContainsKey(g.Monster)).Select(g => (data.Monsters[g.Monster], g.Count)).ToList();
            scale = Encounters.Scale(_encounters, _encounters.TargetLevel(Depth)).Times(session.FixedScale(_encounters));
        }
        else
        {
            var e = Encounters.Build(data, _encounters, Depth, session.Roster.Party.Count, session.Rng[RngStreams.Names.Encounter], session.RosterOf(State.Position.Floor));
            groups = e.Groups.ToList();
            scale = e.Scale;
        }
        State.StepsSinceCombat = 0;
        Stack.Push(new CombatScene(groups, noMagic: State.Cell.Zone.HasFlag(Zone.AntiMagic), scale: scale, bgm: bgm,
            onEnd: outcome => after(outcome == CombatOutcome.Victory)));
    }

    /// <summary>1歩ぶん、残っている状態の時間を進める。毒なら HP が減る。</summary>
    private void PoisonStep()
    {
        if (Services.Session == null) return;
        var hurt = Lasting.Step(Services.Session.Roster.PartyMembers);
        if (hurt.Count > 0) Say(Services.Strings.Format("maze.poison", Services.Strings.List(hurt.Select(c => c.Name))));
    }

    /// <summary>キャンプの小休憩のあと: 敵が来たら戦闘を始めて true。</summary>
    private bool RestInterrupted()
    {
        if (Services.Session == null) return false;
        _encounters ??= Services.Encounters();
        if (!Encounters.RestCheck(_encounters, Services.Session.Rng[RngStreams.Names.Encounter])) return false;
        Say(Services.Strings["camp.ambush"]);
        // 戦闘は、キャンプの画面が閉じて迷宮に戻ってから始める(ここで積むと、キャンプが閉じるときに戦闘の画面を閉じてしまう)
        _ambushPending = true;
        return true;
    }

    private bool _ambushPending;

    /// <summary>この階に入ったばかりなら、部屋ごとに敵がいるかを抽選する。</summary>
    private void RollRoomsIfNew()
    {
        if (Services.Session is not { } session || State.RoomsFloor == State.Position.Floor) return;
        _encounters ??= Services.Encounters();
        State.RollRooms(session.Rng[RngStreams.Names.Encounter], _encounters.RoomChance);
    }

    /// <summary>敵のいる部屋に踏み込んだら戦う(Wizardry の玄室)。戦ったら true。</summary>
    private bool RoomEncounter()
    {
        var session = Services.Session;
        if (session == null || State.EnteredOccupiedRoom(_stepFrom) is not int room) return false;
        if (!session.Roster.PartyMembers.Any(c => c.State == LifeState.Alive && c.Hp > 0)) return false;
        _encounters ??= Services.Encounters();
        Say(Services.Strings["maze.room_occupied"]);
        var e = Encounters.Build(Services.Data, _encounters, Depth, session.Roster.Party.Count, session.Rng[RngStreams.Names.Encounter], session.RosterOf(State.Position.Floor));
        StartEncounter(e.Groups, e.Scale, room);
        return true;
    }

    /// <summary>この1歩で敵に出会うか。出会ったら戦闘へ。</summary>
    private void MaybeEncounter(bool force = false)
    {
        var session = Services.Session;
        if (session == null || !session.Roster.PartyMembers.Any(c => c.State == LifeState.Alive && c.Hp > 0)) return;
        _encounters ??= Services.Encounters();
        var rng = session.Rng[RngStreams.Names.Encounter];
        if (!force && !Encounters.Check(_encounters, State.StepsSinceCombat, rng)) return;
        int floors = State.Dungeon.Floors.Count;
        double depth = floors <= 1 ? 0 : (double)State.Position.Floor / (floors - 1);
        // さまよう名のある敵は、その階の遭遇にまれに混ざる
        if (session.NamedWandering(State.Position.Floor) is { } wanderer && rng.NextDouble() < Core.Monsters.NamedMonsterDef.WanderChance)
        {
            NamedFight(wanderer);
            return;
        }
        var e = Encounters.Build(Services.Data, _encounters, depth, session.Roster.Party.Count, rng, session.RosterOf(State.Position.Floor));
        StartEncounter(e.Groups, e.Scale);
    }

    /// <summary>名のある敵と戦う(1体だけ。倒すと item_chance で決まった品を落とす)。</summary>
    private void NamedFight(Core.Monsters.NamedSpawn spawn)
    {
        var session = Services.Session!;
        if (!Services.Data.NamedMonsters.TryGetValue(spawn.Id, out var def) || !Services.Data.Monsters.TryGetValue(def.Monster, out var baseDef)) return;
        if (!session.Roster.PartyMembers.Any(c => c.State == LifeState.Alive && c.Hp > 0)) return;
        _encounters ??= Services.Encounters();
        // 元の敵を写して、名前と経験値を変える
        var named = baseDef.WithName(def.Name);
        named.Xp = baseDef.Xp * 3;
        Say(Services.Strings.Format("maze.named_appears", Services.Name(def.Name)));
        double depth = Depth;
        State.StepsSinceCombat = 0;
        var groups = new List<(Core.Monsters.MonsterDef, int)> { (named, 1) };
        var scale = new MonsterScale(def.HpMult, def.DamageMult, def.AcBonus, def.HitBonus) { Boss = true };   // 名のある敵はボスの扱い
        Stack.Push(new CombatScene(groups, noMagic: State.Cell.Zone.HasFlag(Zone.AntiMagic), scale: scale, bgm: CombatScene.BossBgm, onEnd: outcome =>
        {
            if (outcome != CombatOutcome.Victory) return;
            spawn.Defeated = true;
            Say(Services.Strings.Format("maze.named_defeated", Services.Name(def.Name)));
            _treasure ??= TreasureSettings.Load(Services.Content);
            var lootRng = session.Rng[RngStreams.Names.Loot];
            var prize = lootRng.NextDouble() < def.ItemChance && Services.Data.Items.TryGetValue(def.Item, out var item)
                ? Loot.Make(Services.Data, _treasure, item, _encounters.TargetLevel(depth), lootRng) : null;
            MaybeChest(depth, _encounters.TargetLevel(depth), groups, randomChest: true, extra: prize == null ? null : [prize]);
        }));
    }

    /// <summary>
    /// 歩いていて出会った敵と戦う。厳しいセーブなら、始める前に「続きの戦闘」として保存する
    /// (戦闘の途中でやめても、続きからこの戦闘を始め直す。やり直しで逃れられない)。
    /// </summary>
    private void StartEncounter(IReadOnlyList<(Core.Monsters.MonsterDef Def, int Count)> groups, MonsterScale? scale, int? room = null)
    {
        var session = Services.Session!;
        _encounters ??= Services.Encounters();
        double depth = Depth;
        State.StepsSinceCombat = 0;
        if (session.Difficulty.StrictSave)
        {
            var sc = scale ?? MonsterScale.None;
            session.PendingCombat = new PendingCombat
            {
                Groups = groups.Select(g => new PendingCombat.PendingGroup { Monster = g.Def.Id, Count = g.Count }).ToList(),
                HpMult = sc.HpMult, DamageMult = sc.DamageMult, AcBonus = sc.AcBonus, HitBonus = sc.HitBonus,
            };
            AutoSave();
        }
        Stack.Push(new CombatScene(groups, noMagic: State.Cell.Zone.HasFlag(Zone.AntiMagic), scale: scale, onEnd: outcome =>
        {
            session.PendingCombat = null;
            if (outcome == CombatOutcome.Victory)
            {
                Say(Services.Strings["maze.after_victory"]);
                if (room is int r) State.OccupiedRooms.Remove(r);   // 部屋は空になる(逃げたら、いたまま)
                MaybeChest(depth, _encounters.TargetLevel(depth), groups, room: room != null);
            }
            else if (outcome == CombatOutcome.Fled) Say(Services.Strings["maze.after_flee"]);
        }));
    }

    private TreasureSettings? _treasure;

    /// <summary>
    /// 勝ったあと、宝箱が出るか。倒した敵の落とし物があれば必ず出て、中身に落とし物が入る。
    /// randomChest が false なら、落とし物があるときだけ出る(ボス)。出たら宝箱の画面を積む。
    /// </summary>
    private void MaybeChest(double depth, double targetLevel, IEnumerable<(Core.Monsters.MonsterDef Def, int Count)> defeated, bool randomChest = true,
        string? boss = null, bool room = false, IEnumerable<ItemInstance>? extra = null)
    {
        var session = Services.Session!;
        _treasure ??= TreasureSettings.Load(Services.Content);
        var rng = session.Rng[RngStreams.Names.Loot];
        var drops = Loot.RollDrops(Services.Data, _treasure, defeated, targetLevel, rng, boss, session.DropBoostOf(Services.Data, State.Position.Floor));
        if (extra != null) drops.AddRange(extra);   // 名のある敵の品(当たったとき)
        if (drops.Count == 0 && (!randomChest || rng.NextDouble() >= (room ? _treasure.RoomChestChance : _treasure.ChestChance))) return;
        var chest = Chest.Generate(Services.Data, _treasure, depth, targetLevel, rng, drops);
        Stack.Push(new ChestScene(chest, TeleportRandom, () =>
        {
            Say(Services.Strings["maze.alarm"]);
            MaybeEncounter(force: true);
        }));
    }

    /// <summary>テレポーターの罠: 同じ階の、岩盤でない別のマスへ飛ばされる。</summary>
    private void TeleportRandom()
    {
        var floor = State.Floor;
        var rng = Services.Session!.Rng[RngStreams.Names.Loot];
        var cells = new List<GridPos>();
        for (int y = 0; y < floor.Height; y++)
        for (int x = 0; x < floor.Width; x++)
        {
            if (!floor[x, y].IsSolid) cells.Add(new GridPos(x, y));
        }
        var to = cells[rng.Next(0, cells.Count)];
        State.Position = new PartyPosition(State.Position.Floor, to, State.Position.Facing);
        State.AutoMap.Record(State);   // 飛ばされた先を地図に
        Services.Audio.PlaySe(Sounds.Step);
        Say(Services.Strings["maze.teleported"]);
        AfterMove();
    }

    private readonly HashSet<Keys> _debugHeld = [];

    /// <summary>デバッグ用のキー(--debug のときだけ)。F5 すぐ戦闘 / F6 全員を次のレベルまでの経験値に / F7 全員の HP を戻す。</summary>
    private bool DebugKeys()
    {
        var kb = Keyboard.GetState();
        bool Down(Keys k)
        {
            bool now = kb.IsKeyDown(k), was = _debugHeld.Contains(k);
            if (now) _debugHeld.Add(k);
            else _debugHeld.Remove(k);
            return now && !was;
        }
        var party = Services.Session?.Roster.PartyMembers.ToList() ?? [];
        if (Down(Keys.F5))
        {
            MaybeEncounter(force: true);
            return true;
        }
        if (Down(Keys.F6))
        {
            foreach (var c in party) c.Xp = Math.Max(c.Xp, Core.Rules.Progression.XpForLevel(c.Level + 1));
            Say("[debug] 経験値を足した");
            return true;
        }
        if (Down(Keys.F7))
        {
            foreach (var c in party.Where(c => c.State is LifeState.Alive or LifeState.Unconscious))
            {
                c.State = LifeState.Alive;
                c.Hp = c.MaxHp;
            }
            Say("[debug] HP を戻した");
            return true;
        }
        return false;
    }

    /// <summary>今の階のテーマ(名前の無い生成した階に、名前と入ったときの文を付ける)。</summary>
    private FloorTheme? CurrentTheme => Services.Data == null ? null : FloorAdvisor.ThemeFor(Services.Data, Depth);

    /// <summary>AI がこの世界のために書いた、今の階の文(無ければ null)。</summary>
    private Core.Ai.WorldFloorText? WorldFloor => Services.Session?.World?.Floor(State.Position.Floor);

    private string FloorLabel()
    {
        var f = State.Floor;
        var themeName = WorldFloor?.Name ?? CurrentTheme?.Name;
        string? name = f.NameJa ?? f.NameEn ?? (themeName is { } tn ? Services.Name(tn) : null);
        return name == null ? $"B{State.Position.Floor + 1}" : $"B{State.Position.Floor + 1} {name}";
    }

    /// <summary>テーマのある階に着いたら、その階の様子を一文で知らせる。</summary>
    /// <summary>階に入ったとき: その階の曲にして(階ごとの曲があれば)、入ったときの文を出す。</summary>
    private void SayFloorEnter()
    {
        Services.Audio.PlayBgm(Services.BgmFor(Bgm, State.Position.Floor), gain: BgmGain);
        if (State.Floor.NameJa == null && State.Floor.NameEn == null && (WorldFloor?.Enter ?? CurrentTheme?.Enter) is { } text) Say(Services.Name(text));
    }

    private void Say(string message)
    {
        _messages.Add(message);
        if (_messages.Count > 50) _messages.RemoveAt(0);
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.Primitives(ViewArea, pb => WireframeRenderer.Draw(pb, State, ViewArea, _time));
        d.Rect(ViewArea, new Color(70, 90, 110), 1.5f);
        if (State.InDark)
            d.Text(s["maze.darkness"], new Vector2(ViewArea.X + ViewArea.Width / 2, ViewArea.Y + ViewArea.Height / 2 - 16), 28, new Color(90, 100, 110), TextAlign.Center, FontStyle.Story);

        var mapFloor = State.Dungeon.Floors[_lastKnownFloor];
        if (!Services.Options.ShowMiniMap)
        {
            // 地図を隠す設定(方眼紙で遊ぶ人向け): 枠だけ出す
            Theme.Panel(d, MiniMapArea);
            d.Text(s["maze.minimap_off"], new Vector2(MiniMapArea.X + MiniMapArea.Width / 2, MiniMapArea.Y + MiniMapArea.Height / 2 - 12), 18, Theme.Dim, TextAlign.Center);
        }
        else AutoMapRenderer.Draw(d, mapFloor, State.AutoMap.For(_lastKnownFloor), MiniMapArea, _lastKnown,
            InFog ? null : (State.Position.Cell, State.Position.Facing), MiniMapRadius,
            State.Notes.Where(n => n.Floor == _lastKnownFloor).Select(n => n.Cell).ToHashSet(), arrivals: State.AutoMap.ArrivalsOn(_lastKnownFloor).ToHashSet());
        if (InFog && Services.Options.ShowMiniMap)
            d.Text(s["maze.fog_map"], new Vector2(MiniMapArea.X + MiniMapArea.Width / 2, MiniMapArea.Y + MiniMapArea.Height - 34), 18, new Color(200, 200, 210), TextAlign.Center);

        Theme.Panel(d, InfoArea);
        d.Text(FloorLabel(), new Vector2(InfoArea.X + 16, InfoArea.Y + 10), 22, Color.White, style: FontStyle.Story);
        int start = Math.Max(0, _messages.Count - MessageLines);
        for (int i = start; i < _messages.Count; i++)
        {
            bool latest = i == _messages.Count - 1;
            DrawWrapped(d, _messages[i], new Vector2(InfoArea.X + 16, InfoArea.Y + 44 + (i - start) * 30), InfoArea.Width - 32, 19,
                latest ? Color.White : Theme.Dim);
        }
        d.Text(s.Format("maze.help", s[Services.Options.BackKey == "turn" ? "maze.help.turn" : "maze.help.step"]), new Vector2(InfoArea.X + 16, InfoArea.Y + InfoArea.Height - 40), 13, Theme.Dim);

        PartyPanel.Draw(d, Services, PartyArea);
    }

    /// <summary>1行に収まらないメッセージは、縮めて収める。</summary>
    private static void DrawWrapped(Draw2D d, string text, Vector2 at, float width, float size, Color color)
    {
        float w = d.Fonts.Measure(text, size, FontStyle.Story).X;
        if (w > width) size *= width / w;
        d.Text(text, at, size, color, style: FontStyle.Story);
    }
}

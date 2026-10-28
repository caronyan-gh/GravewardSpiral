using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Core.Combat;

/// <summary>通しで遊んだ結果。</summary>
public sealed record CampaignResult(
    bool Cleared, string? FailReason, int Expeditions, int Steps, int Fights, int Wipes, int Deaths, int Lost,
    int DeepestFloor, int BossAttempts, double AverageLevel, int EventsDone, int Fled, List<string> Log, int Drops = 0, int Uniques = 0,
    long MazeGold = 0, double WeaponBonus = 0, double MagicArmed = 0, int NamedKills = 0, int NamedItems = 0, int NamedInWorld = 0);

/// <summary>ボットの動き方。</summary>
public sealed class CampaignOptions
{
    /// <summary>迷宮に入る回数の上限(これを超えたら「時間切れ」)。</summary>
    public int MaxExpeditions { get; set; } = 150;

    /// <summary>パーティのレベル(強いクランで新しい世界を始めたとき。世界のレベル帯もその強さに合わせる)。</summary>
    public int StartLevel { get; set; } = 1;

    /// <summary>全滅してよい回数(超えたら失敗)。全滅すると、持っている金で全員を寺院へ運ぶ。</summary>
    public int MaxWipes { get; set; } = 3;

    /// <summary>パーティの HP の合計がこの割合を切ったら町へ戻る。</summary>
    public double RetreatHp { get; set; } = 0.45;

    /// <summary>ボスに挑む前に、パーティの HP がこの割合以上で、誰も欠けていないこと。満たさなければ一度町へ戻る。</summary>
    public double BossReadyHp { get; set; } = 0.9;

    /// <summary>道に迷って歩く分(最短の道の何倍歩くか)。</summary>
    public double Wander { get; set; } = 1.5;

    /// <summary>次の階へ下りるのは、パーティの平均レベルがその階の目安のレベル − これ以上になってから(届くまで今の階で鍛える)。</summary>
    public double LevelMargin { get; set; } = 0.5;

    /// <summary>ボスに挑むのは、平均レベルが最下層の目安のレベル − これ以上になってから。</summary>
    public double BossLevelMargin { get; set; }

    /// <summary>鍛えるときに1回で歩く歩数の上限。</summary>
    public int GrindSteps { get; set; } = 300;

    /// <summary>戦闘中、立っている人の HP の合計がこの割合を切るか、半分が倒れたら逃げる。</summary>
    public double FleeHp { get; set; } = 0.25;

    /// <summary>戦ったあと、HP の合計がこの割合を切っていたらキャンプする(階ごとに1回)。</summary>
    public double CampHp { get; set; } = 0.6;

    /// <summary>1人が持つ回復の水薬の数(町で買い足す)。</summary>
    public int Potions { get; set; } = 2;

    /// <summary>人に近い動き(逃げる・水薬・宝と装備・キャンプ)。false なら前の単純なボット。</summary>
    public bool Smart { get; set; } = true;

    public bool Verbose { get; set; }
}

/// <summary>
/// ボットのパーティに、シナリオを最初から最後まで遊ばせる(調整用。tools/GravewardSpiral.Sim の --campaign)。
/// 町: 寺院で蘇生(失敗して灰・消失もある。消えた人は同じクラスの L1 で入れ替える)、レベルアップ、呪文の準備、宿で大休憩、町の人を訪ねる。
/// 迷宮: 一番深く行けた階まで階段の間を歩き(1歩ごとに遭遇の判定)、その先の階では、行けるイベントのマスを近い順にこなし、
/// 最下層ではボスに挑む。弱ったら町へ戻る(帰り道も遭遇する)。宝箱は金だけ受け取る(装備は替えない)。
/// 選択肢は、いつも最初の選択肢を選ぶ(お約束の仕掛けでは「やってみる」側)。
/// </summary>
public sealed class CampaignSim
{
    private readonly GameData _data;
    private EncounterSettings _enc;
    private readonly TreasureSettings _treasure;
    private readonly CampaignOptions _o;
    private readonly GameSession _session;
    private readonly ExplorationState _state;
    private readonly IRandom _rng;
    private readonly string[] _classes;
    private readonly List<string> _log = [];
    private int _steps, _fights, _wipes, _deaths, _lost, _bossAttempts, _events, _deepest;
    private readonly HashSet<(int, GridPos)> _visited = [];
    private readonly HashSet<(int, GridPos)> _later = [];   // まだ済まないイベント(紋章が足りない扉など)。次に迷宮へ入ったときにまた試す

    private CampaignSim(GameData data, ContentLibrary content, string[] classes, ulong seed, CampaignOptions o)
    {
        _data = data;
        _o = o;
        _classes = classes;
        _enc = EncounterSettings.Load(content);
        _treasure = TreasureSettings.Load(content);
        var gen = MazeGenSettings.Load(content);
        // シナリオが迷宮を持っていればそれを使い、無ければシードから作る
        var dungeon = DungeonLoader.ScenarioDungeon(content);
        var request = dungeon != null ? DungeonLoader.RequestFor(dungeon, seed) : GenerationRequest.Default(gen, seed);
        dungeon ??= new MazeGenerator(gen).Generate(request).Dungeon;
        _session = GameSession.New(0, request, dungeon);
        for (int i = 0; i < classes.Length; i++) AddMember(classes[i], i);
        // 強いパーティで始めたら、世界のレベル帯をその強さに合わせる(周回)
        if (o.StartLevel > 1)
        {
            _session.SetWorldLevel(_enc, WorldLevel.ClanPower(data, Party));
            _enc = _session.WorldSettings(_enc);
        }
        // 自由な迷宮は、由来で出る敵が決まる。自由な迷宮から作ったシナリオは、残した由来と主の組で始める
        var pack = Save.WorldPackDef.Of(content);
        if (content.Scenario == null) _session.Origin = Ai.WorldBuilder.Pick(data, seed);
        else pack?.ApplyTo(_session);
        _session.BuildRosters(data, _enc);
        pack?.ApplyBossPicks(_session);
        _session.ApplyScenario(data, content, _enc, gen);   // シナリオで決めた顔ぶれ・名のある敵・置いたイベント
        _state = _session.EnterMaze();
        _rng = new Pcg32(seed, 77);
    }

    private Character AddMember(string cls, int i)
    {
        var c = CharacterFactory.QuickCreate(_data, $"#{i + 1}", "human", cls, _o.StartLevel);
        _session.Roster.Characters.Add(c);
        _session.Roster.AddToParty(c);
        return c;
    }

    public static CampaignResult Run(GameData data, ContentLibrary content, string[] classes, ulong seed, CampaignOptions? options = null) =>
        new CampaignSim(data, content, classes, seed, options ?? new CampaignOptions()).Play();

    private List<Character> Party => _session.Roster.PartyMembers.ToList();
    private Dungeon Dungeon => _session.Dungeon;
    private int Floors => Dungeon.Floors.Count;
    private double DepthOf(int floor) => Floors <= 1 ? 0 : (double)floor / (Floors - 1);

    private void Log(string s)
    {
        if (_o.Verbose) _log.Add(s);
    }

    private CampaignResult Result(bool cleared, string? why, int expeditions) => new(cleared, why, expeditions, _steps, _fights, _wipes, _deaths, _lost,
        _deepest + 1, _bossAttempts, AverageLevel, _events, _fled, _log, Drops, Uniques, MazeGold,
        Party.Average(c => BestWeapon(c).Bonus), Party.Count(c => BestWeapon(c).Magic) / (double)Math.Max(1, Party.Count),
        NamedKills, NamedItems, _session.Named.Count);

    /// <summary>迷宮で稼いだ金の合計(入るたびの、出たときと入ったときの差)。</summary>
    public long MazeGold { get; private set; }

    /// <summary>終わったときに持っている一番強い武器(利き手か遠隔の枠)の強化値と、魔法の武器か(物理に抵抗する敵に効く)。</summary>
    private (int Bonus, bool Magic) BestWeapon(Character c)
    {
        var best = (Bonus: 0, Magic: false);
        foreach (var slot in new[] { EquipSlot.MainHand, EquipSlot.Ranged })
        {
            if (c.Inventory[slot] is not { } it || !_data.Items.TryGetValue(it.ItemId, out var d) || d.Kind != ItemKind.Weapon) continue;
            int b = it.TotalBonus(d);
            bool magic = d.IsMagic || b > 0;
            if (b > best.Bonus || (magic && !best.Magic)) best = (Math.Max(best.Bonus, b), best.Magic || magic);
        }
        return best;
    }

    private CampaignResult Play()
    {
        for (int exp = 1; exp <= _o.MaxExpeditions; exp++)
        {
            if (!Town()) return Result(false, "町で立て直せない", exp);
            long before = PartyGold.Total(Party);
            var outcome = Expedition();
            MazeGold += Math.Max(0, PartyGold.Total(Party) - before);   // 迷宮で稼いだ金(戦闘と宝箱。売った分は入らない)
            if (outcome == Outcome.Cleared)
            {
                Log($"#{exp} クリア(平均 L{AverageLevel:F1})");
                return Result(true, null, exp);
            }
            if (outcome == Outcome.Wiped)
            {
                _wipes++;
                Log($"#{exp} 全滅(B{_state.Position.Floor + 1})");
                if (_wipes > _o.MaxWipes) return Result(false, $"全滅 {_wipes} 回(B{_state.Position.Floor + 1})", exp);
                // 回収: 全員を寺院へ(金は失う)
                foreach (var c in Party.Where(c => c.State == LifeState.Unconscious)) c.State = LifeState.Dead;
                foreach (var c in Party) c.Gold /= 2;
            }
        }
        return Result(false, "時間切れ", _o.MaxExpeditions);
    }

    // ---------------- 町 ----------------

    private bool Town()
    {
        var party = Party;
        foreach (var c in party.Where(c => c.State is LifeState.Dead or LifeState.Ashes).ToList())
        {
            PartyGold.Pay(party, Math.Min(PartyGold.Total(party), Temple.RevivePrice(c)));   // 足りない分は寺院が貸す(調整用の甘さ)
            var r = Temple.Revive(c, _rng);
            if (r == Temple.ReviveResult.Lost || c.State == LifeState.Lost)
            {
                // 消えた: 同じクラスの新人を入れる
                _lost++;
                int i = _session.Roster.Party.IndexOf(c.Id);
                _session.Roster.RemoveFromParty(c);
                var n = CharacterFactory.QuickCreate(_data, $"#{_session.Roster.Characters.Count + 1}", "human", c.ClassId);
                _session.Roster.Characters.Add(n);
                _session.Roster.Party.Insert(Math.Max(0, i), n.Id);
            }
        }
        foreach (var c in Party)
        {
            if (c.State == LifeState.Ashes) continue;   // 灰のまま(次の町でまた試す)
            if (c.CanLevelUp)
            {
                c.LevelUp(_data);
                Spellcasting.AutoPick(_data, c);
            }
            c.LongRest();
        }
        if (_o.Smart) Shopping();
        // 町の人を訪ねる(話でフラグが立つ)
        var ctx = new EventContext { Data = _data, Session = _session, Rng = _rng, TargetLevel = _enc.TargetLevel(0.5) };
        foreach (var e in _data.Events.Values.Where(e => e.Place == "town").OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (EventRunner.Available(e, ctx)) Drive(new EventRunner(e, ctx), -1);
        }
        return Party.Count(c => c.CanAct) >= Math.Min(2, Party.Count);   // 少人数の編成は、全員が動ければよい
    }

    /// <summary>
    /// 町の買い物: 拾った品を鑑定し、よい装備に替え、残りは売る(水薬と大事なものは残す)。
    /// 店の鎧と盾でよくなるものは買い、水薬を買い足す。
    /// </summary>
    private void Shopping()
    {
        var party = Party;
        var defs = _data.Items;
        foreach (var c in party.Where(c => c.IsAlive))
        {
            foreach (var it in c.Inventory.Items.Where(i => !i.Identified).ToList())
            {
                if (PartyGold.Pay(party, Shop.IdentifyPrice(defs[it.ItemId], it))) it.Identified = true;
            }
        }
        // 拾った装備を、使えてよくなる人に渡して替える
        var loot = party.SelectMany(c => c.Inventory.Items.Select(i => (Owner: c, Item: i))).Where(x => x.Item.Identified
            && defs[x.Item.ItemId].IsEquippable && !defs[x.Item.ItemId].Cursed && defs[x.Item.ItemId].Kind != ItemKind.Backpack).ToList();
        foreach (var (owner, item) in loot)
        {
            foreach (var c in party.Where(c => c.IsAlive))
            {
                if (!owner.Inventory.Items.Contains(item)) break;
                owner.Inventory.Items.Remove(item);
                c.Inventory.Items.Add(item);
                if (TryUpgrade(c, item)) break;
                c.Inventory.Items.Remove(item);
                owner.Inventory.Items.Add(item);
            }
        }
        // 売る(水薬と大事なものは残す)
        foreach (var c in party)
        {
            foreach (var it in c.Inventory.Items.Where(i => i.ItemId != PotionId && defs[i.ItemId].Kind != ItemKind.Key && Shop.CanSell(defs[i.ItemId])).ToList())
            {
                c.Inventory.Items.Remove(it);
                PartyGold.Share(party, Shop.SellPrice(defs[it.ItemId], it));
            }
        }
        // 店の鎧と盾(高い順に、買えてよくなるもの)
        foreach (var c in party.Where(c => c.IsAlive))
        {
            foreach (var def in Shop.Standard(_data).Where(d => d.Kind is ItemKind.Armor or ItemKind.Shield).OrderByDescending(d => d.Price))
            {
                var it = ItemInstance.Of(def.Id);
                long price = Shop.BuyPrice(def, it);
                if (PartyGold.Total(party) < price + 200) continue;   // 蘇生のために少し残す
                if (!c.Inventory.TryAdd(it, defs)) break;
                if (TryUpgrade(c, it)) PartyGold.Pay(party, price);
                else c.Inventory.Items.Remove(it);
            }
        }
        // 水薬を買い足す
        var potion = defs[PotionId];
        foreach (var c in party.Where(c => c.IsAlive))
        {
            int have = c.Inventory.Items.Where(i => i.ItemId == PotionId).Sum(i => i.Count);
            for (; have < _o.Potions; have++)
            {
                long price = Shop.BuyPrice(potion, ItemInstance.Of(PotionId));
                if (PartyGold.Total(party) < price + 200 || !c.Inventory.TryAdd(ItemInstance.Of(PotionId), defs)) break;
                PartyGold.Pay(party, price);
            }
        }
    }

    /// <summary>袋の中の item を装備してみて、よくなれば残す(true)。よくならなければ元に戻す。</summary>
    private bool TryUpgrade(Character c, ItemInstance item)
    {
        var defs = _data.Items;
        var def = defs[item.ItemId];
        var inv = c.Inventory;
        EquipSlot? slot = def.Kind switch
        {
            ItemKind.Armor => EquipSlot.Armor,
            ItemKind.Shield => EquipSlot.OffHand,
            ItemKind.Weapon => def.Ranged ? EquipSlot.Ranged : EquipSlot.MainHand,
            ItemKind.Accessory => def.Slot switch
            {
                "ring" => inv[EquipSlot.Ring1] == null ? EquipSlot.Ring1 : EquipSlot.Ring2,
                "neck" => EquipSlot.Neck,
                "cloak" => EquipSlot.Cloak,
                "head" => EquipSlot.Head,
                "hands" => EquipSlot.Hands,
                "feet" => EquipSlot.Feet,
                "waist" => EquipSlot.Waist,
                _ => null,
            },
            _ => null,
        };
        if (slot is not { } sl) return false;
        var old = inv[sl];
        if (def.Kind == ItemKind.Accessory)
        {
            if (old != null || def.Attunement && inv.AttunedCount >= Inventory.MaxAttuned) return false;
            return inv.Equip(inv.Items.IndexOf(item), sl, defs) == EquipResult.Ok;
        }
        if (def.Kind == ItemKind.Shield && inv[EquipSlot.MainHand] is { } mh && defs[mh.ItemId].Has("two_handed")) return false;
        if (def.Kind == ItemKind.Weapon)
        {
            // 今の武器と同じ間合い(近接か遠隔か)で、習熟があるものだけ
            if (sl == EquipSlot.MainHand && old != null && defs[old.ItemId].Ranged != def.Ranged) return false;
            if (def.Has("two_handed") && inv[EquipSlot.OffHand] != null) return false;
            if (!c.WeaponProficient(_data, def)) return false;
        }
        double before = Score(c, sl);
        if (inv.Equip(inv.Items.IndexOf(item), sl, defs) != EquipResult.Ok) return false;
        if (Score(c, sl) > before + 1e-9 && c.ArmorProficient(_data)) return true;
        // 元に戻す
        inv.Equipped.Remove(sl);
        item.Attuned = false;
        if (old != null)
        {
            inv.Items.Remove(old);
            inv.Equipped[sl] = old;
        }
        inv.Items.Add(item);
        return false;
    }

    /// <summary>装備のよさ。鎧と盾は AC、武器は1回の攻撃の期待ダメージ + 命中の上乗せ。</summary>
    private double Score(Character c, EquipSlot slot)
    {
        if (slot is not (EquipSlot.MainHand or EquipSlot.Ranged)) return c.ArmorClass(_data);
        if (c.Inventory[slot] is not { } w || !_data.Items.TryGetValue(w.ItemId, out var def) || def.Damage == null) return 0;
        double dmg = Rules.DiceExpression.Parse(def.Damage).Average + w.TotalBonus(def);
        if (w.ElementDice(def) is { } e) dmg += Rules.DiceExpression.Parse(e).Average;
        dmg += Loot.OnHitAverage(_data, w);
        return dmg + w.HitAcBonus(def) * 1.5;
    }

    // ---------------- 迷宮 ----------------

    private enum Outcome { Returned, Wiped, Cleared }

    private Outcome Expedition()
    {
        _visited.ExceptWith(_later);
        _later.Clear();
        _campedFloor = -1;
        _state.Position = new PartyPosition(0, Stairs(0, up: true), Direction.North);
        _state.StepsSinceCombat = 0;
        _state.RoomsFloor = -1;   // 入り直したら、部屋の敵は抽選し直す
        // 一番深く行けた階まで、階段から階段へ歩く
        for (int f = 0; f < _deepest; f++)
        {
            if (Walk(f, Stairs(f, up: true), Stairs(f, up: false)) is { } o) return o;
            if (Weak()) return Retreat(f, Stairs(f, up: false));
        }
        while (true)
        {
            int f = _deepest;
            var here = Stairs(f, up: true);
            _state.Position = new PartyPosition(f, here, Direction.North);
            // 行けるイベントを近い順に
            while (NextEvent(f, here) is { } next)
            {
                if (Walk(f, here, next.Cell) is { } o) return o;
                if (Weak()) return Retreat(f, next.Cell);   // 着く前に弱った(イベントは次の機会に)
                here = next.Cell;
                _visited.Add((f, here));
                if (next.Mark.StartsWith("boss:"))
                {
                    // ボスの前でキャンプして小休憩(ヒットダイスで回復。休憩中にも敵が来ることがある)
                    if (AverageLevel >= _enc.TargetLevel(1) - _o.BossLevelMargin && Camp(f) is { } co) return co;
                    if (!BossReady())
                    {
                        Log($"ボスの前で引き返す(平均 L{AverageLevel:F1}、HP {(double)Party.Sum(c => c.Hp) / Party.Sum(c => c.MaxHp):P0}、動けない {Party.Count(c => !c.CanAct)})");
                        _visited.Remove((f, here));
                        return Grind(f, here);
                    }
                    _bossAttempts++;
                    var boss = _session.Boss(_data, next.Mark["boss:".Length..])!;
                    var outcome = Fight(boss.Fighters(_data.Monsters), boss.Scale.Times(_session.FixedScale(_enc)));
                    Log($"ボスと戦った: {outcome}(平均 L{AverageLevel:F1})");
                    if (outcome != CombatOutcome.Victory)
                    {
                        _visited.Remove((f, here));   // また挑む
                        return outcome == CombatOutcome.Defeat ? Outcome.Wiped : Retreat(f, here);
                    }
                    if (boss.RewardFlag != null) _session.Flags.Add(boss.RewardFlag);
                    Log($"ボス {boss.Id} を倒した(平均 L{AverageLevel:F1})");
                    AfterVictory(f, boss.Fighters(_data.Monsters), randomChest: false, boss: boss.Id);
                    return Outcome.Cleared;
                }
                if (RunEvent(f, here, next.Mark) is { } eo) return eo;
                if (Weak()) return Retreat(f, here);
            }
            if (f == Floors - 1) return Retreat(f, here);   // ボスがいない最下層(ここには来ない)
            // 次の階の目安に届いていなければ、この階で鍛えて町へ戻る
            if (AverageLevel < _enc.TargetLevel(DepthOf(f + 1)) - _o.LevelMargin) return Grind(f, here);
            var down = Stairs(f, up: false);
            if (Walk(f, here, down) is { } o2) return o2;
            if (Weak()) return Retreat(f, down);
            _deepest = f + 1;
            Log($"B{_deepest + 1} へ(平均 L{AverageLevel:F1})");
        }
    }

    private GridPos Stairs(int floor, bool up)
    {
        var f = Dungeon.Floors[floor];
        return f.FindFeature(up ? CellFeature.StairsUp : CellFeature.StairsDown) ?? f.Start;
    }

    private bool Weak()
    {
        var party = Party;
        if (party.Any(c => !c.CanAct)) return true;
        int max = party.Sum(c => c.MaxHp);
        return max == 0 || (double)party.Sum(c => c.Hp) / max < _o.RetreatHp;
    }

    private double AverageLevel => Party.Average(c => c.Level);

    /// <summary>ボスに挑めるか(最下層の目安のレベルに届き、誰も欠けず、HP が十分)。</summary>
    private bool BossReady()
    {
        var party = Party;
        return AverageLevel >= _enc.TargetLevel(1) - _o.BossLevelMargin && party.All(c => c.CanAct)
            && (double)party.Sum(c => c.Hp) / party.Sum(c => c.MaxHp) >= _o.BossReadyHp;
    }

    /// <summary>キャンプの小休憩。休憩中に敵が来たら戦う(全滅したら Wiped)。</summary>
    private Outcome? Camp(int floor)
    {
        if (Encounters.RestCheck(_enc, _rng))
        {
            var e = Encounters.Build(_data, _enc, DepthOf(floor), Party.Count, _rng, _session.RosterOf(floor));
            if (Fight(e.Groups, e.Scale) == CombatOutcome.Defeat) return Outcome.Wiped;
        }
        ShortRest(Party);
        return null;
    }

    /// <summary>小休憩(ゲームのキャンプと同じ: ヒットダイス、技の回数、秘術の回復、休息の歌)。</summary>
    private void ShortRest(List<Character> party)
    {
        var rested = party.Where(c => c.CanAct).Select(c => (c, c.ShortRest(_data, _rng))).ToList();
        ClassFeatures.SongOfRest(rested, party, _rng);
    }

    /// <summary>この階をうろついて鍛える(弱るか、歩数の上限まで)。そのあと町へ戻る。</summary>
    private Outcome Grind(int floor, GridPos at)
    {
        for (int i = 0; i < _o.GrindSteps && !Weak(); i++)
        {
            if (Step(floor) is { } o) return o;
        }
        return Retreat(floor, at);
    }

    /// <summary>1歩。遭遇したら戦う。全滅したら Wiped。</summary>
    private Outcome? Step(int floor)
    {
        _steps++;
        _state.StepsSinceCombat++;
        if (!Encounters.Check(_enc, _state.StepsSinceCombat, _rng)) return null;
        _state.StepsSinceCombat = 0;
        if (_session.NamedWandering(floor) is { } wanderer && _rng.NextDouble() < NamedMonsterDef.WanderChance) return NamedFight(floor, wanderer);
        var e = Encounters.Build(_data, _enc, DepthOf(floor), Party.Count, _rng, _session.RosterOf(floor));
        var outcome = Fight(e.Groups, e.Scale);
        if (outcome == CombatOutcome.Defeat) return Outcome.Wiped;
        if (outcome == CombatOutcome.Victory) AfterVictory(floor, e.Groups, randomChest: true);
        return null;
    }

    /// <summary>町へ戻る(今の階の上り階段まで歩き、上の階を下から上へ)。</summary>
    private Outcome Retreat(int floor, GridPos from)
    {
        if (Walk(floor, from, Stairs(floor, up: true)) is { } o) return o;
        for (int f = floor - 1; f >= 0; f--)
        {
            if (Walk(f, Stairs(f, up: false), Stairs(f, up: true)) is { } o2) return o2;
        }
        return Outcome.Returned;
    }

    /// <summary>歩く(1歩ごとに遭遇。道すがら、敵のいる部屋に踏み込めば戦う)。全滅したら Wiped、それ以外は null。</summary>
    private Outcome? Walk(int floor, GridPos from, GridPos to)
    {
        var dists = Distances(floor, from);
        int dist = dists.TryGetValue(to, out var d) ? d : 0;
        int steps = (int)Math.Ceiling(dist * _o.Wander);
        _state.Position = new PartyPosition(floor, from, Direction.North);
        if (_state.RoomsFloor != floor) _state.RollRooms(_rng, _enc.RoomChance);
        var path = Path(floor, dists, to);
        int passed = 0;
        for (int i = 0; i < steps; i++)
        {
            if (Step(floor) is { } o) return o;
            // 歩いた割合だけ道を進め、部屋に踏み込んだら戦う
            for (int reach = (int)((long)(i + 1) * (path.Count - 1) / steps); passed < reach; passed++)
                if (RoomFight(floor, path[passed], path[passed + 1]) is { } r) return r;
            if (Weak() && i < steps - 1) return null;   // 弱ったら、ここで止まって町へ(呼んだ側が判断する)
        }
        _state.Position = new PartyPosition(floor, to, Direction.North);
        return null;
    }

    private CombatOutcome Fight(IReadOnlyList<(MonsterDef Def, int Count)> groups, MonsterScale? scale)
    {
        _fights++;
        var party = Party;
        var engine = new CombatEngine(_data, _rng);
        var s = engine.Start(party, groups, scale);
        while (s.Outcome == CombatOutcome.Ongoing && s.Round < CombatSimulator.MaxRounds) engine.RunRound(s, Commands(s));
        _deaths += party.Count(c => c.State is LifeState.Dead or LifeState.Ashes or LifeState.Lost);
        if (s.Outcome == CombatOutcome.Fled) _fled++;
        if (_o.Smart && s.Outcome != CombatOutcome.Defeat) Recover();
        return s.Outcome == CombatOutcome.Ongoing ? CombatOutcome.Fled : s.Outcome;
    }

    /// <summary>
    /// 戦闘の行動。おまかせの行動に、人らしい判断を足す: 危なくなったら全員で逃げ、HP の少ない人は水薬を飲む。
    /// </summary>
    private Dictionary<string, PlayerCommand> Commands(CombatState s)
    {
        var d = CombatSimulator.AutoCommands(s, _data);
        if (!_o.Smart) return d;
        var standing = s.Party.Where(p => p.CanAct).ToList();
        int maxAll = s.Party.Sum(p => p.MaxHp);
        bool dire = standing.Count * 2 <= s.Party.Count || maxAll == 0 || (double)standing.Sum(p => p.Hp) / maxAll < _o.FleeHp;
        if (dire)
        {
            foreach (var p in standing) d[p.Id] = new PlayerCommand(CommandKind.Flee);
            return d;
        }
        for (int i = 0; i < s.Party.Count; i++)
        {
            var p = s.Party[i];
            if (!p.CanAct || p.Hp >= p.MaxHp * 0.35 || d.TryGetValue(p.Id, out var cmd) && cmd.Kind == CommandKind.Cast) continue;
            int potion = p.Character.Inventory.Items.FindIndex(it => it.ItemId == PotionId);
            if (potion >= 0) d[p.Id] = new PlayerCommand(CommandKind.Item, -1, i, ItemIndex: potion);
        }
        return d;
    }

    private const string PotionId = "potion_of_healing";

    /// <summary>戦ったあと: 弱っていればキャンプ(階ごとに1回)、それでも HP の少ない人は水薬を飲む。</summary>
    private void Recover()
    {
        var party = Party;
        int max = party.Sum(c => c.MaxHp);
        if (max > 0 && (double)party.Sum(c => c.Hp) / max < _o.CampHp && _campedFloor != _state.Position.Floor)
        {
            _campedFloor = _state.Position.Floor;
            ShortRest(party);
        }
        foreach (var c in party.Where(c => c.CanAct && c.Hp < c.MaxHp * 0.4))
        {
            var holder = party.FirstOrDefault(h => h.Inventory.Items.Any(i => i.ItemId == PotionId));
            if (holder == null) break;
            var it = holder.Inventory.Items.First(i => i.ItemId == PotionId);
            if (--it.Count <= 0) holder.Inventory.Items.Remove(it);
            c.Hp = Math.Min(c.MaxHp, c.Hp + Rules.DiceExpression.Parse("2d4+2").Roll(_rng).Total);
        }
    }

    private int _campedFloor = -1, _fled;

    /// <summary>落とし物の数と、そのうちの固有の品の数(結果に出す)。</summary>
    public int Drops { get; private set; }
    public int Uniques { get; private set; }

    /// <summary>勝ったあと: 落とし物があれば必ず宝箱、無ければ確率で宝箱(randomChest のとき)。</summary>
    private void AfterVictory(int floor, IEnumerable<(MonsterDef Def, int Count)> defeated, bool randomChest, string? boss = null, bool room = false)
    {
        var drops = Loot.RollDrops(_data, _treasure, defeated, _enc.TargetLevel(DepthOf(floor)), _rng, boss, _session.DropBoostOf(_data, floor));
        Drops += drops.Count;
        Uniques += drops.Count(d => _data.Items[d.ItemId].NoRandom);
        foreach (var d in drops.Where(d => _data.Items[d.ItemId].NoRandom)) Log($"固有の品を拾った: {d.ItemId}(平均 L{AverageLevel:F1})");
        if (drops.Count > 0 || (randomChest && _rng.NextDouble() < (room ? _treasure.RoomChestChance : _treasure.ChestChance))) OpenChest(floor, drops);
    }

    /// <summary>宝箱。盗賊道具の使える人がいれば罠はたいてい外せる。中身(金と品)はパーティで分ける。</summary>
    private void OpenChest(int floor, List<ItemInstance>? drops = null)
    {
        var chest = Chest.Generate(_data, _treasure, DepthOf(floor), _enc.TargetLevel(DepthOf(floor)), _rng, drops);
        var party = Party;
        if (!_o.Smart)
        {
            PartyGold.Share(party, chest.Gold);
            return;
        }
        bool thief = party.Any(c => c.CanAct && c.ClassIds.Any(id => _data.Classes[id].Tools.Contains("thieves_tools")));
        if (chest.Trap != ChestTrap.None && _rng.NextDouble() < (thief ? 0.15 : 0.5))
        {
            var opener = party.FirstOrDefault(c => c.CanAct) ?? party[0];
            chest.Trigger(_data, party, opener, _rng);
        }
        chest.Loot(_data, party);
    }

    // ---------------- イベント ----------------

    private (GridPos Cell, string Mark)? NextEvent(int floor, GridPos from)
    {
        var f = Dungeon.Floors[floor];
        var dist = Distances(floor, from, respectGates: true);
        return f.AllCells()
            .Where(p => f[p].Mark is { } m && (m.StartsWith(EventPlacer.MarkPrefix) || m.StartsWith("boss:")) && !_visited.Contains((floor, p)) && dist.ContainsKey(p))
            .OrderBy(p => f[p].Mark!.StartsWith("boss:") ? 1 : 0).ThenBy(p => dist[p])
            .Select(p => ((GridPos, string)?)(p, f[p].Mark!)).FirstOrDefault();
    }

    private Outcome? RunEvent(int floor, GridPos cell, string mark)
    {
        if (!_data.Events.TryGetValue(mark[EventPlacer.MarkPrefix.Length..], out var def)) return null;
        double depth = DepthOf(floor);
        var ctx = new EventContext
        {
            Data = _data, Session = _session, Rng = _rng, Maze = _state, At = (floor, cell),
            Depth = depth, TargetLevel = _enc.TargetLevel(depth), Treasure = _treasure,
            // 唱える・休むと起こる仕掛けは、着いたときにそれをしたことにする(唱える呪文は、条件 cast にあるもの)
            Cast = def.OnCast ? CastWanted(def) ?? "" : null,
            Rested = def.OnRest,
        };
        if (!EventRunner.Available(def, ctx)) return null;
        _events++;
        var outcome = Drive(new EventRunner(def, ctx), floor);
        if (def.Scatter == null) Log($"イベント {def.Id}(B{floor + 1}){(outcome == CombatOutcome.Defeat ? " で全滅" : "")}");   // 手で置いたイベント(物語の部屋など)を通れたか
        if (outcome == CombatOutcome.Defeat)
        {
            _visited.Remove((floor, cell));   // 全滅したイベントは、また来る
            return Outcome.Wiped;
        }
        if (_session.Flags.Contains(GameSession.EndingFlag)) return Outcome.Cleared;   // 手順 ending に着いた
        // 一度きりでない・まだ済んでいないイベント(紋章が足りない扉など)は、あとでまた来る
        if (EventRunner.Available(def, ctx) && def.Once != null) _later.Add((floor, cell));
        return null;
    }

    /// <summary>イベントの中の条件 cast が求める呪文(無ければ null)。</summary>
    private static string? CastWanted(EventDef def)
    {
        static IEnumerable<EventCondition> All(IEnumerable<EventStep> steps) =>
            steps.SelectMany(s => s.Conditions().Concat(s.Children().SelectMany(All)));
        return def.When.Concat(All(def.Steps)).FirstOrDefault(c => c.Type == "cast" && !c.Not && !string.IsNullOrEmpty(c.Id))?.Id;
    }

    /// <summary>イベントを最後まで進める(いつも最初の選択肢)。戦闘で全滅したら Defeat を返す。</summary>
    private CombatOutcome? Drive(EventRunner r, int floor)
    {
        for (int guard = 0; guard < 500; guard++)
        {
            switch (r.Next())
            {
                case EventDone or EventToTown or EventWarp: return null;   // 飛ばされたら、ボットは次の目当てへ歩き直す
                case EventChoice: r.Choose(0); break;
                case EventCombat c:
                {
                    if (floor < 0)
                    {
                        r.CombatEnded(false);
                        break;
                    }
                    List<(MonsterDef, int)> groups;
                    MonsterScale? scale;
                    if (c.Boss != null && _session.Boss(_data, c.Boss) is { } b)
                    {
                        groups = b.Fighters(_data.Monsters);
                        scale = b.Scale.Times(_session.FixedScale(_enc));
                    }
                    else if (c.Groups.Count > 0)
                    {
                        groups = c.Groups.Select(g => (_data.Monsters[g.Monster], g.Count)).ToList();
                        scale = Encounters.Scale(_enc, _enc.TargetLevel(DepthOf(floor))).Times(_session.FixedScale(_enc));
                    }
                    else
                    {
                        var e = Encounters.Build(_data, _enc, DepthOf(floor), Party.Count, _rng, _session.RosterOf(floor));
                        groups = e.Groups.ToList();
                        scale = e.Scale;
                    }
                    var outcome = Fight(groups, scale);
                    if (outcome == CombatOutcome.Defeat) return outcome;
                    r.CombatEnded(outcome == CombatOutcome.Victory);
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>prev から cur へ来て、敵のいる部屋に踏み込んだら戦う。全滅したら Wiped。</summary>
    private Outcome? RoomFight(int floor, GridPos prev, GridPos cur)
    {
        _state.Position = new PartyPosition(floor, cur, Direction.North);
        if (_session.NamedLairAt(floor, cur) is { } lair) return NamedFight(floor, lair);
        if (_state.EnteredOccupiedRoom(prev) is not int room) return null;
        RoomFights++;
        _state.StepsSinceCombat = 0;
        var e = Encounters.Build(_data, _enc, DepthOf(floor), Party.Count, _rng, _session.RosterOf(floor));
        var outcome = Fight(e.Groups, e.Scale);
        if (outcome == CombatOutcome.Defeat) return Outcome.Wiped;
        if (outcome == CombatOutcome.Victory)
        {
            _state.OccupiedRooms.Remove(room);
            AfterVictory(floor, e.Groups, randomChest: true, room: true);
        }
        return null;
    }

    /// <summary>名のある敵と戦う(ゲームと同じ: 元の敵を強くした1体。勝てば item_chance で品を落とす)。</summary>
    private Outcome? NamedFight(int floor, NamedSpawn spawn)
    {
        if (!_data.NamedMonsters.TryGetValue(spawn.Id, out var def) || !_data.Monsters.TryGetValue(def.Monster, out var baseDef)) return null;
        var named = baseDef.WithName(def.Name);
        named.Xp = baseDef.Xp * 3;
        List<(MonsterDef, int)> groups = [(named, 1)];
        var outcome = Fight(groups, new MonsterScale(def.HpMult, def.DamageMult, def.AcBonus, def.HitBonus));
        if (outcome == CombatOutcome.Defeat) return Outcome.Wiped;
        if (outcome != CombatOutcome.Victory) return null;
        spawn.Defeated = true;
        NamedKills++;
        Log($"名のある敵を倒した: {spawn.Id}(B{floor + 1})");
        if (_rng.NextDouble() < def.ItemChance && _data.Items.TryGetValue(def.Item, out var item))
        {
            NamedItems++;
            OpenChest(floor, [Loot.Make(_data, _treasure, item, _enc.TargetLevel(DepthOf(floor)), _rng)]);
        }
        else AfterVictory(floor, groups, randomChest: true);
        return null;
    }

    /// <summary>名のある敵を倒した数と、その品を手に入れた数。</summary>
    public int NamedKills { get; private set; }
    public int NamedItems { get; private set; }

    /// <summary>部屋の戦闘の数。</summary>
    public int RoomFights { get; private set; }

    // ---------------- 道のり ----------------

    /// <summary>from(dists の 0 のマス)から to までの道(両端を含む)。行けなければ to だけ。</summary>
    private List<GridPos> Path(int floor, Dictionary<GridPos, int> dists, GridPos to)
    {
        var f = Dungeon.Floors[floor];
        if (!dists.TryGetValue(to, out int k)) return [to];
        var path = new List<GridPos> { to };
        var p = to;
        while (k > 0)
        {
            GridPos? prev = null;
            foreach (var dir in DirectionExtensions.All)
            {
                if (f.Neighbor(p, dir) is not { } q || !dists.TryGetValue(q, out int kq) || kq != k - 1) continue;
                var back = dir.Opposite();
                if (f[q][back] is EdgeType.Wall or EdgeType.OneWayIn || f.Neighbor(q, back) != p) continue;
                prev = q;
                break;
            }
            if (prev is not { } step) break;
            path.Add(step);
            p = step;
            k--;
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// from から各マスまでの歩数(幅優先)。壁と「入るだけ」の一方通行は通れない。錠は通れる(ボットは鍵を拾う)。
    /// respectGates なら、仕掛けで開く扉(印 gate:)は開けたものだけ通れる。
    /// </summary>
    private Dictionary<GridPos, int> Distances(int floor, GridPos from, bool respectGates = false)
    {
        var f = Dungeon.Floors[floor];
        var dist = new Dictionary<GridPos, int> { [from] = 0 };
        var queue = new Queue<GridPos>([from]);
        bool Gate(GridPos p) => f[p].Mark?.StartsWith("gate:") == true;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in DirectionExtensions.All)
            {
                var e = f[p][d];
                if (e is EdgeType.Wall or EdgeType.OneWayIn) continue;
                if (f.Neighbor(p, d) is not { } n || f[n].IsSolid || dist.ContainsKey(n)) continue;
                if (respectGates && e == EdgeType.Locked && (Gate(p) || Gate(n)) && !_state.IsUnlocked(floor, p, d)) continue;
                dist[n] = dist[p] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }
}

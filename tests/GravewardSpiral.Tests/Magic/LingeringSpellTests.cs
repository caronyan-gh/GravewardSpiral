using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Magic;

/// <summary>地帯の呪文・召喚・カウンタースペル・伝説的抵抗。</summary>
public class LingeringSpellTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Make(string cls, int level) => CharacterFactory.QuickCreate(Data, cls, "human", cls, level);

    private static CombatState State(List<Character> party, MonsterDef def, int count, int hp = 0) => new()
    {
        Party = party.Select(c => new PartyMember(c, Data)).ToList(),
        Groups = [new EnemyGroup(def, Enumerable.Range(0, count).Select(_ => new MonsterUnit(def, hp > 0 ? hp : def.HpAverage)).ToList())],
    };

    private static Dictionary<string, PlayerCommand> Dodge(CombatState s) => s.Party.ToDictionary(p => p.Id, _ => PlayerCommand.Dodge);

    [Fact]
    public void ZoneSpellHitsAgainOnTheCastersTurn()
    {
        var druid = Make("druid", 5);
        var s = State([druid], Data.Monsters["ogre"], 3, hp: 500);
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "moonbeam", SlotLevel: 2));
        Assert.Single(s.Lingerings);
        s.Order.AddRange([s.Party[0], .. s.Groups[0].Units]);
        int before = s.Groups[0].Units.Sum(u => u.Hp);
        engine.RunRound(s, Dodge(s));
        Assert.Contains(s.Log, e => e.Key == "combat.zone");
        Assert.True(s.Groups[0].Units.Sum(u => u.Hp) < before);
    }

    [Fact]
    public void SummonAttacksEachRoundAndEndsWithConcentration()
    {
        var druid = Make("druid", 5);
        var s = State([druid], Data.Monsters["ogre"], 1, hp: 500);
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "conjure_animals", SlotLevel: 3));
        var l = Assert.Single(s.Lingerings);
        Assert.Equal("dire_wolf", l.Summoned!.Id);
        Assert.Equal(2, l.Unit!.Count);
        s.Order.AddRange([s.Party[0], .. s.Groups[0].Units]);
        engine.RunRound(s, Dodge(s));
        Assert.True(s.Groups[0].Units[0].Hp < 500 || s.Log.Any(e => e.Key.Contains("miss")));
        // ほかの集中の呪文を唱えると、前の集中が切れて、呼んだものは消える
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "moonbeam", SlotLevel: 2));
        Assert.DoesNotContain(s.Lingerings, x => x.Summoned != null);
    }

    [Fact]
    public void DumbBeastsChaseSummonsAndSmartFoesHuntTheCaster()
    {
        int SummonHits(string monster, int seed)
        {
            var druid = Make("druid", 9);
            var fighter = Make("fighter", 9);
            var s = State([fighter, druid], Data.Monsters[monster], 4);
            var engine = new CombatEngine(Data, new Pcg32((ulong)seed, 9));
            engine.CastSpell(s, s.Party[1], new PlayerCommand(CommandKind.Cast, 0, SpellId: "conjure_animals", SlotLevel: 5));
            var unit = s.Lingerings.Single().Unit!;
            unit.Hp = 100000;   // 倒れないようにして、狙われた回数だけを数える
            s.Order.AddRange([.. s.Groups[0].Units]);
            int before = unit.Hp;
            for (int r = 0; r < 3; r++) engine.RunRound(s, new Dictionary<string, PlayerCommand>());
            return before - unit.Hp;
        }
        // 狼(知力 3)は召喚に食いつき、山賊の頭(知力 14)は召喚に目もくれない
        Assert.True(SummonHits("wolf", 1) > 0);
        Assert.Equal(0, SummonHits("bandit_captain", 1) + SummonHits("bandit_captain", 2));
    }

    [Fact]
    public void SummonsCanBeDestroyed()
    {
        var unit = new SummonUnit(Data.Monsters["dire_wolf"], 3);
        Assert.Equal(3, unit.Count);
        unit.Hp -= unit.HpEach;   // 1 体分のダメージで 1 体減る
        Assert.Equal(2, unit.Count);
        unit.Hp = 0;
        Assert.False(unit.Standing);
    }

    [Fact]
    public void EnemyMageCountersAFireball()
    {
        var wizard = Make("wizard", 7);
        var s = State([wizard], Data.Monsters["mage"], 1, hp: 500);
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "fireball", SlotLevel: 3));
        Assert.Contains(s.Log, e => e.Key is "combat.counter_ok" or "combat.counter_fail");
        Assert.True(s.Groups[0].Units[0].ReactionUsed);
        Assert.Equal(1, wizard.SlotsUsed[3]);   // 打ち消されても、唱えた側のスロットは減る
    }

    [Fact]
    public void HighLevelArcaneCastersPickCounterspell()
    {
        foreach (var cls in new[] { "wizard", "sorcerer" })
        {
            var c = Make(cls, 20);
            Assert.True(Core.Magic.Spellcasting.Castable(Data, c).Any(x => x.Id == "counterspell"),
                cls + ": known=" + string.Join(",", c.Book(cls).Known) + " prepared=" + string.Join(",", c.Book(cls).Prepared));
        }
    }

    [Fact]
    public void BossesHaveLegendaryResistance()
    {
        Assert.Equal(3, new MonsterUnit(Data.Monsters["lich"], 100).LegendaryResistLeft);
        Assert.Equal(0, new MonsterUnit(Data.Monsters["goblin"], 7).LegendaryResistLeft);
        // 縛る呪文を何度かけても、最初の3回の失敗は抵抗される
        var wizard = Make("wizard", 20);
        var s = State([wizard], Data.Monsters["adult_red_dragon"], 1, hp: 500);
        var engine = new CombatEngine(Data, new Pcg32(6, 6));
        for (int i = 0; i < 12 && !s.Groups[0].Units[0].Conditions.Any(); i++)
        {
            wizard.SlotsUsed.Clear();
            s.Party[0].Concentration = null;
            engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "hold_monster", SlotLevel: 9));
        }
        Assert.Equal(0, s.Groups[0].Units[0].LegendaryResistLeft);
        Assert.Equal(3, s.Log.Count(e => e.Key == "combat.legendary_resist"));
    }

    [Fact]
    public void MagicResistanceGivesAdvantageOnSavesAgainstSpells()
    {
        // 同じ乱数で、魔法への抵抗のあるサテュロスと、それを外したサテュロスに、セイクリッド・フレイム(敏捷力のセーヴ)を 300 回
        var satyr = Data.Monsters["satyr"];
        Assert.Contains("magic_resistance", satyr.Traits);
        int Saves()
        {
            var cleric = Make("cleric", 5);
            var s = State([cleric], satyr, 1, hp: 100000);
            var engine = new CombatEngine(Data, new Pcg32(8, 8));
            for (int i = 0; i < 300; i++) engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "sacred_flame", SlotLevel: 0));
            return s.Log.Count(e => e.Key == "combat.save_ok");
        }
        int with = Saves();
        satyr.Traits.Remove("magic_resistance");
        int without;
        try { without = Saves(); }
        finally { satyr.Traits.Add("magic_resistance"); }
        Assert.True(with > without + 30, $"抵抗あり {with} / なし {without}");
    }

    [Fact]
    public void TrollsRegenerateUnlessBurned()
    {
        var troll = Data.Monsters["troll"];
        Assert.Equal(10, troll.Regenerate);
        Assert.Contains("fire", troll.RegenerateStop);
        var s = State([Make("fighter", 10)], troll, 1, hp: 84);
        var engine = new CombatEngine(Data, new Pcg32(9, 9));
        var t = s.Groups[0].Units[0];
        s.Order.Add(t);   // トロルの番だけ回す
        t.Hp = 50;
        engine.RunRound(s, Dodge(s));
        Assert.Equal(60, t.Hp);   // 番の初めに 10 戻る
        engine.Damage(s, t, 5, "fire", crit: false, magical: false);
        engine.RunRound(s, Dodge(s));
        Assert.Equal(55, t.Hp);   // 火を受けた次の番は戻らない
        engine.RunRound(s, Dodge(s));
        Assert.Equal(65, t.Hp);   // その次からまた戻る
        Assert.Contains(s.Log, e => e.Key == "combat.regenerate");
    }
}

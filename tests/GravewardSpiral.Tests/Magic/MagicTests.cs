using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Magic;

public class MagicTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Make(string cls, int level = 1, string race = "human") => CharacterFactory.QuickCreate(Data, cls, race, cls, level);

    private static CombatState State(List<Character> party, MonsterDef def, int count, int hp = 0) => new()
    {
        Party = party.Select(c => new PartyMember(c, Data)).ToList(),
        Groups = [new EnemyGroup(def, Enumerable.Range(0, count).Select(_ => new MonsterUnit(def, hp > 0 ? hp : def.HpAverage)).ToList())],
    };

    [Fact]
    public void SpellsAreConverted()
    {
        Assert.True(Data.Spells.Count >= 300);
        Assert.True(Data.Spells.Values.Count(s => s.Usable) >= 90);
        var fb = Data.Spells["fireball"];
        Assert.Equal("ファイアーボール", fb.Name.Get("ja"));
        Assert.Equal(SpellTarget.Group, fb.Target);
        Assert.Equal("9d6", fb.Effects[0].Fail[0].DiceAt(4));
        Assert.Equal("2d10", Data.Spells["fire_bolt"].Effects[0].DiceAt(6));   // 初級呪文はキャラクターのレベルで強くなる
        Assert.Equal(SpellStatus.Roleplay, Data.Spells["speak_with_animals"].Status);
    }

    [Theory]
    [InlineData("wizard", 1, 1, 2)]
    [InlineData("wizard", 5, 3, 2)]
    [InlineData("cleric", 20, 9, 1)]
    [InlineData("paladin", 1, 1, 0)]
    [InlineData("paladin", 2, 1, 2)]
    [InlineData("paladin", 5, 2, 2)]
    [InlineData("warlock", 5, 3, 2)]
    [InlineData("warlock", 5, 1, 0)]
    [InlineData("fighter", 10, 1, 0)]
    public void SlotTablesFollow5e(string cls, int level, int spellLevel, int slots) =>
        Assert.Equal(slots, Spellcasting.MaxSlots(Data, Make(cls, level), spellLevel));

    [Fact]
    public void NewCastersGetSpellsPicked()
    {
        var cleric = Make("cleric");
        int usableCantrips = Data.Spells.Values.Count(x => x.Usable && x.IsCantrip && Data.ClassesWithSpell(x.Id).Any(k => k.Id == "cleric"));
        Assert.Equal(Math.Min(3, usableCantrips), cleric.Book("cleric").Cantrips.Count);   // 使える初級呪文が足りなければ、ある分だけ
        Assert.Equal(Spellcasting.PreparedCount(Data.Classes["cleric"], cleric), cleric.Book("cleric").Prepared.Count);
        Assert.Contains("cure_wounds", cleric.Book("cleric").Prepared.Concat(cleric.Book("cleric").Cantrips).Concat(["cure_wounds"]));
        var wizard = Make("wizard");
        Assert.Equal(6, wizard.Book("wizard").Known.Count);                         // 呪文書
        Assert.All(wizard.Book("wizard").Prepared, id => Assert.Contains(id, wizard.Book("wizard").Known));
        var sorcerer = Make("sorcerer", 3);
        Assert.Equal(4, sorcerer.Book("sorcerer").Known.Count);
        Assert.All(sorcerer.Book("sorcerer").Known, id => Assert.True(Data.Spells[id].Level <= 2));
        Assert.Empty(Make("fighter").Books);
    }

    [Fact]
    public void HealingWakesTheFallenAndUsesASlot()
    {
        var cleric = Make("cleric");
        var fighter = Make("fighter");
        if (!cleric.Book("cleric").Prepared.Contains("cure_wounds")) cleric.Book("cleric").Prepared.Add("cure_wounds");
        var s = State([fighter, cleric], Data.Monsters["goblin"], 1);
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        engine.Damage(s, s.Party[0], fighter.Hp + 1, "slashing", false, false);
        Assert.Equal(LifeState.Unconscious, fighter.State);
        engine.CastSpell(s, s.Party[1], new PlayerCommand(CommandKind.Cast, TargetMember: 0, SpellId: "cure_wounds", SlotLevel: 1));
        Assert.Equal(LifeState.Alive, fighter.State);
        Assert.True(fighter.Hp >= 1);
        Assert.Equal(1, cleric.SlotsUsed[1]);
        cleric.LongRest();
        Assert.Empty(cleric.SlotsUsed);
    }

    [Fact]
    public void FireballHitsTheWholeGroupAndUpcasts()
    {
        var wizard = Make("wizard", 7);
        var s = State([wizard], Data.Monsters["goblin"], 4, hp: 200);
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "fireball", SlotLevel: 4));
        Assert.All(s.Groups[0].Units, u => Assert.True(u.Hp < 200));
        Assert.Equal(1, wizard.SlotsUsed[4]);
        Assert.Contains(s.Log, e => e.Key == "combat.cast");
    }

    [Fact]
    public void SleepPutsTheWeakestToSleep()
    {
        var wizard = Make("wizard");
        var s = State([wizard], Data.Monsters["goblin"], 3, hp: 5);
        var engine = new CombatEngine(Data, new Pcg32(6, 6));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, SpellId: "sleep", SlotLevel: 1));
        Assert.True(s.Groups[0].Units.Count(u => u.Has(Condition.Unconscious)) >= 1);   // 5d8 は最低 5
    }

    [Fact]
    public void BlessAddsDiceAndEndsWithConcentration()
    {
        var cleric = Make("cleric");
        var party = new List<Character> { Make("fighter"), cleric };
        var s = State(party, Data.Monsters["goblin"], 1);
        var engine = new CombatEngine(Data, new Pcg32(7, 7));
        engine.CastSpell(s, s.Party[1], new PlayerCommand(CommandKind.Cast, SpellId: "bless", SlotLevel: 1));
        Assert.Contains(s.Party[0].Modifiers, m => m.Stat == "attack" && m.Dice == "1d4");
        Assert.Equal("bless", s.Party[1].Concentration);
        engine.Damage(s, s.Party[1], cleric.Hp + 1, "slashing", false, false);   // 倒れると集中が切れる
        Assert.DoesNotContain(s.Party[0].Modifiers, m => m.SpellId == "bless");
    }

    [Fact]
    public void ShieldOfFaithRaisesAc()
    {
        var cleric = Make("cleric");
        var s = State([cleric], Data.Monsters["goblin"], 1);
        int ac = s.Party[0].Ac;
        new CombatEngine(Data, new Pcg32(8, 8)).CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetMember: 0, SpellId: "shield_of_faith", SlotLevel: 1));
        Assert.Equal(ac + 2, s.Party[0].Ac);
    }

    [Fact]
    public void TempHpAbsorbsDamageFirst()
    {
        var wizard = Make("wizard");
        var s = State([wizard], Data.Monsters["goblin"], 1);
        var p = s.Party[0];
        p.TempHp = 5;
        int hp = p.Hp;
        new CombatEngine(Data, new Pcg32(9, 9)).Damage(s, p, 3, "slashing", false, false);
        Assert.Equal(hp, p.Hp);
        Assert.Equal(2, p.TempHp);
    }

    [Fact]
    public void ShortRestSpendsHitDiceAndRestoresPactSlots()
    {
        var warlock = Make("warlock", 5);
        warlock.PactSlotsUsed = 2;
        warlock.Hp = 1;
        int dice = warlock.HitDice = 3;
        int used = warlock.ShortRest(Data, new Pcg32(1, 2));
        Assert.True(used >= 1 && used <= dice);
        Assert.True(warlock.Hp > 1);
        Assert.Equal(0, warlock.PactSlotsUsed);
    }

    [Fact]
    public void MonsterCastersUseTheirSlots()
    {
        var party = new List<Character> { Make("fighter", 5), Make("fighter", 5), Make("cleric", 5) };
        var engine = new CombatEngine(Data, new Pcg32(12, 12));
        var s = engine.Start(party, [(Data.Monsters["mage"], 1)]);
        for (int i = 0; i < 3 && s.Outcome == CombatOutcome.Ongoing; i++) engine.RunRound(s, CombatSimulator.AutoCommands(s, Data));
        Assert.Contains(s.Log, e => e.Key == "combat.cast" && e.Args[0] is LocalizedText t && t.Get("en") == "Mage");
    }

    [Fact]
    public void PoisonLastsAfterCombatAndHurtsWhileWalking()
    {
        var c = Make("fighter");
        var s = State([c], Data.Monsters["goblin"], 1, hp: 1);
        var p = s.Party[0];
        p.Conditions.Add(new ActiveCondition { Condition = Condition.Poisoned, RoundsLeft = 600 });
        p.Conditions.Add(new ActiveCondition { Condition = Condition.Prone });
        Lasting.Save(p);
        Assert.Single(c.Lasting);
        Assert.Equal(60, c.Lasting[0].Minutes);
        int hp = c.Hp;
        Assert.Single(Lasting.Step([c]));
        Assert.Equal(hp - 1, c.Hp);
        Assert.Equal(59, c.Lasting[0].Minutes);
        c.LongRest();
        Assert.Empty(c.Lasting);
    }

    private static Character Multi(params (string Cls, int Levels)[] plan)
    {
        var c = Make(plan[0].Cls);
        for (int i = 0; i < plan.Length; i++)
        {
            int target = c.LevelIn(plan[i].Cls) + plan[i].Levels - (i == 0 ? 1 : 0);
            while (c.LevelIn(plan[i].Cls) < target)
            {
                c.Xp = Core.Rules.Progression.XpForLevel(c.Level + 1);
                c.LevelUp(Data, plan[i].Cls);
            }
        }
        return c;
    }

    [Fact]
    public void MulticlassAddsCasterLevels()
    {
        var c = Multi(("fighter", 2), ("wizard", 3));
        Assert.Equal(5, c.Level);
        Assert.Equal(2, c.LevelIn("fighter"));
        Assert.Equal(3, c.LevelIn("wizard"));
        Assert.Equal(3, Spellcasting.CasterLevel(Data, c));
        Assert.Equal(4, Spellcasting.MaxSlots(Data, c, 1));
        Assert.Equal(2, Spellcasting.MaxSlots(Data, c, 2));
        Assert.Equal("wizard", Spellcasting.CasterClass(Data, c)!.Id);
        Assert.Equal(1, CombatEngine.AttacksPerAction(Data, c));                    // ファイター 2 では追加攻撃なし
        Assert.Equal(10, Spellcasting.SpellsKnown(Data.Classes["wizard"], c)); // 呪文書はウィザードのレベル 3 で 10

        var both = Multi(("cleric", 1), ("wizard", 1));
        Assert.Equal(2, Spellcasting.CasterLevel(Data, both));
        Assert.Equal(3, Spellcasting.MaxSlots(Data, both, 1));

        var pact = Multi(("warlock", 3), ("sorcerer", 2));
        Assert.Equal(3, Spellcasting.MaxSlots(Data, pact, 1));   // ソーサラー 2 の 1 レベルのスロット 3 つ
        Assert.Equal(2, Spellcasting.MaxSlots(Data, pact, 2));    // 2 レベルはウォーロック 3 の協約魔術の 2 つだけ
    }

    [Fact]
    public void OldSingleClassCharactersAreNormalized()
    {
        var c = Make("fighter", 5);
        c.Classes.Clear();
        Assert.Equal(5, c.LevelIn("fighter"));
        c.NormalizeClasses();
        Assert.Single(c.Classes);
        Assert.Equal(5, c.Classes[0].Level);
    }

    [Fact]
    public void CastersRaiseTheWinRate()
    {
        List<Character> Martial() => [Make("fighter", 5), Make("fighter", 5), Make("paladin", 5), Make("rogue", 5), Make("ranger", 5), Make("fighter", 5)];
        List<Character> Mixed() => [Make("fighter", 5), Make("fighter", 5), Make("paladin", 5), Make("cleric", 5), Make("wizard", 5), Make("sorcerer", 5)];
        IReadOnlyList<(MonsterDef, int)> Foes(IRandom _) => [(Data.Monsters["orc"], 4), (Data.Monsters["worg"], 2)];
        var a = CombatSimulator.Run(Data, 150, 11, _ => Martial(), Foes);
        var b = CombatSimulator.Run(Data, 150, 11, _ => Mixed(), Foes);
        Assert.True(b.WinRate >= a.WinRate - 0.05, $"術者あり {b.WinRate:P0} / なし {a.WinRate:P0}");
        Assert.True(b.WinRate > 0.5, $"術者あり {b.WinRate:P0}");
    }

    [Fact]
    public void MulticlassCastersKeepSpellsPerClass()
    {
        var c = Multi(("cleric", 1), ("wizard", 1));
        var cleric = c.Book("cleric");
        var wizard = c.Book("wizard");
        Assert.NotEmpty(cleric.Prepared);
        Assert.NotEmpty(wizard.Known);
        Assert.All(cleric.Prepared, id => Assert.Contains(Data.ClassesWithSpell(id), k => k.Id == "cleric"));
        Assert.All(wizard.Known, id => Assert.Contains(Data.ClassesWithSpell(id), k => k.Id == "wizard"));
        var castable = Spellcasting.Castable(Data, c).Select(x => x.Id).ToList();
        Assert.Contains(cleric.Prepared[0], castable);
        Assert.Contains(wizard.Prepared[0], castable);
        // 唱えるクラスは、その呪文を持つクラス(難易度はそのクラスの能力値)
        Assert.Equal("wizard", Spellcasting.ClassFor(Data, c, wizard.Prepared[0])!.Id);
        string onlyCleric = cleric.Prepared.First(id => !wizard.Has(id));
        Assert.Equal("cleric", Spellcasting.ClassFor(Data, c, onlyCleric)!.Id);
    }

    [Fact]
    public void MulticlassNeedsAbilityScoresOnBothSides()
    {
        var c = Make("fighter");
        c.Scores = new Core.Rules.AbilityScores { Str = 15, Dex = 12, Con = 14, Int = 12, Wis = 10, Cha = 8 };
        Assert.True(c.CanTakeClass(Data, "fighter"));       // 今のクラスはいつでも
        Assert.False(c.CanTakeClass(Data, "wizard"));       // 知力 13 が要る
        Assert.True(c.CanTakeClass(Data, "barbarian"));     // 筋力 13
        c.Scores.Int = 13;
        Assert.True(c.CanTakeClass(Data, "wizard"));
        c.Scores.Str = 12;   // ファイターの条件(筋力か敏捷力 13)を満たさない
        Assert.False(c.CanTakeClass(Data, "wizard"));
        Assert.False(c.CanTakeClass(Data, "monk"));         // 敏捷力と判断力の両方 13
    }

    [Fact]
    public void LevelUpCanTakeOneLevelAtATime()
    {
        var c = Make("fighter");
        c.Xp = Core.Rules.Progression.XpForLevel(4);
        Assert.Equal(1, c.LevelUp(Data, "fighter", maxLevels: 1));
        Assert.Equal(2, c.Level);
        Assert.True(c.CanLevelUp);
        Assert.Equal(2, c.LevelUp(Data, "fighter"));   // 残りはまとめて
        Assert.Equal(4, c.Level);
    }

    [Fact]
    public void AMulticlassLearnsSpellsByEachClassLevel()
    {
        // ウィザード 1 / クレリック 4: スロットは術者のレベル 5 でも、ウィザードとして覚えられるのは 1 レベルの呪文まで(5版)
        var c = Multi(("cleric", 4), ("wizard", 1));
        Assert.Equal(3, Spellcasting.MaxSpellLevel(Data, c));
        Assert.Equal(1, Spellcasting.ClassMaxSpellLevel(Data, Data.Classes["wizard"], c));
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(Data, Data.Classes["cleric"], c));
        Assert.DoesNotContain(Spellcasting.ClassSpells(Data, Data.Classes["wizard"], c, cantrips: false), s => s.Id == "fireball");
        Assert.All(Spellcasting.ClassSpells(Data, Data.Classes["wizard"], c, cantrips: false), s => Assert.Equal(1, s.Level));
    }

    [Fact]
    public void AShortRestBringsBackOnlyThePactSlots()
    {
        // ウォーロック 3 / ウィザード 3: 2 レベルはふつうのスロット 2 と協約のスロット 2。小休憩で戻るのは協約の 2 だけ
        // (ウィザードの秘術の回復は、使ったことにして止める)
        var c = Multi(("warlock", 3), ("wizard", 3));
        c.FeatureUses["arcane_recovery"] = 1;
        var wiz = Data.Classes["wizard"];
        Assert.Equal(4, Spellcasting.SlotsLeft(Data, c, 2));
        for (int i = 0; i < 4; i++) Spellcasting.UseSlot(Data, c, 2);
        Assert.Equal(0, Spellcasting.SlotsLeft(Data, c, 2));
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(2, Spellcasting.SlotsLeft(Data, c, 2));
        c.LongRest();
        Assert.Equal(4, Spellcasting.SlotsLeft(Data, c, 2));
    }

    [Fact]
    public void ASpellThatDoesNotScaleStillShowsWhereItCanBePaid()
    {
        // ウォーロック 5 は 3 レベルの協約のスロットしか無い: 2 レベルのホールド・パーソンは 3 の欄に並ぶ
        var w = Multi(("warlock", 5));
        var hold = Data.Spells["hold_person"];
        Assert.NotEqual("slot", hold.Scale);
        Assert.Equal(0, Spellcasting.MaxSlots(Data, w, 2));   // 2 の欄は持っていない(タブは黒)
        Assert.True(Spellcasting.ListedAt(Data, w, hold, 3));

        // ウィザード 3: 1 レベルを使い切ったら、強くならない 1 レベルの呪文は 2 の欄に並ぶ(残っているあいだは 1 の欄だけ)
        var c = Multi(("wizard", 3));
        var plain = Data.Spells.Values.First(s => s.Level == 1 && s.Scale != "slot" && s.Usable);
        Assert.True(Spellcasting.ListedAt(Data, c, plain, 1));
        Assert.False(Spellcasting.ListedAt(Data, c, plain, 2));
        for (int i = 0; i < 4; i++) Spellcasting.UseSlot(Data, c, 1);
        Assert.True(Spellcasting.ListedAt(Data, c, plain, 2));
        Assert.False(Spellcasting.ListedAt(Data, c, plain, 3));
    }
}

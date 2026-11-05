using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Items;

/// <summary>同調の要る魔法の武器の、当たったときのダメージ(フレイム・タンの火)。</summary>
public class FlameTongueTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void PaladinWithFlameTongueAddsFire()
    {
        var c = CharacterFactory.QuickCreate(Data, "P", "human", "paladin", 12);
        c.Inventory.Items.Add(new ItemInstance { ItemId = "flame_tongue", Identified = true });
        Assert.Equal(EquipResult.Ok, c.Inventory.Equip(c.Inventory.Items.Count - 1, EquipSlot.MainHand, Data.Items));
        Assert.True(c.Inventory[EquipSlot.MainHand]!.Attuned);
        bool fire = false;
        for (ulong seed = 1; seed <= 20 && !fire; seed++)
        {
            var engine = new CombatEngine(Data, new Pcg32(seed, 3));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Attack, TargetGroup: 0) });
            fire |= s.Log.Any(e => e.Key == "combat.damage" && e.Args.Length >= 3 && Equals(e.Args[2], "fire"));
        }
        Assert.True(fire);
    }
}

/// <summary>HP の上限の無い kill(プレーン・シフト)。</summary>
public class PlaneShiftTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void KillWithoutMaxHpSlaysAnyHpButNotBosses()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 13);
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        var ogre = s.Groups[0].Units[0];
        engine.ApplyTo(s, s.Party[0], ogre, [new GravewardSpiral.Core.Effects.EffectPart { Type = "kill" }]);
        Assert.False(ogre.Standing);
        // ボス(名のある敵も)には効かない
        var boss = new MonsterUnit(Data.Monsters["ogre"], 59, MonsterScale.None with { Boss = true });
        s.Groups[0].Units.Add(boss);
        engine.ApplyTo(s, s.Party[0], boss, [new GravewardSpiral.Core.Effects.EffectPart { Type = "kill" }]);
        Assert.True(boss.Standing);
    }
}

/// <summary>利き手の投げる武器は投げない(戻ってくる武器だけは投げる)。</summary>
public class MainHandThrowTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Theory]
    [InlineData("javelin", false)]
    [InlineData("spear", false)]
    [InlineData("returning_dagger", true)]
    public void MainHandThrownOnlyIfItReturns(string id, bool thrown)
    {
        var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 5);
        c.Inventory.Equipped.Remove(EquipSlot.Ranged);
        c.Inventory.Equipped[EquipSlot.MainHand] = new ItemInstance { ItemId = id, Identified = true };
        var s = new CombatEngine(Data, new Pcg32(1, 1)).Start([c], [(Data.Monsters["goblin"], 1)]);
        Assert.Equal(thrown, s.Party[0].WeaponChoices(melee: false).Any());
    }
}

/// <summary>キャンプでは、準備していなくても準備できる呪文を唱えられる。</summary>
public class CampCastTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void ClericCastsUnpreparedClassSpellsAtCamp()
    {
        var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 5);
        c.Books["cleric"].Prepared.Clear();
        Assert.DoesNotContain(GravewardSpiral.Core.Magic.Spellcasting.Castable(Data, c), s => s.Id == "lesser_restoration");
        var camp = GravewardSpiral.Core.Magic.Spellcasting.CampCastable(Data, c).ToList();
        Assert.Contains(camp, s => s.Id == "lesser_restoration");
        Assert.DoesNotContain(camp, s => s.Level > 3);   // L5 のクレリックは 3 レベルの呪文まで
    }

    [Fact]
    public void SorcererStillCastsOnlyKnownSpellsAtCamp()
    {
        var c = CharacterFactory.QuickCreate(Data, "S", "human", "sorcerer", 5);
        Assert.Equal(GravewardSpiral.Core.Magic.Spellcasting.Castable(Data, c).Select(s => s.Id), GravewardSpiral.Core.Magic.Spellcasting.CampCastable(Data, c).Select(s => s.Id));
    }

    [Fact]
    public void CampOnlySpellsAreNotPreparedButCastAtCamp()
    {
        var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 5);
        var cleric = Data.Classes["cleric"];
        Assert.DoesNotContain(GravewardSpiral.Core.Magic.Spellcasting.PreparePool(Data, cleric, c), s => s.Id == "prayer_of_healing");
        Assert.Contains(GravewardSpiral.Core.Magic.Spellcasting.PreparePool(Data, cleric, c), s => s.Id == "lesser_restoration");
        c.Books["cleric"].Prepared.Add("prayer_of_healing");   // 前の版で準備していた分は、大休憩で外れる
        c.LongRest(data: Data);
        Assert.DoesNotContain("prayer_of_healing", c.Books["cleric"].Prepared);
        Assert.Contains(GravewardSpiral.Core.Magic.Spellcasting.CampCastable(Data, c), s => s.Id == "prayer_of_healing");
    }
}

/// <summary>キャンプで唱えた長い呪文の効き目は、戦闘をまたいで続く。</summary>
public class LastingBuffTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static void CampCast(Character caster, string spell, int slot)
    {
        var state = new CombatState { Party = [new PartyMember(caster, Data)], Groups = [] };
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        foreach (var p in state.Party) Lasting.Load(p);
        engine.CastSpell(state, state.Party[0], new PlayerCommand(CommandKind.Cast, -1, 0, spell, slot));
        foreach (var p in state.Party) Lasting.Save(p);
    }

    [Fact]
    public void MageArmorLastsIntoTheNextFightUntilALongRest()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 15);
        CampCast(c, "mage_armor", 1);
        var buff = Assert.Single(c.Buffs, b => b.SpellId == "mage_armor");
        Assert.Null(buff.Minutes);   // 8時間は、大休憩か町に戻るまで
        for (int i = 0; i < 200; i++) Lasting.Step([c]);
        var s = new CombatEngine(Data, new Pcg32(2, 2)).Start([c], [(Data.Monsters["goblin"], 1)]);
        Assert.Contains(s.Party[0].Modifiers, m => m.SpellId == "mage_armor");
        c.LongRest(data: Data);
        Assert.Empty(c.Buffs);
    }

    [Fact]
    public void FreedomOfMovementEndsAfterAnHour()
    {
        var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 15);
        CampCast(c, "freedom_of_movement", 4);
        var buff = Assert.Single(c.Buffs, b => b.SpellId == "freedom_of_movement" && b.Stat == "immune:paralyzed");
        Assert.InRange(buff.Minutes!.Value, 59, 60);
        for (int i = 0; i < 61; i++) Lasting.Step([c]);
        Assert.Empty(c.Buffs);
    }

    [Fact]
    public void ConcentrationSpellsDoNotLast()
    {
        var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 15);
        c.Books["cleric"].Prepared.Add("shield_of_faith");
        CampCast(c, "shield_of_faith", 1);
        Assert.Empty(c.Buffs);
    }
}

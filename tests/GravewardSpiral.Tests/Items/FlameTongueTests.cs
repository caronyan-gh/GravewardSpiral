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

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>レビューの指摘(G05〜G07、E02)の修正。</summary>
public class ReviewFixTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static (CombatEngine Engine, CombatState State, PartyMember P) Fight(ulong seed = 1)
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "cleric", 3);
        var engine = new CombatEngine(Data, new Pcg32(seed, seed));
        var s = engine.Start([c], [(Data.Monsters["goblin"], 1)]);
        return (engine, s, s.Party[0]);
    }

    [Fact]
    public void DamageTakenByTempHpStillCallsForAConcentrationSave()
    {
        // 一時的な HP で受けきっても、集中の判定はする(難易度 10 なら、耐久力 +2 のクレリックはたまに落ちる)
        int broke = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var (engine, s, p) = Fight(seed);
            p.Concentration = "bless";
            p.TempHp = 10;
            engine.Damage(s, p, 8, "slashing", crit: false, magical: false);
            Assert.Equal(2, p.TempHp);
            if (p.Concentration == null) broke++;
        }
        Assert.True(broke > 0);
    }

    [Fact]
    public void BeingParalyzedEndsConcentration()
    {
        var (engine, s, p) = Fight();
        p.Concentration = "bless";
        engine.ApplyToParty(s, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Paralyzed, Rounds = 2 }]);
        Assert.True(p.Has(Condition.Paralyzed));
        Assert.Null(p.Concentration);
    }

    [Fact]
    public void ResistanceFromTwoSourcesCountsOnce()
    {
        // 生まれつき火に抵抗のある敵に、呪文の火への抵抗を重ねても 1/2 のまま
        var monster = Data.Monsters.Values.First(m => m.Resist.Contains("fire") && m.Usable);
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var party = new List<Character> { CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 3) };
        var s = engine.Start(party, [(monster, 1)]);
        var m = s.Groups[0].Alive.First();
        int before = m.Hp;
        m.Modifiers.Add(new ActiveModifier { Stat = "resist:fire" });
        engine.Damage(s, m, 4, "fire", crit: false, magical: true);
        Assert.Equal(before - 2, m.Hp);   // 1/4 なら 1 しか減らない
    }

    [Fact]
    public void ResistanceAndVulnerabilityOnGearStillCountOnceWithAnotherResistance()
    {
        // 装備で抵抗と脆弱が両方あると倍率は 1。そこへ呪文の抵抗を足しても、抵抗1回・脆弱1回のまま(再レビューの R02)
        var hero = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 3);
        hero.Inventory.Equipped[Core.Items.EquipSlot.Armor] = Core.Items.ItemInstance.Of("armor_of_resistance_fire");
        hero.Inventory.Equipped[Core.Items.EquipSlot.Armor]!.Attuned = true;
        hero.Inventory.Equipped[Core.Items.EquipSlot.Cloak] = Core.Items.ItemInstance.Of("cursed_cloak_of_the_moth");
        hero.MaxHp = hero.Hp = 100;
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start([hero], [(Data.Monsters["goblin"], 1)]);
        var p = s.Party[0];
        Assert.Equal(1, p.DamageMultiplier("fire", magical: true));
        p.Modifiers.Add(new ActiveModifier { Stat = "resist:fire" });
        engine.Damage(s, p, 20, "fire", crit: false, magical: true);
        Assert.Equal(80, p.Hp);
    }

    [Fact]
    public void ARunLoopIsCaughtByTheCheckAndStoppedAtRunTime()
    {
        var loop = System.Text.Json.JsonSerializer.Deserialize<EventDef>("""{ "id": "review_cycle", "steps": [{ "type": "run", "id": "review_cycle" }] }""", ContentLibrary.JsonOptions)!;
        var data = GameData.Load(Content);
        data.Events[loop.Id] = loop;
        Assert.Contains(EventCheck.Check(data, loop), p => p.Contains("輪"));
        // 選択肢の中の run は、選ぶたびに止まるので輪にしない
        var menu = System.Text.Json.JsonSerializer.Deserialize<EventDef>("""{ "id": "menu", "steps": [{ "type": "choice", "options": [{ "label": { "en": "Again", "ja": "もう一度" }, "steps": [{ "type": "run", "id": "menu" }] }] }] }""", ContentLibrary.JsonOptions)!;
        data.Events[menu.Id] = menu;
        Assert.DoesNotContain(EventCheck.Check(data, menu), p => p.Contains("輪"));
    }
}

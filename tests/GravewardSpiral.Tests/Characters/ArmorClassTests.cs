using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>AC の計算(鎧なしの防御、重装鎧と敏捷力)と、味方に向ける呪文で選べる人。</summary>
public class ArmorClassTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Bare(string cls, int level = 1)
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", cls, level);
        c.Inventory.Equipped.Clear();
        return c;
    }

    private static void Hold(Character c, EquipSlot slot, string itemId) => c.Inventory.Equipped[slot] = new ItemInstance { ItemId = itemId, Identified = true };

    [Fact]
    public void AMonkKeepsUnarmoredDefenseWithAnOffHandWeaponButNotAShield()
    {
        var c = Bare("monk");
        int bare = c.ArmorClass(Data);
        Assert.Equal(10 + c.Scores.Mod(Ability.Dex) + c.Scores.Mod(Ability.Wis), bare);
        Hold(c, EquipSlot.OffHand, "dagger");
        Assert.Equal(bare, c.ArmorClass(Data));   // もう片方の手の武器では消えない
        Hold(c, EquipSlot.OffHand, "shield");
        Assert.Equal(10 + c.Scores.Mod(Ability.Dex) + Data.Items["shield"].ArmorClass, c.ArmorClass(Data));   // 盾で消える
    }

    [Fact]
    public void UnarmoredDefenseComesFromTheFirstClassThatHasIt()
    {
        // ファイターで始めて、あとからバーバリアンを取っても付く
        var c = Bare("fighter");
        c.Classes.Add(new ClassLevel { ClassId = "barbarian", Level = 1 });
        c.Level = 2;
        Assert.Equal(10 + c.Scores.Mod(Ability.Dex) + c.Scores.Mod(Ability.Con), c.ArmorClass(Data));
        // バーバリアンのあとでモンクを取っても、判断力の方には変わらない
        c.Classes.Add(new ClassLevel { ClassId = "monk", Level = 1 });
        c.Level = 3;
        Assert.Equal(10 + c.Scores.Mod(Ability.Dex) + c.Scores.Mod(Ability.Con), c.ArmorClass(Data));
    }

    [Fact]
    public void HeavyArmorIgnoresANegativeDexterity()
    {
        var c = Bare("fighter");
        c.Scores[Ability.Dex] = 8;   // 修正 -1
        Hold(c, EquipSlot.MainHand, "longsword");   // 防御のスタイル(鎧で +1)にならないように武器を持つ
        Hold(c, EquipSlot.Armor, "plate_armor");
        Assert.Equal(Data.Items["plate_armor"].ArmorClass, c.ArmorClass(Data));
        Hold(c, EquipSlot.Armor, "scale_mail");   // 中装鎧はマイナスも足す
        Assert.Equal(Data.Items["scale_mail"].ArmorClass - 1, c.ArmorClass(Data));
    }

    [Fact]
    public void ReviveSpellsTargetOnlyTheDead()
    {
        Assert.True(CombatEngine.AllyTargetable(Data.Spells["revivify"], LifeState.Dead));
        Assert.False(CombatEngine.AllyTargetable(Data.Spells["revivify"], LifeState.Alive));
        Assert.False(CombatEngine.AllyTargetable(Data.Spells["cure_wounds"], LifeState.Dead));
        Assert.True(CombatEngine.AllyTargetable(Data.Spells["cure_wounds"], LifeState.Unconscious));
    }

    [Fact]
    public void MageArmorSetsTheUnarmoredBaseAndBarkskinSetsAFloor()
    {
        var c = Bare("wizard");
        var p = new PartyMember(c, Data);
        int dex = c.Scores.Mod(Ability.Dex);
        Assert.Equal(10 + dex, p.Ac);
        p.Modifiers.Add(new ActiveModifier { Stat = "ac_unarmored", Value = 13 });
        Assert.Equal(13 + dex, p.Ac);   // 鎧を着ていなければ 13 + 敏捷力
        Hold(c, EquipSlot.Armor, "leather_armor");
        Assert.Equal(Data.Items["leather_armor"].ArmorClass + dex, p.Ac);   // 鎧を着ていれば効かない
        p.Modifiers.Add(new ActiveModifier { Stat = "ac_min", Value = 16 });
        Assert.Equal(16, p.Ac);   // バークスキン: 16 より下がらない
        // モンクの鎧なしの防御の方が高ければ、そちら(足し算にしない)
        var m = Bare("monk");
        m.Scores[Ability.Wis] = 18;
        var pm = new PartyMember(m, Data);
        int monk = pm.Ac;
        pm.Modifiers.Add(new ActiveModifier { Stat = "ac_unarmored", Value = 13 });
        Assert.Equal(monk, pm.Ac);
    }

    [Fact]
    public void ASecondCopyOfTheSameAttunedItemDoesNotWork()
    {
        var c = Bare("fighter");
        c.Inventory.Items.Add(new ItemInstance { ItemId = "ring_of_protection", Identified = true });
        c.Inventory.Items.Add(new ItemInstance { ItemId = "ring_of_protection", Identified = true });
        Assert.Equal(EquipResult.Ok, c.Inventory.Equip(c.Inventory.Items.FindIndex(i => i.ItemId == "ring_of_protection"), EquipSlot.Ring1, Data.Items));
        Assert.Equal(EquipResult.Ok, c.Inventory.Equip(c.Inventory.Items.FindIndex(i => i.ItemId == "ring_of_protection"), EquipSlot.Ring2, Data.Items));
        Assert.True(c.Inventory[EquipSlot.Ring1]!.Attuned);
        Assert.False(c.Inventory[EquipSlot.Ring2]!.Attuned);
        Assert.Single(c.Inventory.Active(Data.Items), x => x.Def.Id == "ring_of_protection");
        // 前のセーブで2つとも同調していても、効くのは1つ
        c.Inventory[EquipSlot.Ring2]!.Attuned = true;
        Assert.Single(c.Inventory.Active(Data.Items), x => x.Def.Id == "ring_of_protection");
    }
}

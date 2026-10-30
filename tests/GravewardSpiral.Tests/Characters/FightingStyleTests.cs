using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;

namespace GravewardSpiral.Tests.Characters;

/// <summary>ファイティングスタイル(装備で自動。撃つ番はいつも弓術)。</summary>
public class FightingStyleTests
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
    public void WithNoWeaponAndNoArmorNoStyleWorks()
    {
        var c = Bare("fighter");
        Assert.Empty(ClassFeatures.ActiveStyles(Data, c));
        Hold(c, EquipSlot.Armor, "chain_mail");
        Assert.Equal(["style_defense"], ClassFeatures.ActiveStyles(Data, c));   // 武器なしで鎧なら防御
    }

    [Fact]
    public void AMeleeWeaponWithABowShowsBothStylesAndShotsGetArchery()
    {
        var c = Bare("fighter");
        Hold(c, EquipSlot.MainHand, "longsword");
        Hold(c, EquipSlot.OffHand, "shield");
        Hold(c, EquipSlot.Ranged, "longbow");
        Assert.Equal(["style_dueling", "style_archery"], ClassFeatures.ActiveStyles(Data, c));
        Assert.True(ClassFeatures.ArcheryOnShot(Data, c));   // 撃つ番は、利き手が剣でも弓術
    }

    [Fact]
    public void ACopiedFighterGetsItsStylesFromTheData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", """[{"id": "test_fighter", "extends": "fighter"}]""");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var c = CharacterFactory.QuickCreate(data, "T", "human", "test_fighter", 1);
        c.Inventory.Equipped.Clear();
        Hold(c, EquipSlot.MainHand, "longsword");
        Hold(c, EquipSlot.OffHand, "shield");
        Assert.Equal(["style_dueling"], ClassFeatures.ActiveStyles(data, c));
        var pm = new Core.Combat.PartyMember(c, data);
        Assert.Equal(2, Core.Combat.FeatureChecks.MainMeleeDamageBonus(data, pm));
        Assert.Equal(0, Core.Combat.FeatureChecks.RangedShotHitBonus(data, pm));   // 飛び道具を持っていない
        Hold(c, EquipSlot.Ranged, "longbow");
        Assert.Equal(2, Core.Combat.FeatureChecks.RangedShotHitBonus(data, pm));
        Assert.DoesNotContain(ClassFeatures.Passives(data, c), id => id.StartsWith("style_"));   // 能力の一覧には出さない(専用の行に出す)
    }

    [Fact]
    public void OnlyClassesWithAStyleGetArchery()
    {
        Assert.False(ClassFeatures.ArcheryOnShot(Data, Bare("rogue", 5)));
        Assert.False(ClassFeatures.ArcheryOnShot(Data, Bare("paladin", 1)));   // パラディンは L2 から
        Assert.True(ClassFeatures.ArcheryOnShot(Data, Bare("ranger", 2)));
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

public class CharacterTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static AbilityScores Scores(int str, int dex, int con, int i, int wis, int cha) =>
        new() { Str = str, Dex = dex, Con = con, Int = i, Wis = wis, Cha = cha };

    private static Character Make(string race, string cls, AbilityScores? scores = null, string bg = "soldier",
        IReadOnlyList<Ability>? free = null, IReadOnlyList<string>? skills = null, IReadOnlyList<string>? freeSkills = null)
    {
        var c = Data.Classes[cls];
        var r = Data.Races[race];
        var fixedSkills = r.Skills.Concat(Data.Backgrounds[bg].Skills).ToHashSet();
        var picks = skills ?? Data.SkillChoicesFor(c).Where(s => !fixedSkills.Contains(s)).Take(c.SkillChoices).ToList();
        var freeSk = freeSkills ?? Data.Skills.Keys.Order().Where(s => !fixedSkills.Contains(s) && !picks.Contains(s)).Take(r.FreeSkills).ToList();
        var freeAb = free ?? Abilities.All.Where(a => !r.AbilityBonus.ContainsKey(a.Code())).Take(r.FreeAbilityPoints).ToList();
        var choices = new CharacterChoices("テスト", race, cls, bg, Alignment.Neutral, scores ?? Scores(15, 14, 13, 12, 10, 8), freeAb, picks, freeSk);
        return CharacterFactory.Create(Data, choices);
    }

    [Fact]
    public void PackDataLoadsWithoutProblems()
    {
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        Assert.Equal(9, Data.Races.Count);
        Assert.Equal(12, Data.Classes.Count);
        Assert.Equal(18, Data.Skills.Count);
        Assert.Equal("ファイター", Data.Classes["fighter"].Name.Get("ja"));
    }

    [Theory]
    [InlineData(1, -5)]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(15, 2)]
    [InlineData(20, 5)]
    public void ModifierFollows5e(int score, int mod) => Assert.Equal(mod, Abilities.Modifier(score));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    [InlineData(40, 6)]
    public void ProficiencyBonusIsCapped(int level, int bonus) => Assert.Equal(bonus, Progression.ProficiencyBonus(level));

    [Fact]
    public void XpTableMatches5eAndContinuesPast20()
    {
        Assert.Equal(300, Progression.XpForLevel(2));
        Assert.Equal(355000, Progression.XpForLevel(20));
        Assert.Equal(415000, Progression.XpForLevel(21));
        Assert.Equal(480000, Progression.XpForLevel(22));
        Assert.Equal(20, Progression.LevelForXp(355000));
        Assert.Equal(1, Progression.LevelForXp(299));
    }

    [Fact]
    public void FighterHasRaceBonusHpAndStartingGear()
    {
        var c = Make("dwarf", "fighter", Scores(15, 14, 13, 12, 10, 8));
        Assert.Equal(15, c.Scores.Con);          // 13 + 2
        Assert.Equal(11, c.Scores.Wis);          // 10 + 1(丘ドワーフ)
        Assert.Equal(10 + 2 + 1, c.MaxHp);       // d10 + 耐久力 +2 + 丘ドワーフ +1
        Assert.Equal("longsword", c.Inventory[EquipSlot.MainHand]!.ItemId);
        Assert.Equal("shield", c.Inventory[EquipSlot.OffHand]!.ItemId);
        Assert.Equal("chain_mail", c.Inventory[EquipSlot.Armor]!.ItemId);
        Assert.Equal("sack", c.Inventory[EquipSlot.Backpack]!.ItemId);
        Assert.Equal(16 + 2, c.ArmorClass(Data));   // チェインメイルは敏捷力を足さない、盾 +2
        Assert.Contains("athletics", c.Skills);     // 兵士の背景
        Assert.Equal(125, c.Gold);                  // 5d4 の平均 12.5 × 10(ランダムにしない)
    }

    [Fact]
    public void UnarmoredDefenseForBarbarianAndMonk()
    {
        var barb = Make("human", "barbarian", Scores(15, 14, 13, 8, 10, 12));
        Assert.Equal(10 + Abilities.Modifier(15) + Abilities.Modifier(14), barb.ArmorClass(Data));   // 10 + 敏捷 + 耐久
        var monk = Make("human", "monk", Scores(10, 15, 13, 8, 14, 12));
        Assert.Equal(10 + Abilities.Modifier(16) + Abilities.Modifier(15), monk.ArmorClass(Data));   // 10 + 敏捷 + 判断
    }

    [Fact]
    public void MediumArmorCapsDexterity()
    {
        var cleric = Make("elf", "cleric", Scores(12, 16, 13, 10, 15, 8));   // 敏捷 18(+4)
        Assert.Equal(14 + 2 + 2, cleric.ArmorClass(Data));                   // スケイル 14 + 敏捷は 2 まで + 盾 2
    }

    [Fact]
    public void HalfElfGetsFreeAbilitiesAndSkills()
    {
        var c = Make("half_elf", "wizard", Scores(8, 14, 13, 15, 12, 10), free: [Ability.Int, Ability.Con]);
        Assert.Equal(16, c.Scores.Int);
        Assert.Equal(14, c.Scores.Con);
        Assert.Equal(12, c.Scores.Cha);
        Assert.Equal(2 + 2 + 2, c.Skills.Count);   // クラス 2 + 背景 2 + 好きな技能 2
    }

    [Fact]
    public void ValidationCatchesBadChoices()
    {
        var bad = new CharacterChoices("", "elf", "rogue", "thief", Alignment.Evil, Scores(15, 14, 13, 12, 10, 8), [], ["arcana"], []);
        var p = CharacterFactory.Validate(Data, bad);
        Assert.Contains(p, s => s.Contains("名前"));
        Assert.Contains(p, s => s.Contains("4 つ"));
        Assert.Contains(p, s => s.Contains("選べない技能"));
        var dup = new CharacterChoices("ルカ", "elf", "fighter", "hunter", Alignment.Good, Scores(15, 14, 13, 12, 10, 8), [], ["perception", "athletics"], []);
        Assert.Contains(CharacterFactory.Validate(Data, dup), s => s.Contains("同じ技能"));
    }

    [Fact]
    public void LevelUpUsesAverageHp()
    {
        var c = Make("human", "fighter", Scores(15, 14, 13, 12, 10, 8));   // 耐久 14(+2)
        int before = c.MaxHp;
        Assert.Equal(0, c.LevelUp(Data));
        c.Xp = 900;
        Assert.Equal(2, c.LevelUp(Data));
        Assert.Equal(3, c.Level);
        Assert.Equal(before + 2 * (10 / 2 + 1 + 2), c.MaxHp);
    }

    [Fact]
    public void LongRestRestoresHpAndHitDice()
    {
        var c = Make("human", "fighter");
        c.Xp = 6500;
        c.LevelUp(Data);
        c.Hp = 1;
        c.HitDice = 0;
        c.LongRest();
        Assert.Equal(c.MaxHp, c.Hp);
        Assert.Equal(2, c.HitDice);   // レベル 5 の半分
        c.Hp = 1;
        c.LongRest(0.5);
        Assert.Equal((int)Math.Ceiling(c.MaxHp * 0.5), c.Hp);
    }

    [Fact]
    public void BagLimitsAndStacks()
    {
        var c = Make("human", "fighter");
        var inv = c.Inventory;
        Assert.Equal(6, inv.Capacity(Data.Items));
        for (int i = 0; i < 6; i++) Assert.True(inv.TryAdd(ItemInstance.Of("shortsword"), Data.Items));
        Assert.False(inv.TryAdd(ItemInstance.Of("shortsword"), Data.Items));
        inv.Items.RemoveAt(0);
        Assert.True(inv.TryAdd(ItemInstance.Of("potion_of_healing"), Data.Items));
        Assert.True(inv.TryAdd(ItemInstance.Of("potion_of_healing", 3), Data.Items));   // 重ねて持てる
        Assert.Equal(4, inv.Items.Single(i => i.ItemId == "potion_of_healing").Count);
    }

    [Fact]
    public void BackpackSwapNeedsRoomAndTwoHandedClearsOffHand()
    {
        var c = Make("human", "fighter");
        var inv = c.Inventory;
        Assert.True(inv.TryAdd(ItemInstance.Of("backpack"), Data.Items));
        int idx = inv.Items.FindIndex(i => i.ItemId == "backpack");
        Assert.Equal(EquipResult.Ok, inv.Equip(idx, EquipSlot.Backpack, Data.Items));
        Assert.Equal(10, inv.Capacity(Data.Items));
        for (int i = 0; i < 9; i++) inv.TryAdd(ItemInstance.Of("shortsword"), Data.Items);   // 袋の中が 10 個
        int sackIdx = inv.Items.FindIndex(i => i.ItemId == "sack");
        Assert.Equal(EquipResult.BagTooSmall, inv.Equip(sackIdx, EquipSlot.Backpack, Data.Items));

        var fighter = Make("human", "fighter");
        fighter.Inventory.TryAdd(ItemInstance.Of("greatsword"), Data.Items);
        int gs = fighter.Inventory.Items.FindIndex(i => i.ItemId == "greatsword");
        Assert.Equal(EquipResult.Ok, fighter.Inventory.Equip(gs, EquipSlot.MainHand, Data.Items));
        Assert.Null(fighter.Inventory[EquipSlot.OffHand]);                                  // 両手武器で盾を外す
        Assert.Contains(fighter.Inventory.Items, i => i.ItemId == "shield");
    }

    [Fact]
    public void PartyHoldsSixInOrder()
    {
        var roster = new Roster();
        var members = Enumerable.Range(0, 7).Select(_ => Make("human", "fighter")).ToList();
        roster.Characters.AddRange(members);
        for (int i = 0; i < 6; i++) Assert.True(roster.AddToParty(members[i]));
        Assert.False(roster.AddToParty(members[6]));
        roster.Swap(0, 5);
        Assert.Equal(members[5].Id, roster.Party[0]);
        // 新しい隊列: 選んだ順に前から、残りは今の順で後ろへ
        roster.Reorder([members[3], members[1]]);
        Assert.Equal(new[] { members[3], members[1], members[5], members[2], members[4], members[0] }.Select(m => m.Id), roster.Party);
        roster.Swap(0, 5);   // 以下の確かめのために戻す
        roster.Reorder([members[5]]);
        Assert.True(roster.IsFrontRow(2));
        Assert.False(roster.IsFrontRow(3));
        roster.Delete(members[5]);
        Assert.Equal(5, roster.Party.Count);
    }
}

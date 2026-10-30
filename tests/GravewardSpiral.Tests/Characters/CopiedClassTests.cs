using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Characters;

/// <summary>
/// 別の id のクラスで確かめる(docs/class-data-design.md の Step 7)。SRD の 12 クラスを、試しのパックで copy_&lt;id&gt; に写し(extends だけ)、
/// 記録と同じ書き出し(技・能力・回数・数・守り・戦闘の判定・呪文・罠)が、クラスの id のほかは同じになるかを見る。
/// コアにクラスの id の名指しが残っていれば、写したクラスだけ違う結果になる。
/// </summary>
public class CopiedClassTests
{
    private const string Prefix = "copy_";

    private static readonly ContentLibrary Content = MakeContent();
    private static readonly GameData Data = GameData.Load(Content);

    private static ContentLibrary MakeContent()
    {
        var srd = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        string dir = Path.Combine(Path.GetTempPath(), "gw_copied_classes_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), """{"id": "copies", "depends": ["srd"]}""");
        File.WriteAllText(Path.Combine(dir, "classes.json"),
            "[" + string.Join(",", srd.ClassOrder.Select(id => $$"""{"id": "{{Prefix}}{{id}}", "extends": "{{id}}"}""")) + "]");
        var content = ContentLibrary.Load(TestPaths.Content, dir);
        return content;
    }

    private static readonly string[] Srd = ["barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard"];

    public static IEnumerable<object[]> Classes() => Srd.Select(c => new object[] { c });

    [Fact]
    public void TheCopiesLoadWithoutProblems()
    {
        Assert.Empty(Content.Problems);
        Assert.All(Srd, id => Assert.True(Data.Classes.ContainsKey(Prefix + id)));
    }

    /// <summary>1 つのクラスの全レベル・2 つの能力値の組で、写したクラスの書き出しが、もとのクラスと id のほかは同じ。</summary>
    [Theory]
    [MemberData(nameof(Classes))]
    public void ACopiedClassBehavesLikeTheOriginal(string cls)
    {
        foreach (var (profile, scores) in ClassBehaviorRecordTests.ProfileList)
        {
            for (int lv = 1; lv <= 20; lv++)
            {
                string original = ClassBehaviorRecordTests.Describe(Data, profile, scores, "human", [(cls, lv)]);
                string copy = ClassBehaviorRecordTests.Describe(Data, profile, scores, "human", [(Prefix + cls, lv)]).Replace(Prefix, "");
                AssertSame(original, copy, $"{cls} {lv}");
            }
        }
    }

    /// <summary>マルチクラスも、全部のクラスを写したものに替えて同じ(スロットの合わせ方、協約、最初のクラスの鎧なしの防御など)。</summary>
    [Fact]
    public void CopiedMulticlassesBehaveLikeTheOriginals()
    {
        (string, int)[][] combos =
        [
            [("fighter", 5), ("wizard", 5)], [("monk", 5), ("barbarian", 5)], [("barbarian", 5), ("monk", 5)], [("paladin", 6), ("sorcerer", 4)],
            [("cleric", 3), ("wizard", 3)], [("warlock", 5), ("paladin", 2)], [("ranger", 4), ("druid", 4)], [("bard", 5), ("rogue", 7)],
            [("sorcerer", 3), ("warlock", 3), ("bard", 3)],
        ];
        foreach (var (profile, scores) in ClassBehaviorRecordTests.ProfileList)
        {
            foreach (var combo in combos)
            {
                string original = ClassBehaviorRecordTests.Describe(Data, profile, scores, "human", combo);
                string copy = ClassBehaviorRecordTests.Describe(Data, profile, scores, "human", combo.Select(x => (Prefix + x.Item1, x.Item2)).ToArray()).Replace(Prefix, "");
                AssertSame(original, copy, string.Join("/", combo.Select(x => x.Item1)));
            }
        }
    }

    private static void AssertSame(string original, string copy, string what)
    {
        if (original == copy) return;
        var a = original.Split('\n');
        var b = copy.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        Assert.Fail($"{what}: 写したクラスが違う({i + 1} 行目)\nもと: {(i < a.Length ? a[i] : "(無し)")}\n写し: {(i < b.Length ? b[i] : "(無し)")}");
    }

    private static Character Multi(params (string Id, int Level)[] levels)
    {
        var c = new Character
        {
            Id = "m", Name = "M", RaceId = "human", ClassId = levels[0].Id, Level = levels.Sum(x => x.Level),
            Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(), MaxHp = 50, Hp = 50,
        };
        foreach (var a in Core.Rules.Abilities.All) c.Scores[a] = 14;
        return c;
    }

    /// <summary>
    /// もとのクラスと写したクラスを同じ人が取ると、同じ id の技は1つ・回数は足さない(上限は大きいほう)・使えば共通の回数が減る。
    /// 小休憩で二重に戻らない。重なり方 first はクラスを取った順。呪文はクラスごとに持ち、唱える一覧で同じ呪文は1つ。
    /// </summary>
    [Fact]
    public void TheOriginalAndItsCopyShareFeatures()
    {
        var f = Multi(("fighter", 5), (Prefix + "fighter", 5));
        Assert.Single(ClassFeatures.ActiveFor(Data, f), x => x.Id == "second_wind");
        Assert.Equal(1, ClassFeatures.MaxUses(Data, f, "second_wind"));
        Assert.True(ClassFeatures.Spend(Data, f, "second_wind"));
        Assert.False(ClassFeatures.Spend(Data, f, "second_wind"));   // 別の取得元から使い直せない
        ClassFeatures.ShortRest(Data, f);
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, f, "second_wind"));
        Assert.Equal(5, ClassFeatures.SecondWindBonus(Data, f));   // 取得元のクラスのレベルで数える(足さない)

        // 鎧なしの防御(first): 先に取ったクラスのもの
        Assert.Contains("unarmored_defense_wis", ClassFeatures.Passives(Data, Multi(("monk", 3), (Prefix + "barbarian", 3))));
        Assert.Contains("unarmored_defense_con", ClassFeatures.Passives(Data, Multi((Prefix + "barbarian", 3), ("monk", 3))));

        // 呪文: スロットは合わせた術者のレベル、覚えられる呪文のレベルはそれぞれのクラスのレベル
        var w = Multi(("wizard", 3), (Prefix + "wizard", 3));
        Assert.Equal(6, Spellcasting.CasterLevel(Data, w));
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(Data, Data.Classes[Prefix + "wizard"], w));
        Spellcasting.AutoPick(Data, w);
        var castable = Spellcasting.Castable(Data, w).Select(s => s.Id).ToList();
        Assert.Equal(castable.Distinct().Count(), castable.Count);

        // 協約魔術: 2 つの協約のクラスのレベルを合わせる(ウォーロック 3 + 写し 3 は協約 6: 2 つの 3 レベル)
        var p = Multi(("warlock", 3), (Prefix + "warlock", 3));
        Assert.Equal((2, 3), (Spellcasting.PactSlots(Data, p), Spellcasting.PactSlotLevel(Data, p)));
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(Data, Data.Classes[Prefix + "warlock"], p));
    }

    /// <summary>写したクラスの人を保存して読み直すと、クラス・覚えた呪文・使った回数が残る。</summary>
    [Fact]
    public void ACopiedClassSurvivesSaving()
    {
        var request = new GenerationRequest(3, 2, FloorSizeMode.Fixed(12, 12));
        var session = GameSession.New(1, request, new MazeGenerator(MazeGenSettings.Load(Content)).Generate(request).Dungeon);
        var c = CharacterFactory.QuickCreate(Data, "Copy", "human", Prefix + "wizard", 5);
        session.Roster.Characters.Add(c);
        session.Roster.AddToParty(c);
        Spellcasting.AutoPick(Data, c);
        ClassFeatures.Spend(Data, c, "arcane_recovery");
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        store.Save(session, Content);
        var back = store.Load(session.ClanId, session.WorldKey).Roster.Characters.Single(x => x.Name == "Copy");
        Assert.Equal(Prefix + "wizard", back.ClassId);
        Assert.Equal(c.Book(Prefix + "wizard").Prepared, back.Book(Prefix + "wizard").Prepared);
        Assert.Equal(0, ClassFeatures.UsesLeft(Data, back, "arcane_recovery"));
        Assert.Equal(Spellcasting.Castable(Data, c).Select(s => s.Id), Spellcasting.Castable(Data, back).Select(s => s.Id));
    }

    /// <summary>
    /// 写しでない新しいクラスをパックで足す: 既にある技と能力・新しい能力(効果の部品だけで書く)・回数・3 分の 1 の術者・呪文の束・タグ・攻撃の回数。
    /// 作成・レベルの数・呪文・罠のタグが、データのとおりに動く。
    /// </summary>
    [Fact]
    public void APackCanAddANewClass()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "t_warding_steel", "kind": "passive", "when": "wearing_armor", "effects": [{"type": "ac_bonus", "value": 1}, {"type": "save_bonus", "ability": "int", "value": 2}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "t_spellblade", "name": {"en": "Spellblade", "ja": "魔剣士"}, "hit_die": 10, "tags": ["arcane"],
              "progression": {"attacks": {"by_level": {"1": 1, "6": 2}}},
              "pools": [{"id": "second_wind", "max": 1, "recharge": "short"}],
              "features": [{"id": "second_wind", "level": 1}, {"id": "style_dueling", "level": 1}, {"id": "t_warding_steel", "level": 2}],
              "primary": ["str", "int", "con"], "saves": ["str", "int"], "armor": ["light", "medium", "shield"], "weapons": ["simple", "martial"],
              "skill_choices": 2, "skills": ["arcana", "athletics", "history", "perception"], "start_gold": "4d4", "start_items": ["longsword", "scale_mail", "sack"],
              "spellcasting": {"ability": "int", "style": "known", "progression": "third", "from_level": 3,
                "cantrips": {"by_level": {"3": 2, "10": 3}}, "known": {"by_level": {"3": 3, "7": 5, "13": 8}},
                "spell_access": [{"lv": 3, "spelllv": 0, "set": "wizard_cantrips"}, {"lv": 3, "spelllv": 1, "set": "wizard_1"}, {"lv": 7, "spelllv": 2, "set": "wizard_2"}]},
              "description": {"en": "A test class.", "ja": "試しのクラス。"}}]
            """);
        // 新しい能力の名前(状態の画面とレベルアップの知らせに出す)。無いと読み込みの検査で落ちる
        dir.Write("p/text/ja.json", """{"passive.t_warding_steel": "守りの鋼"}""");
        var missing = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(missing);
        Assert.Contains(missing.Problems, x => x.Contains("'passive.t_warding_steel' が en にありません"));
        dir.Write("p/text/en.json", """{"passive.t_warding_steel": "Warding Steel"}""");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        Assert.Contains("t_spellblade", data.ClassOrder);

        var c = CharacterFactory.QuickCreate(data, "S", "human", "t_spellblade", 7);
        Assert.Equal(2, CombatEngine.AttacksPerAction(data, c));
        Assert.Contains(ClassFeatures.ActiveFor(data, c), f => f.Id == "second_wind");
        Assert.Equal(7, ClassFeatures.SecondWindBonus(data, c));   // このクラスのレベルで数える
        Assert.Contains("t_warding_steel", ClassFeatures.Passives(data, c));
        Assert.True(ClassFeatures.HasFightingStyle(data, c));   // スタイルは状態の画面の専用の行に出す(能力の一覧には出さない)
        Assert.Equal(3, Spellcasting.CasterLevel(data, c));   // 3 分の 1 の術者 7 は術者のレベル 3
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(data, data.Classes["t_spellblade"], c));
        Spellcasting.AutoPick(data, c);
        Assert.Equal(5, c.Book("t_spellblade").Known.Count);
        Assert.All(c.Book("t_spellblade").Known, id => Assert.Contains(id, data.SpellSets["wizard_1"].Spells.Concat(data.SpellSets["wizard_2"].Spells)));
        Assert.True(Core.Items.Traps.HasTag(data, c, "arcane"));   // 術者を狙う罠に当たる

        // 新しい能力(効果の部品だけで書いたもの)は、鎧を着ているあいだ AC に効く
        int armored = c.ArmorClass(data);
        var armor = c.Inventory[Core.Items.EquipSlot.Armor];
        Assert.NotNull(armor);
        Assert.Contains(PassiveRules.Effects(data, c, "ac_bonus"), e => e.Def.Id == "t_warding_steel");
        var pm = new PartyMember(c, data);
        Assert.Equal(c.SaveBonus(data, Core.Rules.Ability.Int) + 2, pm.SaveBonus(Core.Rules.Ability.Int));   // 知力のセーヴにだけ +2
        Assert.Equal(c.SaveBonus(data, Core.Rules.Ability.Wis), pm.SaveBonus(Core.Rules.Ability.Wis));
        c.Inventory.Equipped.Remove(Core.Items.EquipSlot.Armor);
        Assert.DoesNotContain(PassiveRules.Effects(data, c, "ac_bonus"), e => e.Def.Id == "t_warding_steel");
    }
}

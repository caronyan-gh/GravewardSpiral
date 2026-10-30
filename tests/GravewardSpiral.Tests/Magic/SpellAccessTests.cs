using System.Text.Json;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Magic;

/// <summary>呪文の扱いをデータで持つ形(クラスの spellcasting、spellsets.json、数の書き方)。docs/class-data-design.md の Step 2。</summary>
public class SpellAccessTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Multi(params (string Id, int Level)[] levels)
    {
        var c = new Character
        {
            RaceId = "human",
            ClassId = levels[0].Id,
            Level = levels.Sum(x => x.Level),
            Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(),
        };
        foreach (var a in Abilities.All) c.Scores[a] = 14;
        return c;
    }

    [Fact]
    public void TheStandardPackHasNoProblems() => Assert.Empty(Content.Problems);

    [Fact]
    public void AnUnpreparedSpellInTheSpellbookIsCastAsTheClassThatKnowsIt()
    {
        // ウィザードが先。マジック・ミサイルは呪文書にあるが未準備で、ソーサラーでは覚えている → ソーサラーの能力値で唱える
        var c = Multi(("wizard", 1), ("sorcerer", 1));
        c.Book("wizard").Known.Add("magic_missile");
        c.Book("sorcerer").Known.Add("magic_missile");
        Assert.Equal("sorcerer", Spellcasting.ClassFor(Data, c, "magic_missile")?.Id);
        // ウィザードでも準備すれば、両方が候補なので先に取ったウィザード
        c.Book("wizard").Prepared.Add("magic_missile");
        Assert.Equal("wizard", Spellcasting.ClassFor(Data, c, "magic_missile")?.Id);
    }

    [Fact]
    public void ClassesWithASpellComeFromTheSets()
    {
        var ids = Data.ClassesWithSpell("magic_missile").Select(c => c.Id).ToList();
        Assert.Equal(["sorcerer", "wizard"], ids);
        Assert.All(Data.Spells.Values, s => Assert.Null(s.Classes));   // 標準の呪文は classes を持たない
    }

    [Fact]
    public void NumbersFollowTheThreeForms()
    {
        var c = Multi(("paladin", 3));
        c.Scores[Ability.Cha] = 14;   // +2
        LevelNumber Read(string json) => JsonSerializer.Deserialize<LevelNumber>(json, ContentLibrary.JsonOptions)!;
        Assert.Equal(7, Read("7").Eval(c, 3));
        Assert.Equal(3, Read("""{"by_level": {"1": 2, "3": 3, "9": 4}}""").Eval(c, 3));
        Assert.Equal(2, Read("""{"by_level": {"1": 2, "3": 3}}""").Eval(c, 2));
        var paladin = Read("""{"ability": "cha", "class_level": 0.5, "round": "floor", "min": 1}""");
        Assert.Equal(3, paladin.Eval(c, 3));   // 2 + 1.5 の切り捨て
        Assert.Equal(4, paladin.Eval(c, 4));
        var recovery = Read("""{"class_level": 0.5, "round": "ceil"}""");
        Assert.Equal([1, 2, 2], new[] { recovery.Eval(c, 1), recovery.Eval(c, 3), recovery.Eval(c, 4) });
        c.Scores[Ability.Cha] = 3;   // -4
        Assert.Equal(1, paladin.Eval(c, 2));   // min
    }

    [Theory]
    [InlineData("""{"ability": true}""")]
    [InlineData("""{"ability": "luck"}""")]
    [InlineData("""{"round": "nearest"}""")]
    [InlineData("""{"min": 3, "max": 1}""")]
    [InlineData("""{"level": 2}""")]
    [InlineData("""{"by_level": {"1": 2}, "base": 1}""")]
    [InlineData("""{"by_level": {"25": 2}}""")]
    public void BadNumbersAreRejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<LevelNumber>(json, ContentLibrary.JsonOptions));

    [Fact]
    public void APackSpellWithTheOldClassesListJoinsTheClassSet()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/spells.json", """[{"id": "pack_bolt", "name": {"en": "Pack Bolt"}, "level": 1, "school": "evocation", "classes": ["wizard"]}]""");
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p")));
        Assert.Contains("pack_bolt", data.SpellSets["wizard_1"].Spells);
        Assert.Contains("magic_missile", data.SpellSets["wizard_1"].Spells);   // 元の束は残る
    }

    [Fact]
    public void AnOldClassesListReplacesTheClassesThatCanUseTheSpell()
    {
        // 前の形では classes が使えるクラスそのものだったので、上書きした呪文は、元のクラスの束から外れる
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/spells.json", """[{"id": "magic_missile", "extends": "magic_missile", "classes": ["cleric"]}]""");
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p")));
        Assert.Equal(["cleric"], data.ClassesWithSpell("magic_missile").Select(c => c.Id));
    }

    [Fact]
    public void SlotsWaitForTheLevelWhereCastingStarts()
    {
        // 呪文を使い始めるのが L3 のウィザードの写しは、L1・L2 ではスロットも覚えられる呪文も無く、マルチクラスでも数えない
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        // extends はオブジェクトの中までは混ぜないので、spellcasting は全部書く(ウィザードのものを写して from_level だけ変える)
        var sc = JsonSerializer.SerializeToNode(Data.Classes["wizard"].Spellcasting, ContentLibrary.JsonOptions)!.AsObject();
        sc["from_level"] = 3;
        dir.Write("p/classes.json", $$"""[{"id": "late_mage", "extends": "wizard", "spellcasting": {{sc.ToJsonString()}}}]""");
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p")));
        var late = data.Classes["late_mage"];
        Assert.Equal(3, late.Spellcasting!.FromLevel);
        var c = Multi(("late_mage", 1));
        Assert.Equal(0, Spellcasting.MaxSlots(data, c, 1));
        Assert.Equal(0, Spellcasting.ClassMaxSpellLevel(data, late, c));
        var m = Multi(("wizard", 1), ("late_mage", 2));
        Assert.Equal(1, Spellcasting.CasterLevel(data, m));   // ウィザード 1 だけを数える
        Assert.Equal(0, Spellcasting.MaxSlots(data, m, 2));
        var started = Multi(("late_mage", 3));
        Assert.Equal(3, Spellcasting.CasterLevel(data, started));
    }

    [Fact]
    public void TwoPactClassesShareSlotsButLearnByTheirOwnLevel()
    {
        // ウォーロック 2 とその写し 2: 協約のスロットは合計の 4 で数える(2 レベルのスロット)が、覚えられる呪文はそれぞれ L2 なので 1 レベルまで
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", """[{"id": "pact_copy", "extends": "warlock"}]""");
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p")));
        var c = Multi(("warlock", 2), ("pact_copy", 2));
        Assert.Equal(2, Spellcasting.PactSlotLevel(data, c));
        Assert.Equal(1, Spellcasting.ClassMaxSpellLevel(data, data.Classes["warlock"], c));
        Assert.Equal(1, Spellcasting.ClassMaxSpellLevel(data, data.Classes["pact_copy"], c));
    }

    [Fact]
    public void BadSpellcastingIsReported()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", """
            [{"id": "test_mage", "extends": "wizard", "spellcasting": {"ability": "int", "style": "known", "progression": "pact",
              "spell_access": [{"lv": 1, "spelllv": 1, "set": "wizard_cantrips"}, {"lv": 1, "spelllv": 1, "set": "no_such_set"}]}}]
            """);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(content);
        string all = string.Join("\n", content.Problems);
        Assert.Contains("progression を協約魔術", all);
        Assert.Contains("known が要ります", all);
        Assert.Contains("'no_such_set'", all);
        Assert.Contains("初級呪文なので spelllv 0", all);
    }

    /// <summary>
    /// spelllv で、もとのレベルと違うレベルに割り当てられる(Wizardry 風の呪文の段など)。要るスロットと並びは割り当てたレベル、
    /// 効果はもとのレベルを基準に、割り当てたレベルから上のスロットの分だけ強くなる。巻物などクラスの束に無いときは、もとのレベル。
    /// </summary>
    [Fact]
    public void ReassignedSpellLevelsDecideSlotsAndUpcasting()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", """
            [{"id": "t_mage", "extends": "wizard", "spellcasting": {"ability": "int", "style": "known", "progression": "full", "known": {"class_level": 4},
              "spell_access": [{"lv": 1, "spelllv": 1, "set": "wizard_1"}, {"lv": 3, "spelllv": 2, "set": "wizard_3"}]}}]
            """);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var cls = data.Classes["t_mage"];
        var c = Multi(("t_mage", 3));   // 2 レベルの呪文まで: もとは 3 レベルのヘイストやファイアーボールが使える
        var haste = data.Spells["haste"];
        Assert.Contains(Spellcasting.ClassSpells(data, cls, c, cantrips: false), x => x.Id == "fireball");
        c.Book("t_mage").Known.Add("haste");
        c.Book("t_mage").Known.Add("fireball");
        Assert.Equal(2, Spellcasting.AssignedLevel(data, c, haste));
        Assert.Equal(2, Spellcasting.LowestSlot(data, c, haste));
        Assert.True(Spellcasting.ListedAt(data, c, haste, 2));
        var fireball = data.Spells["fireball"];
        Assert.Equal(3, Spellcasting.EffectLevel(data, c, fireball, 2));   // 2 レベルのスロットで、もとの 3 レベルの強さ
        Assert.Equal(4, Spellcasting.EffectLevel(data, c, fireball, 3));   // 1 段上
        Assert.Equal(3, Spellcasting.AssignedLevel(data, Multi(("wizard", 5)), fireball));   // 束に無い人はもとのレベル

        // 唱えると、割り当てたレベルのスロットを使う
        c.MaxHp = c.Hp = 20;
        var state = new GravewardSpiral.Core.Combat.CombatState { Party = [new GravewardSpiral.Core.Combat.PartyMember(c, data)], Groups = [] };
        var engine = new GravewardSpiral.Core.Combat.CombatEngine(data, new GravewardSpiral.Core.Rng.Pcg32(1, 1));
        engine.CastSpell(state, state.Party[0], new GravewardSpiral.Core.Combat.PlayerCommand(GravewardSpiral.Core.Combat.CommandKind.Cast, -1, 0, "haste", 0));
        Assert.Equal(1, c.SlotsUsed.GetValueOrDefault(2));
        Assert.Equal(0, c.SlotsUsed.GetValueOrDefault(3));
    }

    /// <summary>スロットの伸び方はデータ(spell_progressions.json)。パックで足した伸び方でスロットと覚えられる呪文のレベルが決まり、3 分の 1 の術者も表どおり。</summary>
    [Fact]
    public void SlotProgressionsComeFromData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/spell_progressions.json", """[{"id": "quarter", "caster_level": {"class_level": 0.25, "round": "ceil"}, "multiclass": {"class_level": 0.25}}]""");
        dir.Write("p/classes.json", """
            [{"id": "t_quarter", "extends": "wizard", "spellcasting": {"ability": "int", "style": "known", "progression": "quarter", "known": {"class_level": 1},
              "spell_access": [{"lv": 1, "spelllv": 1, "set": "wizard_1"}, {"lv": 1, "spelllv": 2, "set": "wizard_2"}]}},
             {"id": "t_third", "extends": "wizard", "spellcasting": {"ability": "int", "style": "known", "progression": "third", "from_level": 3, "known": {"class_level": 1},
              "spell_access": [{"lv": 3, "spelllv": 1, "set": "wizard_1"}, {"lv": 7, "spelllv": 2, "set": "wizard_2"}]}}]
            """);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var q = Multi(("t_quarter", 9));   // 術者のレベル 3(9/4 の切り上げ): 1 レベル 4・2 レベル 2
        Assert.Equal(3, Spellcasting.CasterLevel(data, q));
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(data, data.Classes["t_quarter"], q));
        Assert.Equal(2, Spellcasting.MaxSlots(data, q, 2));
        var t = Multi(("t_third", 7));   // 3 分の 1 の術者 7 は術者のレベル 3
        Assert.Equal(3, Spellcasting.CasterLevel(data, t));
        Assert.Equal(2, Spellcasting.ClassMaxSpellLevel(data, data.Classes["t_third"], t));
        var multi = Multi(("t_third", 7), ("wizard", 3));   // マルチクラスは 7/3 の切り捨て 2 + 3
        Assert.Equal(5, Spellcasting.CasterLevel(data, multi));
    }

    /// <summary>SRD のクラスの spell_access の lv は、そのレベルの呪文を覚えられるようになるクラスのレベル(スロットの表と同じ区切り)。届かない束(ウォーロックの 6 以上)は除く。</summary>
    [Fact]
    public void SrdSpellAccessLevelsMatchTheSlotTables()
    {
        foreach (var cls in Data.Classes.Values.Where(c => c.Spellcasting != null))
        {
            foreach (var a in cls.Spellcasting!.SpellAccess)
            {
                int? first = Enumerable.Range(1, 20).Cast<int?>()
                    .FirstOrDefault(l => a.Spelllv == 0 ? l >= cls.Spellcasting.FromLevel : Spellcasting.ClassMaxSpellLevel(Data, cls, Multi((cls.Id, l!.Value))) >= a.Spelllv);
                if (first == null) continue;
                Assert.True(first == a.Lv, $"{cls.Id} の {a.Set}: lv は {first}(書いてあるのは {a.Lv})");
            }
        }
    }

    [Fact]
    public void BadSlotDataIsReported()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/spell_progressions.json", """[{"id": "broken"}]""");
        dir.Write("p/spell_slots.json", """[{"id": "21", "slots": [1]}]""");
        dir.Write("p/classes.json", """[{"id": "t_mage", "extends": "wizard", "spellcasting": {"ability": "int", "style": "known", "progression": "no_such", "known": {"class_level": 1}}}]""");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(content);
        string all = string.Join("\n", content.Problems);
        Assert.Contains("'no_such' が spell_progressions にありません", all);
        Assert.Contains("spell_progressions 'broken'", all);
        Assert.Contains("spell_slots '21'", all);
    }

    [Fact]
    public void ACopiedClassCastsLikeTheOriginal()
    {
        // 別の id で写したウィザードは、同じ呪文の数・スロット・選べる呪文になる(Step 7 の確かめの先取り)
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", """[{"id": "test_mage", "extends": "wizard", "name": {"en": "Test Mage"}}]""");
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p")));
        for (int lv = 1; lv <= 20; lv++)
        {
            var a = Multi(("wizard", lv));
            var b = Multi(("test_mage", lv));
            var wa = data.Classes["wizard"];
            var wb = data.Classes["test_mage"];
            Assert.Equal(Spellcasting.CantripsKnown(wa, a), Spellcasting.CantripsKnown(wb, b));
            Assert.Equal(Spellcasting.SpellsKnown(wa, a), Spellcasting.SpellsKnown(wb, b));
            Assert.Equal(Spellcasting.PreparedCount(wa, a), Spellcasting.PreparedCount(wb, b));
            Assert.Equal(Spellcasting.CasterLevel(data, a), Spellcasting.CasterLevel(data, b));
            Assert.Equal(Spellcasting.ClassSpells(data, wa, a, false).Select(s => s.Id), Spellcasting.ClassSpells(data, wb, b, false).Select(s => s.Id));
        }
    }
}

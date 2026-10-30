using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Items;

/// <summary>宝箱の罠のデータ(traps.json と trapsets.json。docs/class-data-design.md の Step 6c)。</summary>
public class TrapTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    private static Character Make(GameData data, string cls, int hp = 200)
    {
        var c = new Character { Id = cls, Name = cls, RaceId = "human", ClassId = cls, Level = 5, Classes = [new ClassLevel { ClassId = cls, Level = 5 }], MaxHp = hp, Hp = hp };
        foreach (var a in Abilities.All) c.Scores[a] = 10;
        return c;
    }

    private static (GameData Data, ContentLibrary Content) WithPack(params (string File, string Json)[] files)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        foreach (var (file, json) in files) dir.Write("p/" + file, json);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    [Fact]
    public void TheChestSetHoldsTheNineTrapsAndKillsAtZero()
    {
        var set = Data.TrapSets[TrapSetDef.Chest];
        Assert.True(set.Lethal);
        Assert.Equal(["", "poison_needle", "gas_bomb", "crossbow_bolt", "exploding_box", "stunner", "teleporter", "mage_blaster", "priest_blaster", "alarm"], Chest.Names(Data));
        var c = Make(Data, "fighter");
        var r = new Chest { Trap = "crossbow_bolt", Tier = 1 }.Trigger(Data, [c], c, new Pcg32(1, 1));
        Assert.Equal("chest.sprung", r.Lines[0].Key);
        Assert.Same(Data.Traps["crossbow_bolt"].Text, r.Lines[0].Args[0]);
    }

    /// <summary>術者を狙う罠は、クラスの id でなくタグ(tags の arcane)で当たる。写したクラスにも、タグを外したクラスにも、データのとおり。</summary>
    [Fact]
    public void BlastersFollowClassTags()
    {
        var (data, content) = WithPack(("classes.json", """[{"id": "t_mage", "extends": "wizard"}, {"id": "t_plain_mage", "extends": "wizard", "tags": []}]"""));
        Assert.Empty(content.Problems);
        var mage = Make(data, "t_mage");
        var plain = Make(data, "t_plain_mage");
        var fighter = Make(data, "fighter");
        new Chest { Trap = "mage_blaster", Tier = 0 }.Trigger(data, [mage, plain, fighter], fighter, new Pcg32(2, 2));
        Assert.True(mage.Hp < 200);
        Assert.Equal(200, plain.Hp);
        Assert.Equal(200, fighter.Hp);
    }

    /// <summary>パックで罠を足し、束 chest に入れると宝箱に出る。罠の lethal は束より優先する。</summary>
    [Fact]
    public void PacksCanAddTraps()
    {
        var (data, content) = WithPack(
            ("traps.json", """[{"id": "acid_spray", "name": {"en": "Acid", "ja": "酸"}, "lethal": false, "steps": [{"type": "damage", "who": "party", "dice": "50d6", "damage_type": "acid"}]}]"""),
            ("trapsets.json", """[{"id": "chest", "lethal": true, "traps": [{"id": "acid_spray", "weight": 3}]}]"""));
        Assert.Empty(content.Problems);
        Assert.Equal(["", "acid_spray"], Chest.Names(data));
        var c = Make(data, "fighter", 10);
        new Chest { Trap = "acid_spray" }.Trigger(data, [c], c, new Pcg32(3, 3));
        Assert.Equal((1, LifeState.Alive), (c.Hp, c.State));   // 罠の lethal: false で HP 1 で止まる
    }

    [Fact]
    public void BadTrapsAreReported()
    {
        var (_, content) = WithPack(
            ("traps.json", """[{"id": "bad", "steps": [{"type": "warp"}, {"type": "damage", "who": "everyone"}, {"type": "condition", "id": "sleepy", "save": {"ability": "luck"}}]}]"""),
            ("trapsets.json", """[{"id": "chest", "traps": [{"id": "no_such_trap"}, {"id": "bad", "weight": 0}]}]"""));
        string all = string.Join("\n", content.Problems);
        Assert.Contains("手順 'warp' は使えません", all);
        Assert.Contains("who 'everyone'", all);
        Assert.Contains("状態 'sleepy'", all);
        Assert.Contains("save の ability 'luck'", all);
        Assert.Contains("'no_such_trap'", all);
        Assert.Contains("weight は 1 以上", all);
    }
}

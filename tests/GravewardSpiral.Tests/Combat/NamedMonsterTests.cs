using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Combat;

public class NamedMonsterTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static GameSession World(ulong seed)
    {
        var request = new GenerationRequest(seed, 6, FloorSizeMode.Fixed(16, 16));
        var s = GameSession.New(1, request, new MazeGenerator(MazeGenSettings.Load(Content)).Generate(request).Dungeon);
        s.BuildRosters(Data, Core.Combat.EncounterSettings.Load(Content));
        return s;
    }

    [Fact]
    public void ZeroToTwoNamedMonstersArePlacedPerWorld()
    {
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        Assert.NotEmpty(Data.NamedMonsters);
        var counts = Enumerable.Range(1, 60).Select(i => World((ulong)i).Named.Count).ToList();
        Assert.All(counts, c => Assert.InRange(c, 0, 2));
        Assert.Contains(0, counts);   // いない世界もある
        Assert.Contains(2, counts);
        // 同じシードなら同じ
        Assert.Equal(World(7).Named.Select(n => (n.Id, n.Floor, n.Lair, n.X, n.Y)), World(7).Named.Select(n => (n.Id, n.Floor, n.Lair, n.X, n.Y)));
        // 深さの範囲の階に、ねぐらは何も無いマスに置く
        foreach (var w in Enumerable.Range(1, 60).Select(i => World((ulong)i)))
        {
            foreach (var n in w.Named)
            {
                var def = Data.NamedMonsters[n.Id];
                double depth = (double)n.Floor / (w.Dungeon.Floors.Count - 1);
                Assert.InRange(depth, def.MinDepth - 1e-9, def.MaxDepth + 1e-9);
                if (!n.Lair) continue;
                var cell = w.Dungeon.Floors[n.Floor][n.X, n.Y];
                Assert.False(cell.IsSolid);
                Assert.Equal(CellFeature.None, cell.Feature);
                Assert.Same(n, w.NamedLairAt(n.Floor, new GridPos(n.X, n.Y)));
            }
        }
    }

    [Fact]
    public void NamedMonstersAreSavedAndUsefulItemsWork()
    {
        var w = Enumerable.Range(1, 60).Select(i => World((ulong)i)).First(x => x.Named.Count > 0);
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        w.Named[0].Defeated = true;
        store.Save(w, Content);
        var back = store.Load(w.ClanId, w.WorldKey);
        Assert.Equal(w.Named.Select(n => (n.Id, n.Floor, n.Lair, n.Rumored, n.Defeated)), back.Named.Select(n => (n.Id, n.Floor, n.Lair, n.Rumored, n.Defeated)));

        // 便利なユニーク品: 鍵開けと罠外し、知覚
        var rogue = CharacterFactory.QuickCreate(Data, "R", "human", "rogue");
        int perception = rogue.SkillBonus(Data, "perception");
        rogue.Inventory.Equipped[EquipSlot.Waist] = ItemInstance.Of("rat_kings_keyring");
        rogue.Inventory.Equipped[EquipSlot.Neck] = ItemInstance.Of("sentry_necklace");
        Assert.Equal(5, rogue.PassiveBonus(Data, "disarm"));
        Assert.Equal(perception + 5, rogue.SkillBonus(Data, "perception"));
    }
}

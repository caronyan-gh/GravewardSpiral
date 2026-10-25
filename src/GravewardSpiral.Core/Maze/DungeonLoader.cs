using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Maze;

/// <summary>
/// パックの dungeons.json から、手作りの迷宮を読む。
/// <code>[{"id": "devtest", "name": {"ja": "試しの迷宮"}, "floors": ["maps/devtest_b1.txt", ...]}]</code>
/// </summary>
public static class DungeonLoader
{
    public const string Collection = "dungeons";

    public sealed class DungeonDef
    {
        public string Id { get; set; } = "";
        public LocalizedText? Name { get; set; }
        public List<string> Floors { get; set; } = [];
    }

    public static Dungeon Load(ContentLibrary content, string id)
    {
        var item = content.Find(Collection, id) ?? throw new KeyNotFoundException($"迷宮 '{id}' がありません");
        var def = item.As<DungeonDef>();
        var floors = def.Floors.Select(path =>
        {
            string full = item.ResolvePath(path);
            return MazeTextFormat.Parse(File.ReadAllText(full), $"{item.Pack.Id}/{path}");
        }).ToList();
        return new Dungeon(def.Id, floors);
    }

    /// <summary>パックにある手作りの迷宮を全部読んで、辻褄を調べる(起動時の検査)。</summary>
    public static void ValidateAll(ContentLibrary content)
    {
        foreach (var item in content.All(Collection))
        {
            try
            {
                var dungeon = Load(content, item.Id);
                content.Problems.AddRange(dungeon.Validate().Select(p => $"迷宮 '{item.Id}': {p}"));
            }
            catch (Exception ex) when (ex is FormatException or IOException or System.Text.Json.JsonException)
            {
                content.Problems.Add($"迷宮 '{item.Id}': {ex.Message}");
            }
        }
    }
}

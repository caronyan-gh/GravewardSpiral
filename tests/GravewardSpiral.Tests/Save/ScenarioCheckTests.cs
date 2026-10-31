using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

/// <summary>まとめての検査(エディタの検査のタブと、配布する ScenarioCheck が呼ぶもの)。同梱の三部作は問題なしで通る。</summary>
public class ScenarioCheckTests
{
    [Theory]
    [InlineData("gravekeeper")]
    [InlineData("sunken")]
    [InlineData("devourer")]
    public void TheBundledScenariosPass(string id)
    {
        string dir = Path.Combine(TestPaths.Content, "scenarios", id);
        var problems = new List<string>();
        var pack = ScenarioPack.Load(dir, problems);
        Assert.Empty(problems);
        var content = ContentLibrary.Load(TestPaths.Content, dir);
        DungeonLoader.ValidateAll(content);
        var data = GameData.Load(content);
        var lines = ScenarioCheck.Run(pack, content, data, ContentLibrary.Load(TestPaths.Content), (ja, _) => ja, "ja");
        Assert.Empty(lines.Where(l => l.Level == CheckLevel.Problem).Select(l => l.Text));
        Assert.Contains(lines, l => l.Level == CheckLevel.Pass && l.Text.StartsWith("読み込みの検査を通った"));
    }

    private static List<CheckLine> Check(string dir)
    {
        var pack = ScenarioPack.Load(dir, []);
        var content = ContentLibrary.Load(TestPaths.Content, dir);
        DungeonLoader.ValidateAll(content);
        return ScenarioCheck.Run(pack, content, GameData.Load(content), ContentLibrary.Load(TestPaths.Content), (ja, _) => ja, "ja");
    }

    /// <summary>改行を LF にして読む(取り出しで CRLF になっていても、置き換えが当たるように)。</summary>
    private static string Lf(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string CopyKitExample(TestPaths.TempDir dir)
    {
        string src = Path.Combine(TestPaths.Root, "scenario-kit", "examples", "kit_example");
        string to = Path.Combine(dir.Path, "kit_example");
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
        return to;
    }

    /// <summary>手引きの見本(scenario-kit/examples/kit_example)は、問題も注意も知らせも無しで通る(手引きの説明とずれないように)。</summary>
    [Fact]
    public void TheKitExampleIsClean()
    {
        var lines = Check(Path.Combine(TestPaths.Root, "scenario-kit", "examples", "kit_example"));
        Assert.Empty(lines.Where(l => l.Level != CheckLevel.Pass).Select(l => l.Text));
    }

    /// <summary>作り方の注意: 立ち去ると済んでしまう選択肢、2つある階段、読まないフラグ、仕切りの無い階、worlds.json の id 違い。</summary>
    [Fact]
    public void AuthoringMistakesAreReported()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        string events = Path.Combine(s, "events.json");
        File.WriteAllText(events, Lf(events)
            .Replace("""{ "type": "again" }""", """{ "type": "text", "text": { "ja": "やめた。", "en": "You leave it." } }""")   // 立ち去るから again を外す
            .Replace("\"kit_example_lever_pulled\" }]", "\"something_else\" }]"));   // 祭壇が別のフラグを読む → レバーのフラグは誰も読まない
        string b1 = Path.Combine(s, "maps", "kit_example_b1.txt");
        File.WriteAllText(b1, Lf(b1).Replace("|   |   |\n+ + + + +", "|u  |   |\n+ + + + +"));   // 上り階段を2つにする
        string b2 = Path.Combine(s, "maps", "kit_example_b2.txt");
        File.WriteAllText(b2, Lf(b2).Replace("|     | |", "|       |").Replace("+ +-+-+L+", "+ + + +L+").Replace("+ + + +-+", "+ + + + +")   // 仕切りを無くす
            .Replace("mark 3 0 gate:kit_example_gate\n", ""));
        File.WriteAllText(Path.Combine(s, "worlds.json"), Lf(Path.Combine(s, "worlds.json")).Replace("\"id\": \"kit_example\"", "\"id\": \"other_id\""));
        var lines = Check(s);
        Assert.Contains(lines, l => l.Level == CheckLevel.Caution && l.Text.Contains("選ぶとイベントが済んで、もう起きない"));
        Assert.Contains(lines, l => l.Level == CheckLevel.Caution && l.Text.StartsWith("B1 に上り階段が 2 つある"));
        Assert.Contains(lines, l => l.Level == CheckLevel.Note && l.Text.StartsWith("フラグ kit_example_lever_pulled を立てているが"));
        Assert.Contains(lines, l => l.Level == CheckLevel.Caution && l.Text.StartsWith("worlds.json の id(other_id)"));
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("序文がまだ無い"));
    }

    /// <summary>地図が1つも読めないときは、落ちずに問題として返す(外の試験で道具が落ちた)。</summary>
    [Fact]
    public void UnreadableMapsAreAProblemNotACrash()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(s, "maps"))) File.WriteAllText(f, "floor broken\nsize 2 2\nwalls\nxxx\n");
        var pack = ScenarioPack.Load(s, []);
        var content = ContentLibrary.Load(TestPaths.Content, s);
        DungeonLoader.ValidateAll(content);
        var lines = ScenarioCheck.Run(pack, content, GameData.Load(content), ContentLibrary.Load(TestPaths.Content), (ja, _) => ja, "ja");
        Assert.Contains(lines, l => l.Level == CheckLevel.Problem && l.Text.StartsWith("迷宮の階が1つも読めない"));
    }

    /// <summary>門: 印のマスに L が無い、開けるイベントが別の階にある。</summary>
    [Fact]
    public void GateMistakesAreReported()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        string b2 = Path.Combine(s, "maps", "kit_example_b2.txt");
        string b1 = Path.Combine(s, "maps", "kit_example_b1.txt");
        File.WriteAllText(b2, Lf(b2).Replace("mark 3 0 gate:kit_example_gate", "mark 1 0 gate:kit_example_gate")   // L の無いマスへ
            .Replace("mark 0 3 event:kit_example_switch\n", ""));
        File.WriteAllText(b1, Lf(b1) + "mark 1 3 event:kit_example_switch\n");   // スイッチを B1 へ
        var lines = Check(s);
        Assert.Contains(lines, l => l.Level == CheckLevel.Caution && l.Text.StartsWith("B2 (1, 0) の門 kit_example_gate のマスに錠の辺(L)が無い"));
        Assert.Contains(lines, l => l.Level == CheckLevel.Caution && l.Text.Contains("の open kit_example_gate は、門のある B2 を開けない"));
    }

    /// <summary>タイマー(timer)で起こすイベントは、使われていないと数えない(外の試験で誤って注意が出た)。</summary>
    [Fact]
    public void AnEventStartedByATimerIsUsed()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        string events = Path.Combine(s, "events.json");
        var list = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(events))!.AsArray();
        list[0]!["steps"]!.AsArray().Insert(0, new System.Text.Json.Nodes.JsonObject { ["type"] = "timer", ["id"] = "kit_example_timed", ["value"] = 5 });
        list.Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "kit_example_timed", "place": "maze", "steps": [{ "type": "text", "text": { "ja": "鐘が鳴った。", "en": "A bell rings." } }] }
            """));
        File.WriteAllText(events, list.ToJsonString());
        var lines = Check(s);
        Assert.DoesNotContain(lines, l => l.Text.Contains("使われていないイベント"));
    }

    /// <summary>門の印を外して錠の辺(L)だけ残ると、歩いて行けないマスの文に、L が原因かもしれないと添える。</summary>
    [Fact]
    public void ALockLeftWithoutItsGateIsPointedOut()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        string b2 = Path.Combine(s, "maps", "kit_example_b2.txt");
        File.WriteAllText(b2, Lf(b2).Replace("mark 3 0 gate:kit_example_gate\n", ""));
        var lines = Check(s);
        Assert.Contains(lines, l => l.Level == CheckLevel.Problem && l.Text.Contains("歩いて行けないマス") && l.Text.Contains("(3,0) 南") && l.Text.Contains("L が原因かもしれない"));
    }

    [Fact]
    public void AFloorWithNoInnerWallsIsNoted()
    {
        using var dir = new TestPaths.TempDir();
        string s = CopyKitExample(dir);
        string b1 = Path.Combine(s, "maps", "kit_example_b1.txt");
        File.WriteAllText(b1, """
            floor kit_example_b1
            size 4 4
            wrap none
            start 0 0 S
            walls
            +-+-+-+-+
            |u      |
            + + + + +
            |       |
            + + + + +
            |       |
            + + + + +
            |      d|
            +-+-+-+-+
            mark 0 3 event:kit_example_lever
            """);
        var lines = Check(s);
        Assert.Contains(lines, l => l.Level == CheckLevel.Note && l.Text.StartsWith("B1 は仕切りの無い1部屋"));
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("B2 は仕切りの無い1部屋"));
    }

    [Fact]
    public void ProblemsComeOutInTheChosenLanguage()
    {
        using var dir = new TestPaths.TempDir();
        // 印の指すイベントが無いシナリオ
        string src = Path.Combine(TestPaths.Content, "scenarios", "sunken");
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dir.Path, "s", Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        string map = Directory.EnumerateFiles(Path.Combine(dir.Path, "s", "maps")).First();
        File.AppendAllText(map, "\nmark 0 0 event:no_such_event\n");
        string scenario = Path.Combine(dir.Path, "s");
        var pack = ScenarioPack.Load(scenario, []);
        var content = ContentLibrary.Load(TestPaths.Content, scenario);
        var data = GameData.Load(content);
        var en = ScenarioCheck.Run(pack, content, data, ContentLibrary.Load(TestPaths.Content), (_, e) => e, "en");
        Assert.Contains(en, l => l.Level == CheckLevel.Problem && l.Text.Contains("the event for mark event:no_such_event does not exist"));
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Tests.Characters;

/// <summary>標準のクラスや種族を外した(disabled)パック。外した物を指す標準の定義は、誤りにせず使われないだけにする(astra の Wiz1〜3 風の試作で見つかった穴)。</summary>
public class TrimmedClassesTests
{
    private static ContentLibrary WithPack(params (string File, string Json)[] files)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        foreach (var (file, json) in files) dir.Write("p/" + file, json);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(content);
        return content;
    }

    /// <summary>バーバリアン・バード・ハーフオークを外しても読める(怒りや不屈の耐久の回数は誰も持たないが、その能力も誰も持たない)。</summary>
    [Fact]
    public void RemovingStandardClassesAndRacesLeavesNoProblems()
    {
        var content = WithPack(
            ("classes.json", """[{"id": "barbarian", "disabled": true}, {"id": "bard", "disabled": true}]"""),
            ("races.json", """[{"id": "half_orc", "disabled": true}]"""));
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        Assert.True(content.WasDisabled("classes", "bard"));
        Assert.False(content.WasDisabled("classes", "fighter"));
    }

    /// <summary>外したクラスを元にして写せる(標準のウィザードを写したメイジを作り、ウィザードは外す)。</summary>
    [Fact]
    public void ADisabledClassCanStillBeTheBaseOfACopy()
    {
        var content = WithPack(("classes.json", """[{"id": "t_mage", "extends": "wizard"}, {"id": "wizard", "disabled": true}]"""));
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        Assert.False(data.Classes.ContainsKey("wizard"));
        Assert.Equal(data.Classes["t_mage"].HitDie, GameData.Load(ContentLibrary.Load(TestPaths.Content)).Classes["wizard"].HitDie);
    }

    /// <summary>無いクラス(外したのではなく、書き間違い)は今までどおり誤り。文には、入れ子のどこかと、書いたファイルが付く。</summary>
    [Fact]
    public void AMisspelledClassIsStillAnErrorWithItsPlaceAndFile()
    {
        var content = WithPack(("events.json", """
            [{"id": "t_ev", "steps": [{"type": "choice", "options": [
              {"label": "a", "steps": [{"type": "text", "text": "x"}]},
              {"label": "b", "when": [{"type": "class", "id": "wizzard"}], "steps": [{"type": "text", "text": "y"}]}]}]}]
            """));
        string problem = Assert.Single(content.Problems, p => p.Contains("wizzard"));
        Assert.Contains("events 't_ev': 条件 class: classes に 'wizzard' がありません", problem);
        Assert.Contains("場所: steps[0].options[1].when[0]", problem);
        Assert.Contains("ファイル: ", problem);
        Assert.Contains("p/events.json", problem);
    }
}

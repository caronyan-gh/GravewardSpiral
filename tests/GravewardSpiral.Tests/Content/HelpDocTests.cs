using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Tests.Content;

public class HelpDocTests
{
    [Fact]
    public void ReadsHeadingsParagraphsAndBullets()
    {
        var blocks = HelpDoc.Parse("""
            # 操作
            ## キー
            決定は Enter、
            取消は **Esc**。

            - 上下で選ぶ
              続きの行
            - 地図は M
            Words on
            two lines.
            """);
        Assert.Equal([
            new HelpBlock(HelpBlockKind.Topic, "操作"),
            new HelpBlock(HelpBlockKind.Heading, "キー"),
            new HelpBlock(HelpBlockKind.Paragraph, "決定は Enter、取消は Esc。"),
            new HelpBlock(HelpBlockKind.Bullet, "上下で選ぶ続きの行"),
            new HelpBlock(HelpBlockKind.Bullet, "地図は M"),
            new HelpBlock(HelpBlockKind.Paragraph, "Words on two lines."),
        ], blocks);
    }

    [Fact]
    public void LoadsFilesInOrderAndFallsBackToEnglish()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("help/en/02_b.md", "# B\n## b1");
        dir.Write("help/en/01_a.md", "# A\ntext");
        var doc = HelpDoc.Load(Path.Combine(dir.Path, "help"), "fr");
        Assert.Equal(["A", "B", "b1"], doc.TocIndexes().Select(i => doc.Blocks[i].Text));
    }

    [Fact]
    public void BundledHelpHasTheSameTopicsInEachLanguage()
    {
        var root = Path.Combine(TestPaths.Content, "help");
        var ja = HelpDoc.Load(root, "ja");
        var en = HelpDoc.Load(root, "en");
        Assert.NotEmpty(ja.Blocks);
        Assert.Equal(ja.Blocks.Count(b => b.Kind == HelpBlockKind.Topic), en.Blocks.Count(b => b.Kind == HelpBlockKind.Topic));
        Assert.Equal(Directory.GetFiles(Path.Combine(root, "ja"), "*.md").Select(Path.GetFileName).Order(),
            Directory.GetFiles(Path.Combine(root, "en"), "*.md").Select(Path.GetFileName).Order());
    }
}

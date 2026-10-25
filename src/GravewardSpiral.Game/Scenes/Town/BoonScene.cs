using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>エピックレベルの恩恵を選ぶ(L21 から1レベルごとに1つ)。</summary>
public sealed class BoonScene(Character c) : MenuScene
{
    protected override string Title => Services.Strings.Format("boon.title", c.Name, c.EpicPoints);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var kind in Character.BoonKinds)
        {
            int taken = c.EpicBoons.Count(b => b == kind);
            yield return new MenuItem(s["boon." + kind], taken > 0 ? $"×{taken}" : null, c.EpicPoints > 0, kind);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not string kind || kind == "back")
        {
            Stack.Pop();
            return;
        }
        if (c.TakeBoon(kind))
        {
            Services.Audio.PlaySe(Sounds.LevelUp);
            Message = Services.Strings.Format("boon.taken", Services.Strings["boon." + kind]);
        }
        else Message = Services.Strings["boon.cannot"];
        if (c.EpicPoints == 0) Stack.Pop();
        else Refresh();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        float y = StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
        Line(d, area, y + 10, Services.Strings["boon.about"], Theme.Dim, 17);
    }
}

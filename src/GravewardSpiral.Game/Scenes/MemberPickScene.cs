using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>パーティから1人を選ぶ画面。選べる人は filter で決め、選んだら onPick を呼ぶ(画面はそのまま残る)。</summary>
public sealed class MemberPickScene(string title, Func<Character, bool> filter, Action<Character> onPick) : MenuScene
{
    protected override string Title => title;

    protected override int? PartyHighlight => List.Current?.Tag is Character ? List.Cursor : null;

    protected override IEnumerable<MenuItem> Items()
    {
        foreach (var c in Services.Session!.Roster.PartyMembers) yield return new MenuItem(c.Name, Enabled: filter(c), Tag: c);
        yield return new MenuItem(Services.Strings["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is Character c) onPick(c);
        else Stack.Pop();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        if (List.Current?.Tag is Character c) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
    }
}

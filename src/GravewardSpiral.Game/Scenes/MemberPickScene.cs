using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// パーティから1人を選ぶ画面。選べる人は filter で決め、選んだら onPick を呼ぶ(画面はそのまま残る)。
/// Extra を渡すと、戻るの前に人でない行を1つ足す(クランハウスの荷物の整理の倉庫。Info は右の欄に出す文)。
/// </summary>
public sealed class MemberPickScene(string title, Func<Character, bool> filter, Action<Character> onPick) : MenuScene
{
    public (string Label, Func<string> Info, Action OnPick)? Extra { get; init; }

    /// <summary>人の行の右に出す文(荷物の整理の袋の埋まり具合など)。null なら出さない。</summary>
    public Func<Character, string?>? Detail { get; init; }

    /// <summary>Extra の行の右に出す文。</summary>
    public Func<string?>? ExtraDetail { get; init; }

    protected override string Title => title;

    protected override int? PartyHighlight => List.Current?.Tag is Character ? List.Cursor : null;

    protected override IEnumerable<MenuItem> Items()
    {
        foreach (var c in Services.Session!.Roster.PartyMembers) yield return new MenuItem(c.Name, Detail?.Invoke(c), Enabled: filter(c), Tag: c);
        if (Extra is { } extra) yield return new MenuItem(extra.Label, ExtraDetail?.Invoke(), Tag: "extra");
        yield return new MenuItem(Services.Strings["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is Character c) onPick(c);
        else if (item.Tag is "extra") Extra?.OnPick();
        else Stack.Pop();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        if (List.Current?.Tag is Character c) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
        else if (List.Current?.Tag is "extra" && Extra is { } extra)
        {
            float y = Line(d, area, area.Y + 20, extra.Label, Theme.Title, 24);
            Line(d, area, y, extra.Info(), Theme.Text, 18);
        }
    }
}

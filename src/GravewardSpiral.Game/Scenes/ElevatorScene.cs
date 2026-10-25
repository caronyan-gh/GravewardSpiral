using GravewardSpiral.Core.Maze;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;

namespace GravewardSpiral.Game.Scenes;

/// <summary>エレベーター。同じ組のエレベーターがある階のうち、行ったことのある階へ移れる。</summary>
public sealed class ElevatorScene(MazeNavigator nav, Action<MoveResult> onMoved) : MenuScene
{
    protected override string Title => Services.Strings["elevator.title"];

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        var state = nav.State;
        foreach (int f in nav.ElevatorFloors())
        {
            bool here = f == state.Position.Floor;
            bool visited = state.AutoMap.Floors.ContainsKey(f);
            yield return new MenuItem($"B{f + 1}", here ? s["elevator.here"] : visited ? null : s["elevator.unknown"], !here && visited, f);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        Stack.Pop();
        if (item.Tag is int f) onMoved(nav.TakeElevator(f));
    }

    protected override void DrawInfo(Draw2D d, RectangleF area) =>
        Line(d, area, area.Y + 20, Services.Strings["elevator.about"], Theme.Text, 19);
}

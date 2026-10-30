using GravewardSpiral.Core.Maze;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>今の階の全体の地図。迷宮の画面に重ねて出し、地図か取消で閉じる。</summary>
public sealed class MapScene : Scene
{
    private static readonly RectangleF Area = new(140, 50, 1000, 610);

    private readonly ExplorationState _state;
    private readonly GridPos _center;
    private readonly PartyPosition? _party;

    public MapScene(ExplorationState state, GridPos center, PartyPosition? party)
    {
        _state = state;
        _center = center;
        _party = party;
    }

    public override bool IsOverlay => true;

    public override void Update(GameTime time, InputState input)
    {
        int floor = _party?.Floor ?? _state.Position.Floor;
        // 決定: この階のメモを読む(書き留めたものがあれば)
        if (input.Pressed(GameAction.Confirm) && _state.Notes.Any(n => n.Floor == floor))
        {
            Stack.Push(new MemoScene(_state, floor));
            return;
        }
        if (input.Pressed(GameAction.Map) || input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Confirm)) Stack.Pop();
    }

    public override void Draw(Draw2D d)
    {
        int floor = _party?.Floor ?? _state.Position.Floor;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 150));
        AutoMapRenderer.Draw(d, _state.Dungeon.Floors[floor], _state.AutoMap.For(floor), Area, _center,
            _party is { } p ? (p.Cell, p.Facing) : null, radius: null, notes: _state.Notes.Where(n => n.Floor == floor).Select(n => n.Cell).ToHashSet(),
            fitKnown: true, arrivals: _state.AutoMap.ArrivalsOn(floor).ToHashSet());   // 分かっているところだけ(まだ行っていない広さは見せない)
        bool hasNotes = _state.Notes.Any(n => n.Floor == floor);
        d.Text(Services.Strings[hasNotes ? "map.close_memo" : "map.close"], new Vector2(640, 672), 16, new Color(150, 160, 170), TextAlign.Center);
    }
}

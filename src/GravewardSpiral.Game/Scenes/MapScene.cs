using GravewardSpiral.Core.Maze;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 全体の地図。迷宮の画面に重ねて出し、地図か取消で閉じる。今の階から開き、
/// 上下・ページのキー・ホイール・見出しの ◀ ▶ で、足を踏み入れたことのある階を切り替えて見られる。
/// walkTo があれば、今の階の通ったマスを押すと地図を閉じて、そのマスへ自動で歩く。
/// </summary>
public sealed class MapScene : Scene
{
    private static readonly RectangleF Area = new(140, 50, 1000, 610);
    private static readonly RectangleF PrevArrow = new(330, 8, 60, 36), NextArrow = new(890, 8, 60, 36);

    private readonly ExplorationState _state;
    private readonly GridPos _center;
    private readonly PartyPosition? _party;
    private readonly Func<int, string>? _label;
    private readonly Action<GridPos>? _walkTo;
    private readonly int _here;
    /// <summary>見られる階(足を踏み入れた階と今の階。上から順)。</summary>
    private readonly List<int> _floors;
    private int _shown;

    /// <param name="label">階の見出し(B3 と名前)。null なら B3 だけ。</param>
    /// <param name="walkTo">マスを押したときに、そこへ歩かせる(自動移動を使わないときは null)。</param>
    public MapScene(ExplorationState state, GridPos center, PartyPosition? party, Func<int, string>? label = null, Action<GridPos>? walkTo = null)
    {
        _walkTo = walkTo;
        _state = state;
        _center = center;
        _party = party;
        _label = label;
        _here = party?.Floor ?? state.Position.Floor;
        _floors = state.AutoMap.Floors.Where(kv => kv.Value.Visited.Count > 0).Select(kv => kv.Key)
            .Append(_here).Where(f => f >= 0 && f < state.Dungeon.Floors.Count).Distinct().Order().ToList();
        _shown = _floors.IndexOf(_here);
    }

    public override bool IsOverlay => true;

    private int Floor => _floors[_shown];

    public override void Update(GameTime time, InputState input)
    {
        // 階を切り替える(上・ページの上・ホイールを奥・◀ で上の階)
        int step = 0;
        if (input.Repeat(GameAction.Up) || input.Pressed(GameAction.PageUp) || input.WheelDelta > 0) step = -1;
        else if (input.Repeat(GameAction.Down) || input.Pressed(GameAction.PageDown) || input.WheelDelta < 0) step = 1;
        else if (input.LeftClicked && input.Mouse is { } m)
        {
            if (PrevArrow.Contains(m)) step = -1;
            else if (NextArrow.Contains(m)) step = 1;
            else if (CanWalk && WalkTarget(m) is { } to)
            {
                // 今の階の通ったマス: 地図を閉じて、そこまで歩く
                Stack.Pop();
                _walkTo!(to);
                return;
            }
        }
        if (step != 0)
        {
            int next = Math.Clamp(_shown + step, 0, _floors.Count - 1);
            if (next != _shown)
            {
                _shown = next;
                Services.Audio.PlaySe(Sounds.Cursor);
            }
            return;
        }
        // 決定: 見ている階のメモを読む(書き留めたものがあれば)
        if (input.Pressed(GameAction.Confirm) && _state.Notes.Any(n => n.Floor == Floor))
        {
            Stack.Push(new MemoScene(_state, Floor));
            return;
        }
        if (input.Pressed(GameAction.Map) || input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Confirm)) Stack.Pop();
    }

    /// <summary>今の階を見ていて、自動で歩けるか。</summary>
    private bool CanWalk => _walkTo != null && Floor == _here && _party != null;

    /// <summary>押した点の、通ったマス(ほかは null)。</summary>
    private GridPos? WalkTarget(Vector2 point)
    {
        var map = _state.AutoMap.For(Floor);
        var cell = AutoMapRenderer.CellAt(_state.Dungeon.Floors[Floor], map, Area, _center, _party?.Cell, radius: null, fitKnown: true, point);
        return cell is { } c && map.Visited.Contains(c) ? c : null;
    }

    public override void Draw(Draw2D d)
    {
        int floor = Floor;
        bool here = floor == _here;
        var map = _state.AutoMap.For(floor);
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 150));
        // ほかの階はパーティの印を出さず、分かっているところが収まるように出す
        var center = here ? _center : map.Visited.FirstOrDefault(_center);
        AutoMapRenderer.Draw(d, _state.Dungeon.Floors[floor], map, Area, center,
            here && _party is { } p ? (p.Cell, p.Facing) : null, radius: null, notes: _state.Notes.Where(n => n.Floor == floor).Select(n => n.Cell).ToHashSet(),
            fitKnown: true, arrivals: _state.AutoMap.ArrivalsOn(floor).ToHashSet(), portals: _state.PortalsOn(floor).ToHashSet());   // 分かっているところだけ(まだ行っていない広さは見せない)

        var s = Services.Strings;
        string title = _label?.Invoke(floor) ?? $"B{floor + 1}";
        if (here) title += s["map.here"];
        d.Text(title, new Vector2(640, 14), 22, Theme.Text, TextAlign.Center);
        if (_floors.Count > 1)
        {
            d.Text("◀", new Vector2(PrevArrow.X + PrevArrow.Width / 2, PrevArrow.Y + 4), 24, _shown > 0 ? Theme.Text : Theme.Disabled, TextAlign.Center);
            d.Text("▶", new Vector2(NextArrow.X + NextArrow.Width / 2, NextArrow.Y + 4), 24, _shown < _floors.Count - 1 ? Theme.Text : Theme.Disabled, TextAlign.Center);
        }
        bool hasNotes = _state.Notes.Any(n => n.Floor == floor);
        string help = s[hasNotes ? "map.close_memo" : "map.close"];
        if (_floors.Count > 1) help = s["map.floors"] + "   " + help;
        if (CanWalk) help = s["map.walk"] + "   " + help;
        d.Text(help, new Vector2(640, 672), 16, new Color(150, 160, 170), TextAlign.Center);
    }
}

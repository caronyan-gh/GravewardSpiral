using GravewardSpiral.Core.Maze;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// オートマップを描く。通ったマスを薄く塗り、見えた壁と扉を線で描く。
/// 小窓(パーティを中心に周りだけ)と、階の全体の両方に使う。
/// </summary>
public static class AutoMapRenderer
{
    private static readonly Color Panel = new(10, 14, 20, 235);
    private static readonly Color Frame = new(70, 90, 110);
    private static readonly Color Visited = new(30, 44, 60);
    private static readonly Color Wall = new(150, 200, 240);
    private static readonly Color Door = new(230, 180, 90);
    private static readonly Color Locked = new(235, 90, 80);
    private static readonly Color Party = new(255, 120, 100);
    private static readonly Color Grid = new(22, 28, 36);

    /// <param name="center">真ん中に置くマス(霧の中では、最後に分かっていた場所)。</param>
    /// <param name="party">パーティの印を出す場所と向き。霧の中では null。</param>
    /// <param name="radius">中心から何マスまで出すか。null なら階の全体を出す。</param>
    /// <param name="fitKnown">階の全体を出すとき、分かっているところ(通ったマス・見えた辺・印・パーティ)の周りだけにする。まだ行っていない広さを見せない。</param>
    /// <summary>地図の印の文字(text の automap.*。上り・下り階段、昇降機、テレポーター、落とし穴、シュート)。</summary>
    public static Text.Strings? Strings { get; set; }

    private static string Label(string key) => Strings?["automap." + key] ?? key[..1];

    public static void Draw(Draw2D d, MazeFloor floor, FloorMap map, RectangleF area, GridPos center,
        (GridPos Cell, Direction Facing)? party, int? radius, IReadOnlySet<GridPos>? notes = null, bool fitKnown = false)
    {
        d.FillRect(area, Panel);
        d.Rect(area, Frame, 1f);

        int cols = radius is int r ? 2 * r + 1 : floor.Width;
        int rows = radius is int r2 ? 2 * r2 + 1 : floor.Height;
        int left = 0, top = 0;
        if (radius == null && fitKnown) (left, top, cols, rows) = KnownBounds(floor, map, party?.Cell ?? center);
        float pad = 8f;
        float cell = MathF.Floor(MathF.Min((area.Width - 2 * pad) / cols, (area.Height - 2 * pad) / rows));
        // 少ししか分かっていなくても、階の全体を出すときと同じ大きさのまま(広さは隠し、縮尺は変えない)
        if (radius == null && fitKnown) cell = MathF.Min(cell, MathF.Floor(MathF.Min((area.Width - 2 * pad) / floor.Width, (area.Height - 2 * pad) / floor.Height)));
        float ox = area.X + (area.Width - cell * cols) / 2f;
        float oy = area.Y + (area.Height - cell * rows) / 2f;
        float thick = Math.Clamp(cell / 12f, 1.5f, 3f);

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            GridPos? at = radius is int rr ? Wrap(floor, center.X - rr + col, center.Y - rr + row) : new GridPos(left + col, top + row);
            float x = ox + col * cell, y = oy + row * cell;
            d.Rect(new RectangleF(x, y, cell + 1, cell + 1), Grid, 1f);
            if (at is not { } p) continue;

            if (map.Visited.Contains(p)) d.FillRect(new RectangleF(x + 1, y + 1, cell - 1, cell - 1), Visited);
            foreach (var dir in DirectionExtensions.All)
            {
                if (map.Seen(p, dir) is not { } edge) continue;
                var (a, b) = Side(x, y, cell, dir);
                // 辺から、このマスの内側へ向かう向き(印の短い線を出す向き)
                var inward = new Vector2(-dir.Dx(), -dir.Dy());
                float tick = cell * 0.18f;
                bool knownOneWay = edge == EdgeType.OneWayOut && floor.Neighbor(p, dir) is { } n && map.Seen(n, dir.Opposite()) == EdgeType.Wall;
                if (MazeView.LooksLikeDoor(edge) && !knownOneWay)
                {
                    // 扉: 壁の線を、2本の短い線が横切る
                    d.Line(a, b, Wall, thick);
                    foreach (float t in new[] { 0.33f, 0.67f })
                    {
                        var m = Vector2.Lerp(a, b, t);
                        d.Line(m - inward * tick, m + inward * tick, edge == EdgeType.Locked ? Locked : Door, thick);   // 錠のある扉は赤
                    }
                }
                else if (knownOneWay)
                {
                    // 一方通行: 扉に見える側と、壁に見える側の両方を見たら印を出す。短い線は扉に見える側(このマス)にだけ出す
                    d.Line(a, b, Wall, thick);
                    foreach (float t in new[] { 0.33f, 0.67f })
                    {
                        var m = Vector2.Lerp(a, b, t);
                        d.Line(m, m + inward * tick, Door, thick);
                    }
                }
                else if (!MazeView.SeeThrough(edge))
                {
                    d.Line(a, b, Wall, thick);
                }
            }
            var feature = floor[p].Feature;
            string? label0 = feature switch { CellFeature.StairsDown => Label("down"), CellFeature.StairsUp => Label("up"), CellFeature.Elevator => Label("elevator"), _ => null };
            if (label0 != null && map.Visited.Contains(p))
                d.Text(label0, new Vector2(x + cell * 0.08f, y + cell * 0.04f), cell * 0.42f, new Color(150, 230, 160));

            if (notes != null && notes.Contains(p))
                d.Text(Label("memo"), new Vector2(x + cell * 0.52f, y + cell * 0.04f), cell * 0.42f, new Color(230, 210, 140));   // 書き留めたメモ

            if (map.Marks.TryGetValue(p, out var mark))
            {
                // 踏んで気づいた仕掛けの印(回転床には付けない)
                string label = mark switch { CellFeature.Teleporter => Label("teleporter"), CellFeature.Pit => Label("pit"), _ => Label("chute") };
                d.Text(label, new Vector2(x + cell * 0.08f, y + cell * 0.48f), cell * 0.42f, new Color(240, 140, 200));
            }

            if (party is { } pt && pt.Cell == p) DrawArrow(d, x, y, cell, pt.Facing);
        }
    }

    /// <summary>分かっているところを囲む範囲(周りに1マスの余白。端がつながっている向きは、階の幅いっぱい)。</summary>
    private static (int Left, int Top, int Cols, int Rows) KnownBounds(MazeFloor floor, FloorMap map, GridPos here)
    {
        var cells = map.Visited.Concat(map.Edges.Keys.Select(e => e.Cell)).Concat(map.Marks.Keys).Append(here).ToList();
        int minX = cells.Min(p => p.X) - 1, maxX = cells.Max(p => p.X) + 1;
        int minY = cells.Min(p => p.Y) - 1, maxY = cells.Max(p => p.Y) + 1;
        if (floor.WrapX) (minX, maxX) = (0, floor.Width - 1);
        if (floor.WrapY) (minY, maxY) = (0, floor.Height - 1);
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(floor.Width - 1, maxX);
        maxY = Math.Min(floor.Height - 1, maxY);
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static GridPos? Wrap(MazeFloor floor, int x, int y)
    {
        if (floor.WrapX) x = ((x % floor.Width) + floor.Width) % floor.Width;
        if (floor.WrapY) y = ((y % floor.Height) + floor.Height) % floor.Height;
        return floor.InBounds(x, y) ? new GridPos(x, y) : null;
    }

    private static (Vector2, Vector2) Side(float x, float y, float s, Direction d) => d switch
    {
        Direction.North => (new Vector2(x, y), new Vector2(x + s, y)),
        Direction.South => (new Vector2(x, y + s), new Vector2(x + s, y + s)),
        Direction.West => (new Vector2(x, y), new Vector2(x, y + s)),
        _ => (new Vector2(x + s, y), new Vector2(x + s, y + s)),
    };

    private static void DrawArrow(Draw2D d, float x, float y, float s, Direction facing)
    {
        var c = new Vector2(x + s / 2f, y + s / 2f);
        var f = new Vector2(facing.Dx(), facing.Dy());
        var side = new Vector2(-f.Y, f.X);
        float size = MathF.Min(s, 40f);
        var tip = c + f * size * 0.32f;
        var l = c - f * size * 0.25f + side * size * 0.22f;
        var r = c - f * size * 0.25f - side * size * 0.22f;
        float t = Math.Clamp(size / 10f, 2f, 3.5f);
        d.Line(tip, l, Party, t);
        d.Line(l, r, Party, t);
        d.Line(r, tip, Party, t);
    }
}

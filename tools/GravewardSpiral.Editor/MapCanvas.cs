using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GravewardSpiral.Core.Maze;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 迷宮1階の地図。マスと辺を描き、クリックを「どのマスか」「どの辺か」に変えて知らせる。
/// EdgeMode(辺の筆を選んでいるとき)は、どこをクリックしても一番近い辺を知らせる(押したまま動かすと続けて塗れる)。
/// そうでなければ、クリックしたマスを選び、押したまま動かすと四角く選ぶ(Selection)。
/// マウスの下の、今の道具が当たる辺かマスを光らせる。
/// </summary>
public sealed class MapCanvas : FrameworkElement
{
    public const double Cell = 30, Pad = 24, EdgeHit = 6;

    private MazeFloor? _floor;
    private (GridPos A, GridPos B)? _selection;
    private GridPos? _lastPainted;
    private (GridPos Cell, Direction Dir)? _lastEdge;
    private (GridPos Cell, Direction Dir)? _hoverEdge;
    private GridPos? _hoverCell;

    /// <summary>辺の筆を選んでいる(クリックは一番近い辺に当たる)。</summary>
    public bool EdgeMode
    {
        get => _edgeMode;
        set
        {
            _edgeMode = value;
            _hoverEdge = null;
            _hoverCell = null;
            InvalidateVisual();
        }
    }
    private bool _edgeMode;

    public MazeFloor? Floor
    {
        get => _floor;
        set
        {
            _floor = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>名のある敵のねぐら(この階のマス。地図に「名」で出す)。</summary>
    public IReadOnlyCollection<GridPos> Lairs { get; set; } = [];

    /// <summary>エレベーターの縦穴が通るが止まらないマス(薄い e で出す)。</summary>
    public IReadOnlyCollection<GridPos> ShaftPasses { get; set; } = [];

    /// <summary>選んでいる四角(向かい合う2つの角)。無ければ null。</summary>
    public (GridPos A, GridPos B)? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    /// <summary>選んでいるマス(四角の中のマス全部)。</summary>
    public List<GridPos> SelectedCells()
    {
        if (_selection is not { } s) return [];
        int x0 = Math.Min(s.A.X, s.B.X), x1 = Math.Max(s.A.X, s.B.X), y0 = Math.Min(s.A.Y, s.B.Y), y1 = Math.Max(s.A.Y, s.B.Y);
        return [.. from y in Enumerable.Range(y0, y1 - y0 + 1) from x in Enumerable.Range(x0, x1 - x0 + 1) select new GridPos(x, y)];
    }

    /// <summary>辺をクリックした、か押したまま入った(マス、向き、右クリックか、押し始めか)。</summary>
    public event Action<GridPos, Direction, bool, bool>? EdgeClicked;

    /// <summary>選ぶ四角が変わった(押したまま動かしているあいだも)。done は、ボタンを離したとき。</summary>
    public event Action<bool>? SelectionChanged;

    public MapCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        _floor == null ? new Size(0, 0) : new Size(_floor.Width * Cell + Pad * 2, _floor.Height * Cell + Pad * 2);

    private Rect CellRect(int x, int y) => new(Pad + x * Cell, Pad + y * Cell, Cell, Cell);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_floor == null) return;
        Focus();
        CaptureMouse();
        bool right = e.ChangedButton == MouseButton.Right;
        var p = e.GetPosition(this);
        if (_edgeMode)
        {
            if (NearestEdge(p) is { } edge)
            {
                _lastEdge = Normal(edge);
                EdgeClicked?.Invoke(edge.Cell, edge.Dir, right, true);
            }
        }
        else if (HitCell(p) is { } cell)
        {
            _lastPainted = cell;
            _selection = (cell, cell);
            SelectionChanged?.Invoke(false);
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_floor == null) return;
        // 光らせる所(今の道具が当たる辺かマス)
        var at = e.GetPosition(this);
        var hoverEdge = _edgeMode ? NearestEdge(at) is { } he ? Normal(he) : ((GridPos, Direction)?)null : null;
        var hoverCell = _edgeMode ? null : HitCell(at);
        if (hoverEdge != _hoverEdge || hoverCell != _hoverCell)
        {
            (_hoverEdge, _hoverCell) = (hoverEdge, hoverCell);
            InvalidateVisual();
        }
        if (_lastPainted == null && _lastEdge == null) return;
        bool left = e.LeftButton == MouseButtonState.Pressed, right = e.RightButton == MouseButtonState.Pressed;
        if (!left && !right) return;
        if (_lastEdge != null)
        {
            if (NearestEdge(e.GetPosition(this)) is { } edge && Normal(edge) != _lastEdge)
            {
                _lastEdge = Normal(edge);
                EdgeClicked?.Invoke(edge.Cell, edge.Dir, right, false);
                InvalidateVisual();
            }
            return;
        }
        if (HitCell(e.GetPosition(this)) is { } cell && cell != _lastPainted && _selection is { } sel)
        {
            _lastPainted = cell;
            _selection = (sel.A, cell);
            SelectionChanged?.Invoke(false);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_lastPainted != null) SelectionChanged?.Invoke(true);
        _lastPainted = null;
        _lastEdge = null;
        ReleaseMouseCapture();
    }

    private GridPos? HitCell(Point p)
    {
        int x = (int)Math.Floor((p.X - Pad) / Cell), y = (int)Math.Floor((p.Y - Pad) / Cell);
        return _floor != null && _floor.InBounds(x, y) ? new GridPos(x, y) : null;
    }

    /// <summary>同じ辺を同じ形で(西と北は、となりのマスの東と南に)。</summary>
    private (GridPos Cell, Direction Dir) Normal((GridPos Cell, Direction Dir) e) =>
        e.Dir is Direction.West or Direction.North && _floor!.Neighbor(e.Cell, e.Dir) is { } n ? (n, e.Dir.Opposite()) : e;

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        (_hoverEdge, _hoverCell) = (null, null);
        InvalidateVisual();
    }

    /// <summary>その点に一番近い、マスの縁(マスの外なら null)。</summary>
    private (GridPos Cell, Direction Dir)? NearestEdge(Point p)
    {
        if (HitCell(p) is not { } c) return null;
        var r = CellRect(c.X, c.Y);
        double dn = p.Y - r.Top, ds = r.Bottom - p.Y, dw = p.X - r.Left, de = r.Right - p.X;
        double min = Math.Min(Math.Min(dn, ds), Math.Min(dw, de));
        var d = min == dn ? Direction.North : min == ds ? Direction.South : min == dw ? Direction.West : Direction.East;
        return (c, d);
    }

    private static readonly Brush Floor0 = Brushes.White;
    private static readonly Brush SolidBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly Brush DarkBrush = new SolidColorBrush(Color.FromRgb(200, 200, 215));
    private static readonly Brush FogBrush = new SolidColorBrush(Color.FromRgb(215, 235, 245));
    private static readonly Brush AntiBrush = new SolidColorBrush(Color.FromArgb(70, 160, 60, 200));
    private static readonly Brush RoomBrush = new SolidColorBrush(Color.FromArgb(40, 230, 170, 40));
    private static readonly Pen WallPen = new(Brushes.Black, 2.5);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(225, 225, 225)), 1);
    private static readonly Pen DoorPen = new(new SolidColorBrush(Color.FromRgb(150, 90, 30)), 4);
    private static readonly Pen SecretPen = new(new SolidColorBrush(Color.FromRgb(200, 40, 40)), 3) { DashStyle = DashStyles.Dash };
    private static readonly Pen LockedPen = new(new SolidColorBrush(Color.FromRgb(230, 140, 0)), 5);
    private static readonly Pen OneWayPen = new(new SolidColorBrush(Color.FromRgb(30, 110, 200)), 2.5);
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(50, 59, 91, 219));
    private static readonly Pen HoverPen = new(new SolidColorBrush(Color.FromArgb(160, 59, 91, 219)), 1.5);
    private static readonly Pen HoverEdgePen = new(new SolidColorBrush(Color.FromArgb(150, 59, 91, 219)), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    private static readonly Pen SelectPen = new(new SolidColorBrush(Color.FromRgb(0, 120, 255)), 3);
    private static readonly Pen RoomPen = new(new SolidColorBrush(Color.FromRgb(220, 150, 20)), 1.5) { DashStyle = DashStyles.Dot };
    private static readonly Typeface Face = new("Yu Gothic UI");

    protected override void OnRender(DrawingContext dc)
    {
        if (_floor is not { } f) return;
        dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int x = 0; x < f.Width; x++) Label(dc, (x).ToString(), new Point(Pad + x * Cell + Cell / 2, Pad / 2), 10, Brushes.Gray, dpi);
        for (int y = 0; y < f.Height; y++) Label(dc, (y).ToString(), new Point(Pad / 2, Pad + y * Cell + Cell / 2), 10, Brushes.Gray, dpi);
        foreach (var p in f.AllCells())
        {
            var c = f[p];
            var r = CellRect(p.X, p.Y);
            Brush bg = c.IsSolid ? SolidBrush : c.Zone.HasFlag(Zone.Dark) ? DarkBrush : c.Zone.HasFlag(Zone.Fog) ? FogBrush : Floor0;
            dc.DrawRectangle(bg, GridPen, r);
            if (c.Zone.HasFlag(Zone.AntiMagic)) dc.DrawRectangle(AntiBrush, null, r);
        }
        foreach (var room in f.Rooms)
        {
            var r = new Rect(Pad + room.X * Cell + 2, Pad + room.Y * Cell + 2, room.W * Cell - 4, room.H * Cell - 4);
            dc.DrawRectangle(RoomBrush, RoomPen, r);
        }
        foreach (var p in f.AllCells())
        {
            var c = f[p];
            var r = CellRect(p.X, p.Y);
            string? feature = c.Feature switch
            {
                CellFeature.StairsUp => T("上", "U"), CellFeature.StairsDown => T("下", "D"), CellFeature.Spinner => T("回", "S"), CellFeature.Teleporter => "T",
                CellFeature.Pit => T("穴", "P"), CellFeature.Chute => T("落", "C"), CellFeature.Elevator => "E", _ => null,
            };
            if (feature != null) Label(dc, feature, new Point(r.Left + Cell / 2, r.Top + Cell / 2 - 3), 13, Brushes.DarkGreen, dpi, bold: true);
            if (c.Mark is { } mark)
            {
                string tag = mark.StartsWith(Core.Events.EventPlacer.MarkPrefix) ? "★" : mark.StartsWith("boss:") ? T("主", "B") : mark == "key" ? T("鍵", "K")
                    : mark.StartsWith("message:") ? T("文", "M") : mark.StartsWith("gate:") ? T("門", "G") : mark.StartsWith("elevator:") ? "" : T("印", "*");
                if (tag.Length > 0) Label(dc, tag, new Point(r.Right - 7, r.Bottom - 8), 10, Brushes.Purple, dpi, bold: true);
            }
        }
        foreach (var p in ShaftPasses.Where(p => f.InBounds(p.X, p.Y)))
        {
            var r = CellRect(p.X, p.Y);
            Label(dc, "e", new Point(r.Left + r.Width / 2, r.Top + r.Height / 2), 11, new SolidColorBrush(Color.FromArgb(140, 120, 120, 120)), dpi);
        }
        foreach (var p in Lairs.Where(p => f.InBounds(p.X, p.Y)))
        {
            var r = CellRect(p.X, p.Y);
            Label(dc, Loc.T("名", "N"), new Point(r.Left + 7, r.Bottom - 8), 10, Brushes.Firebrick, dpi, bold: true);
        }
        // 辺: 東と南だけ描く(西と北は、となりのマスの東と南。外周の西と北は自分で描く)
        foreach (var p in f.AllCells())
        {
            var r = CellRect(p.X, p.Y);
            DrawEdge(dc, f[p][Direction.East], r.TopRight, r.BottomRight, Direction.East);
            DrawEdge(dc, f[p][Direction.South], r.BottomLeft, r.BottomRight, Direction.South);
            if (p.X == 0) DrawEdge(dc, f[p][Direction.West], r.TopLeft, r.BottomLeft, Direction.West);
            if (p.Y == 0) DrawEdge(dc, f[p][Direction.North], r.TopLeft, r.TopRight, Direction.North);
        }
        // 着く場所(矢印)
        var s = CellRect(f.Start.X, f.Start.Y);
        var center = new Point(s.Left + Cell / 2, s.Top + Cell / 2);
        var tip = new Point(center.X + f.StartFacing.Dx() * Cell * 0.35, center.Y + f.StartFacing.Dy() * Cell * 0.35);
        dc.DrawLine(new Pen(Brushes.Red, 3), center, tip);
        dc.DrawEllipse(Brushes.Red, null, center, 3.5, 3.5);
        if (_selection is { } sel && f.InBounds(sel.A.X, sel.A.Y) && f.InBounds(sel.B.X, sel.B.Y))
        {
            var a = CellRect(Math.Min(sel.A.X, sel.B.X), Math.Min(sel.A.Y, sel.B.Y));
            var b = CellRect(Math.Max(sel.A.X, sel.B.X), Math.Max(sel.A.Y, sel.B.Y));
            dc.DrawRectangle(HoverBrush, SelectPen, new Rect(a.TopLeft, b.BottomRight));
        }
        // 今の道具が当たる所
        if (_hoverCell is { } hc && f.InBounds(hc.X, hc.Y)) dc.DrawRectangle(HoverBrush, HoverPen, CellRect(hc.X, hc.Y));
        if (_hoverEdge is { } he && f.InBounds(he.Cell.X, he.Cell.Y))
        {
            var r = CellRect(he.Cell.X, he.Cell.Y);
            var (a, b) = he.Dir switch
            {
                Direction.North => (r.TopLeft, r.TopRight), Direction.South => (r.BottomLeft, r.BottomRight),
                Direction.West => (r.TopLeft, r.BottomLeft), _ => (r.TopRight, r.BottomRight),
            };
            dc.DrawLine(HoverEdgePen, a, b);
        }
    }

    private static void DrawEdge(DrawingContext dc, EdgeType t, Point a, Point b, Direction d)
    {
        switch (t)
        {
            case EdgeType.Open:
                return;
            case EdgeType.Wall:
                dc.DrawLine(WallPen, a, b);
                return;
            case EdgeType.Door:
                dc.DrawLine(DoorPen, Lerp(a, b, 0.25), Lerp(a, b, 0.75));
                dc.DrawLine(WallPen, a, Lerp(a, b, 0.25));
                dc.DrawLine(WallPen, Lerp(a, b, 0.75), b);
                return;
            case EdgeType.SecretDoor:
                dc.DrawLine(SecretPen, a, b);
                return;
            case EdgeType.Locked:
                dc.DrawLine(LockedPen, Lerp(a, b, 0.2), Lerp(a, b, 0.8));
                dc.DrawLine(WallPen, a, Lerp(a, b, 0.2));
                dc.DrawLine(WallPen, Lerp(a, b, 0.8), b);
                return;
            case EdgeType.OneWayOut or EdgeType.OneWayIn:
            {
                // 矢印は、通れる向き(このマスから出る向き、か入る向き)
                var mid = Lerp(a, b, 0.5);
                var dir = t == EdgeType.OneWayOut ? d : d.Opposite();
                var v = new Vector(dir.Dx(), dir.Dy()) * 7;
                var n = new Vector(-v.Y, v.X) * 0.6;
                dc.DrawLine(OneWayPen, mid - v, mid + v);
                dc.DrawLine(OneWayPen, mid + v, mid + v * 0.1 + n);
                dc.DrawLine(OneWayPen, mid + v, mid + v * 0.1 - n);
                return;
            }
        }
    }

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static void Label(DrawingContext dc, string text, Point center, double size, Brush brush, double dpi, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : Face, size, brush, dpi);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}

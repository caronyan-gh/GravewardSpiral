using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 迷宮: 左に階の一覧、真ん中に地図、右に壁と扉の筆と、選んだマス・この階の項目。
/// 地図をクリックするとマスを選び、押したまま動かすと四角く選ぶ。右の「このマスにあるもの」(1マスに1つ: 階段・仕掛け・岩盤・イベント・ボス・鍵・文言・門)を選ぶと、
/// それに合う欄だけが下に出る。見え方(ふつう・暗闇・霧のどれか1つ)と魔法禁止、部屋、着く場所は別の欄。四角く選んだときは、選んだマス全部に当てる。
/// 壁と扉は筆で塗る: 筆を選んでいるあいだは、クリックが一番近いマスの縁に当たる(一方通行は同じ辺をもう一度で向きを逆に。右クリックで壁を消す)。
/// 「使わない」か Esc で、マスを選ぶに戻る。
/// </summary>
public sealed class MazeTab : IEditorTab
{
    private readonly EditorContext _context;
    private readonly Grid _view = new();
    private readonly ListBox _floors = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly MapCanvas _map = new();
    private readonly StackPanel _cellPanel = new();
    private readonly StackPanel _floorPanel = new() { Margin = new Thickness(4, 0, 4, 4) };
    private readonly TextBox _seed = new() { Text = "1" };
    private readonly List<RadioButton> _brushButtons = [];

    /// <summary>標準のイベントから選んだもの(イベントの欄の候補に足す)。</summary>
    private readonly List<Choice> _pickedStandard = [];
    private EdgeType? _brush;
    private bool _loading;

    public MazeTab(EditorContext context, int floor)
    {
        _context = context;
        Floor = Math.Max(0, floor);
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

        // 左: 階の一覧と、階の操作
        var left = new StackPanel { Margin = new Thickness(8) };
        left.Children.Add(Ui.Heading(T("階", "Floors")));
        _floors.Height = 260;
        _floors.SelectionChanged += (_, _) =>
        {
            if (_loading || _floors.SelectedIndex < 0) return;
            Floor = _floors.SelectedIndex;
            ShowFloor();
        };
        left.Children.Add(_floors);
        left.Children.Add(Stack(Ui.Button(T("自動で足す", "Add (generated)"), () => AddFloor(blank: false)), Ui.Button(T("空で足す", "Add (blank)"), () => AddFloor(blank: true))));
        left.Children.Add(Stack(Ui.Button(T("上へ", "Up"), () => MoveFloor(-1)), Ui.Button(T("下へ", "Down"), () => MoveFloor(1)), Ui.Button(T("消す", "Delete"), RemoveFloor)));
        left.Children.Add(Ui.Heading(T("作り直す", "Regenerate")));
        left.Children.Add(Ui.Row(T("シード", "Seed"), _seed, 60));
        left.Children.Add(Ui.Button(T("この階を自動で作り直す", "Regenerate this floor"), Regenerate, T("同じ大きさで作り直す。ほかの階とつなぐ仕掛けは、検査で確かめる", "Regenerates at the same size. Check links to other floors in the Check tab.")));
        _view.Children.Add(Scrolled(left, 0));

        // 真ん中: 地図
        _map.EdgeClicked += OnEdge;
        _map.SelectionChanged += _ => ShowCell();
        var mapScroll = new ScrollViewer { Content = _map, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        // 真ん中の上: この階(名前・大きさ・端のつながり)。操作の説明は右上の ? で
        var center = new DockPanel();
        DockPanel.SetDock(_floorPanel, Dock.Top);
        center.Children.Add(_floorPanel);
        center.Children.Add(mapScroll);
        Grid.SetColumn(center, 1);
        _view.Children.Add(center);
        _view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || _brush == null) return;
            _brushButtons[0].IsChecked = true;   // 筆をやめて、マスを選ぶに戻る
            e.Handled = true;
        };

        // 地図と右の間: 縦の道具の窓(選ぶと、壁と扉の筆)。四角いボタンに印と短い名前
        var tools = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        RadioButton Tool(string glyph, string glyphColor, string label, string tip, bool icon = false)
        {
            var mark = new TextBlock { Text = glyph, FontSize = icon ? 15 : 16, FontWeight = FontWeights.Bold, Foreground = Theme.Brush(glyphColor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (icon) mark.FontFamily = Theme.Icons;
            var b = new RadioButton { Content = mark, GroupName = "brush", Style = (Style)Application.Current.FindResource("ToolButton"), ToolTip = $"{label}: {tip}" };
            _brushButtons.Add(b);
            tools.Children.Add(b);
            return b;
        }
        var select = Tool("\uE7C9", "#3B5BDB", T("選ぶ", "Select"), T("マスを選ぶ(押したまま動かすと四角く)。Esc でここに戻る", "select cells (drag for a rectangle). Esc comes back here"), icon: true);
        select.IsChecked = true;
        select.Checked += (_, _) => SetBrush(null);
        tools.Children.Add(new Border { Height = 1, Background = Theme.Brush(Theme.Palette.Border), Margin = new Thickness(4, 2, 4, 6) });
        foreach (var (type, label, labelEn, glyph, color) in Brushes)
        {
            var b = Tool(glyph, color, T(label, labelEn), T($"マスの縁をこれにする(押したまま続けて。右クリックで壁を消す)", "make cell edges this (drag to continue; right-click removes walls)"));
            b.Tag = type;
            b.Checked += (_, _) => SetBrush((EdgeType)b.Tag);
        }
        Grid.SetColumn(tools, 2);
        _view.Children.Add(tools);

        // 右: 選んだマスの情報だけ
        var right = new StackPanel { Margin = new Thickness(8) };
        right.Children.Add(Ui.Heading(T("選んだマス", "Selected cells")));
        right.Children.Add(_cellPanel);
        _view.Children.Add(Scrolled(right, 3));
        // イベントのタブで足したイベントも出るように、このタブを開くたびに選んだマスの欄を作り直す
        _view.IsVisibleChanged += (_, _) => { if (_view.IsVisible) Refresh(); };
    }

    /// <summary>
    /// ほかのタブで直したもの(物語のタブで置いたボス、敵と宝のタブのねぐら、イベントのタブで足したイベント)を、地図と右の欄に映す。
    /// 選んでいる階とマスはそのまま。
    /// </summary>
    private void Refresh()
    {
        _map.Lairs = _context.Pack?.Named?.Where(n => n.Lair && n.Floor == Floor).Select(n => new GridPos(n.X, n.Y)).ToList() ?? [];
        ShowShaftPasses();
        _map.InvalidateVisual();
        ShowCell();
        ShowFloorProps();
    }

    /// <summary>マスを選ぶ(確かめ用の --select)。</summary>
    public void Select(GridPos a, GridPos b)
    {
        _map.Selection = (a, b);
        ShowCell();
    }

    private void SetBrush(EdgeType? brush)
    {
        _brush = brush;
        _map.EdgeMode = brush != null;
    }

    /// <summary>選んだマスの欄の、左の名前の幅(中身の欄を広く取る)。</summary>
    private const double LabelWidth = 86;

    private static Style Palette => (Style)Application.Current.FindResource("PaletteButton");

    /// <summary>辺の筆(種類、名前、地図の描き方に合わせた印と色)。一方通行は、塗った辺の向きをクリックで変える。</summary>
    private static readonly (EdgeType Type, string Label, string LabelEn, string Glyph, string Color)[] Brushes =
    [
        (EdgeType.Wall, "壁", "Wall", "━", "#000000"), (EdgeType.Open, "通路", "Open", "┅", "#B0B0B0"), (EdgeType.Door, "扉", "Door", "━", "#965A1E"),
        (EdgeType.SecretDoor, "隠し扉", "Secret door", "┅", "#C82828"), (EdgeType.Locked, "錠", "Locked", "━", "#E68C00"),
        (EdgeType.OneWayOut, "一方通行", "One-way", "→", "#1E6EC8"),
    ];

    /// <summary>このマスにあるもの(1マスに1つ)。キー、名前。</summary>
    private static readonly (string Key, string Ja, string En)[] Things =
    [
        ("none", "なし", "Nothing"), ("up", "上り階段", "Stairs up"), ("down", "下り階段", "Stairs down"), ("spinner", "回転床", "Spinner"),
        ("teleport", "テレポーター", "Teleporter"), ("pit", "落とし穴", "Pit"), ("chute", "シュート", "Chute"), ("elevator", "エレベーター", "Elevator"),
        ("solid", "岩盤(入れないマス)", "Solid rock"), ("event", "イベント", "Event"), ("boss", "ボス", "Boss"), ("key", "鍵", "Key"),
        ("message", "文言(張り紙など)", "Text"), ("gate", "門(仕掛けで開く扉)", "Gate"), ("dest", "着地点(イベントで移す先)", "Landing point"),
    ];

    public string Key => "maze";
    public string Header => T("迷宮", "Maze");
    public UIElement View => _view;

    /// <summary>選んでいる階(0 が B1)。</summary>
    public int Floor { get; private set; }

    private MazeFloor? Current => _context.Pack is { } p && Floor < p.Floors.Count ? p.Floors[Floor] : null;

    public void Reload()
    {
        if (_context.Pack is { } p) Floor = Math.Clamp(Floor, 0, Math.Max(0, p.Floors.Count - 1));
        ShowFloor();
    }

    private void ShowFloor()
    {
        _loading = true;
        _floors.Items.Clear();
        if (_context.Pack is { } p)
            for (int i = 0; i < p.Floors.Count; i++)
                _floors.Items.Add($"B{i + 1}  {(En ? p.Floors[i].NameEn : p.Floors[i].NameJa) ?? Loc.Name(p.Text.Floor(i)?.Name) ?? p.Floors[i].NameJa ?? p.Floors[i].NameEn ?? ""}  {p.Floors[i].Width}×{p.Floors[i].Height}");
        _floors.SelectedIndex = Current != null ? Floor : -1;
        _loading = false;
        _map.Floor = Current;
        _map.Lairs = _context.Pack?.Named?.Where(n => n.Lair && n.Floor == Floor).Select(n => new GridPos(n.X, n.Y)).ToList() ?? [];
        _map.Selection = null;
        ShowCell();
        ShowFloorProps();
    }

    private void Edited()
    {
        // エレベーターの縦穴は、マスが空いているかで止まる階が変わるので、直すたびに入れ直す
        if (_context.Pack is { } pack) pack.SetElevators(pack.Elevators.ToList());
        ShowShaftPasses();
        _map.InvalidateVisual();
        _context.MarkEdited();
    }

    private void OnEdge(GridPos p, Direction d, bool erase, bool first)
    {
        if (Current is not { } f || _brush is not { } brush) return;
        if (erase) MazeEdit.SetEdge(f, p, d, EdgeType.Open);   // 消す = 壁や扉を消して通路にする
        else MazeEdit.PaintEdge(f, p, d, brush, flip: first);   // 一方通行の向きは、押し始めの1回だけ変える
        Edited();
    }


    /// <summary>この階で、縦穴が通るが止まらないマスを地図に出す。</summary>
    private void ShowShaftPasses() =>
        _map.ShaftPasses = _context.Pack is { } pack && Current is { } f
            ? pack.Elevators.Where(s => s.Covers(Floor) && f.InBounds(s.X, s.Y) && f[s.Cell].Mark != s.Mark).Select(s => s.Cell).ToList()
            : [];

    /// <summary>このマスが止まる階になっている縦穴(無ければ null)。</summary>
    private ElevatorShaft? ShaftOf(MazeCell c) => _context.Pack?.Elevators.FirstOrDefault(s => s.Mark == c.Mark);

    /// <summary>そのマスにあるもの(Things のキー)。どれにも当たらない印は "other"。</summary>
    private static string ThingOf(MazeCell c) => c.Feature switch
    {
        CellFeature.StairsUp => "up", CellFeature.StairsDown => "down", CellFeature.Spinner => "spinner", CellFeature.Teleporter => "teleport",
        CellFeature.Pit => "pit", CellFeature.Chute => "chute", CellFeature.Elevator => "elevator", CellFeature.Solid => "solid",
        _ => c.Mark switch
        {
            null => "none",
            "key" => "key",
            { } m when m.StartsWith(EventPlacer.MarkPrefix) => "event",
            { } m when m.StartsWith("boss:") => "boss",
            { } m when m.StartsWith("message:") => "message",
            { } m when m.StartsWith("gate:") => "gate",
            { } m when m.StartsWith("dest:") => "dest",
            _ => "other",
        },
    };

    /// <summary>
    /// マスにあるものを替える(前の仕掛けと印は消す)。岩盤から外すと、四方は壁のまま(「四方の壁を消す」で開ける)。
    /// イベントやボスなどは、はじめの中身を入れておく(下の欄で直す)。
    /// </summary>
    private void SetThing(MazeFloor f, GridPos p, string thing)
    {
        var c = f[p];
        // エレベーターを「なし」にしたら、縦穴ごと消す(ほかのものにしたら、この階だけ止まらなくなる)
        if (ShaftOf(c) is { } shaft && _context.Pack is { } owner)
        {
            if (thing == "none")
            {
                owner.SetElevators(owner.Elevators.Where(s => s != shaft));
                return;
            }
            if (thing == "elevator") return;
        }
        if (c.IsSolid && thing != "solid") MazeEdit.SetFeature(f, p, CellFeature.None);
        MazeEdit.SetFeature(f, p, CellFeature.None);
        c.Mark = null;
        switch (thing)
        {
            case "up": MazeEdit.SetFeature(f, p, CellFeature.StairsUp); break;
            case "down": MazeEdit.SetFeature(f, p, CellFeature.StairsDown); break;
            case "spinner": MazeEdit.SetFeature(f, p, CellFeature.Spinner); break;
            case "pit": MazeEdit.SetFeature(f, p, CellFeature.Pit); break;
            case "teleport": MazeEdit.SetFeature(f, p, CellFeature.Teleporter, new WarpTarget(null, p)); break;
            case "chute": MazeEdit.SetFeature(f, p, CellFeature.Chute, new WarpTarget(Floor + 1, p)); break;
            case "elevator" when _context.Pack is { } pack:
            {
                // ここに縦穴を通す(はじめはこの階と、その下の階。上下の階は下の欄で直す)。同じ座標の縦穴は置き換える
                int count = pack.Floors.Count, from = Floor + 1, to = Math.Min(count, Floor + 2);
                if (to == from) from = Math.Max(1, from - 1);
                pack.SetElevators(pack.Elevators.Where(s => s.X != p.X || s.Y != p.Y).Append(new ElevatorShaft(p.X, p.Y, from, to)));
                break;
            }
            case "solid": MazeEdit.SetFeature(f, p, CellFeature.Solid); break;
            case "event": c.Mark = EventChoices().FirstOrDefault() is { } ev ? EventPlacer.MarkPrefix + ev.Key : null; break;
            case "boss": c.Mark = "boss:"; break;   // どのボスかは、下の欄で選ぶ(はじめは空)
            case "key": c.Mark = "key"; break;
            case "message": c.Mark = "message:" + (_context.Pack?.Id ?? "scenario") + ".text1"; break;
            case "gate": c.Mark = "gate:gate1"; break;
            case "dest": c.Mark = "dest:dest1"; break;
        }
    }

    /// <summary>イベントの候補: このシナリオの迷宮のイベント(名前つき)、迷宮に置いてある標準のイベント、標準のイベントから選んだもの。</summary>
    private List<Choice> EventChoices()
    {
        var mine = _context.Pack?.Events ?? [];
        var list = mine.Where(e => e.Place == "maze").Select(e => new Choice(e.Id, EventList.Name(e) ?? "")).ToList();
        if (_context.Pack is { } pack)
            foreach (var id in pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
                         .Where(m => m.StartsWith(EventPlacer.MarkPrefix)).Select(m => m[EventPlacer.MarkPrefix.Length..]).Distinct().Order(StringComparer.Ordinal))
                if (list.All(x => x.Key != id) && _context.BaseData.Events.TryGetValue(id, out var std))
                    list.Add(new Choice(id, T($"{EventList.Name(std)}(標準)", $"{EventList.Name(std)} (standard)")));
        list.AddRange(_pickedStandard.Where(c => list.All(x => x.Key != c.Key)));
        return list;
    }

    /// <summary>ボスの候補(最下層の主と、物語のタブで足したボス)。</summary>
    private List<Choice> BossChoices()
    {
        var bosses = new List<Choice> { new(Core.Save.GameSession.FinalBossId, T("最下層の主", "Final boss")) };
        if (_context.Pack is { } pack)
            bosses.AddRange(pack.Bosses.Select(b => (string?)b["id"]).OfType<string>().Select(id => new Choice(id, "")));
        return bosses;
    }

    /// <summary>選んだマス(1つか、四角く選んだもの)の欄。</summary>
    private void ShowCell()
    {
        _cellPanel.Children.Clear();
        var cells = _map.SelectedCells();
        if (Current is not { } f || cells.Count == 0 || cells.Any(p => !f.InBounds(p.X, p.Y)))
        {
            _cellPanel.Children.Add(Ui.Note(T("地図のマスをクリックすると、ここにそのマスにあるものが出て直せる。押したまま動かすと四角く選べる。", "Click a cell on the map to see and edit what is there. Drag to select a rectangle.")));
            return;
        }
        var p = cells[0];
        bool one = cells.Count == 1;
        _cellPanel.Children.Add(Ui.Note(one ? $"({p.X}, {p.Y})" : T($"{cells.Count} マス(({p.X}, {p.Y}) から {cells[^1].X - p.X + 1}×{cells[^1].Y - p.Y + 1})", $"{cells.Count} cells ({cells[^1].X - p.X + 1}×{cells[^1].Y - p.Y + 1} from ({p.X}, {p.Y}))")));

        // このマスにあるもの(四角く選んだときは、全部同じなら出す)
        var things = Things.Select(t => new Choice(t.Key, T(t.Ja, t.En))).ToList();
        var kinds = cells.Select(x => ThingOf(f[x])).Distinct().ToList();
        if (kinds.Contains("other")) things.Add(new Choice("other", T("その他の印", "Other mark")));
        if (kinds.Count > 1) things.Insert(0, new Choice("mixed", T("いろいろ(選ぶと全部をそれにする)", "Mixed (pick one to set them all)")));
        var thing = new ComboBox { ItemsSource = things, SelectedItem = things.First(t => t.Key == (kinds.Count == 1 ? kinds[0] : "mixed")) };
        thing.SelectionChanged += (_, _) =>
        {
            if (thing.SelectedItem is not Choice chosen || chosen.Key is "other" or "mixed" || kinds.Count == 1 && chosen.Key == kinds[0]) return;
            if (chosen.Key == "boss") SetThing(f, p, "boss");   // ボスは1体に1マス
            else foreach (var x in cells) SetThing(f, x, chosen.Key);
            Edited();
            ShowCell();
        };
        _cellPanel.Children.Add(Ui.Row(T("あるもの", "What is here"), thing, LabelWidth));
        if (kinds.Count == 1) ThingFields(f, cells, kinds[0]);

        // 見え方(どれか1つ)と魔法禁止
        var view = new WrapPanel();
        foreach (var (z, ja, en) in new[] { (Zone.None, "ふつう", "Normal"), (Zone.Dark, "暗闇", "Darkness"), (Zone.Fog, "霧", "Fog") })
        {
            bool all = cells.All(x => (f[x].Zone & (Zone.Dark | Zone.Fog)) == z);
            var b = new RadioButton { Content = T(ja, en), GroupName = $"view{Floor}", IsChecked = all, Style = Palette };
            b.Checked += (_, _) =>
            {
                foreach (var x in cells) f[x].Zone = (f[x].Zone & ~(Zone.Dark | Zone.Fog)) | z;
                Edited();
            };
            view.Children.Add(b);
        }
        _cellPanel.Children.Add(Ui.Row(T("見え方", "Visibility"), view, LabelWidth));
        var anti = new CheckBox { Content = T("魔法禁止(呪文が唱えられない)", "Anti-magic (no spells)"), IsChecked = cells.All(x => f[x].Zone.HasFlag(Zone.AntiMagic)) };
        anti.Click += (_, _) =>
        {
            foreach (var x in cells) f[x].Zone = anti.IsChecked == true ? f[x].Zone | Zone.AntiMagic : f[x].Zone & ~Zone.AntiMagic;
            Edited();
        };
        _cellPanel.Children.Add(Ui.Row("", anti, LabelWidth));

        // 壁(四方まとめて)
        var walls = new WrapPanel();
        walls.Children.Add(Ui.Button(T("四方の壁を消す", "Open all sides"), () =>
        {
            foreach (var x in cells)
            {
                if (f[x].IsSolid) MazeEdit.SetFeature(f, x, CellFeature.None);
                MazeEdit.OpenAll(f, x);
            }
            Edited();
            ShowCell();
        }, T("選んだマスの四方を通路にする(岩盤なら岩盤も消す)", "Make all sides of the selected cells open (clears solid rock too)")));
        walls.Children.Add(Ui.Button(T("四方を壁にする", "Wall all sides"), () =>
        {
            foreach (var x in cells) foreach (var d in DirectionExtensions.All) MazeEdit.SetEdge(f, x, d, EdgeType.Wall);
            Edited();
        }));
        _cellPanel.Children.Add(Ui.Row(T("壁", "Walls"), walls, LabelWidth));

        // 部屋
        if (one && f.RoomAt(p) is int index)
        {
            var room = f.Rooms[index];
            var roomRow = new WrapPanel();
            roomRow.Children.Add(new TextBlock { Text = T($"({room.X}, {room.Y}) から {room.W}×{room.H}", $"{room.W}×{room.H} from ({room.X}, {room.Y})"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            roomRow.Children.Add(Ui.Button(T("この部屋を消す", "Remove this room"), () =>
            {
                f.Rooms.RemoveAt(index);
                Edited();
                ShowCell();
            }, T("部屋の囲みだけを消す。壁や扉はそのまま", "Removes only the room outline; walls and doors stay")));
            _cellPanel.Children.Add(Ui.Row(T("部屋", "Room"), roomRow, LabelWidth));
        }
        else if (one)
            _cellPanel.Children.Add(Ui.Row(T("部屋", "Room"), Ui.Note(T("部屋を作るには、地図を押したまま動かして四角く選ぶ", "To make a room, drag on the map to select a rectangle")), LabelWidth));
        else
        {
            _cellPanel.Children.Add(Ui.Row(T("部屋", "Room"), Ui.Button(T("この四角を部屋にする", "Make this rectangle a room"), () =>
            {
                f.Rooms.Add(new MazeRoom(p.X, p.Y, cells[^1].X - p.X + 1, cells[^1].Y - p.Y + 1));
                Edited();
                ShowCell();
            }, T("部屋は、名のある敵のねぐらや、はめ込む部屋の目安になる", "Rooms guide named monster lairs and prefab rooms")), LabelWidth));
        }

        // 着く場所(上り階段から来たとき)
        if (one)
        {
            if (f.Start == p)
            {
                var facing = new ComboBox { ItemsSource = DirectionExtensions.All.Select(d => new Choice(d.ToString(), DirName(d))).ToList(), Width = 100 };
                facing.SelectedItem = ((List<Choice>)facing.ItemsSource).First(c => c.Key == f.StartFacing.ToString());
                facing.SelectionChanged += (_, _) =>
                {
                    if (facing.SelectedItem is not Choice c) return;
                    f.StartFacing = Enum.Parse<Direction>(c.Key);
                    Edited();
                };
                var row = new WrapPanel();
                row.Children.Add(new TextBlock { Text = T("ここに着く。向き ", "Arrive here, facing "), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(facing);
                _cellPanel.Children.Add(Ui.Row(T("着く場所", "Arrival"), row, LabelWidth));
            }
            else
                _cellPanel.Children.Add(Ui.Row(T("着く場所", "Arrival"), Ui.Button(T("ここを着く場所にする", "Arrive here"), () =>
                {
                    f.Start = p;
                    Edited();
                    ShowCell();
                }, T("上り階段から来たとき(と、この階から始めるとき)に立つマス", "Where the party stands when coming down the stairs (or starting on this floor)")), LabelWidth));
        }
    }

    private static string DirName(Direction d) => d switch
    {
        Direction.North => T("北", "North"), Direction.East => T("東", "East"), Direction.South => T("南", "South"), _ => T("西", "West"),
    };

    /// <summary>「このマスにあるもの」に合う欄(イベントならどのイベントか、テレポーターなら行き先、など)。</summary>
    private void ThingFields(MazeFloor f, List<GridPos> cells, string kind)
    {
        var p = cells[0];
        var c = f[p];
        void Marks(Func<string, string> mark, string value)
        {
            foreach (var x in cells) f[x].Mark = mark(value);
            Edited();
        }
        switch (kind)
        {
            case "event":
            {
                var list = EventChoices();
                string? id = c.Mark?[EventPlacer.MarkPrefix.Length..];
                if (id != null && list.All(x => x.Key != id)) list.Add(new Choice(id, ""));
                var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(x => x.Key == id) };
                box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice ch) Marks(v => EventPlacer.MarkPrefix + v, ch.Key); };
                _cellPanel.Children.Add(Ui.Row(T("どのイベント", "Which event"), box, LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Button(T("標準のイベントから選ぶ…", "Pick a standard event…"), () =>
                {
                    var dialog = new EventPickerWindow(_context.BaseData.Events.Values.Where(e => e.Place == "maze"), okLabel: T("このイベントにする", "Use this event")) { Owner = Application.Current.MainWindow };
                    if (dialog.ShowDialog() != true || dialog.Picked is not { } e) return;
                    _pickedStandard.RemoveAll(x => x.Key == e.Id);
                    _pickedStandard.Add(new Choice(e.Id, T($"{EventList.Name(e)}(標準)", $"{EventList.Name(e)} (standard)")));
                    Marks(v => EventPlacer.MarkPrefix + v, e.Id);
                    ShowCell();
                }, T("標準のパックの迷宮のイベントを、名前と中身を見て選ぶ。自分のイベントはイベントのタブで作る", "Pick a standard maze event by name and content. Make your own on the Events tab")), LabelWidth));
                break;
            }
            case "boss":
            {
                // 1行目は空(まだ選んでいない)。置いてあるボスには場所を添える
                var floors = _context.Pack!.Floors;
                var list = new List<Choice> { new("", T("(ボスを選ぶ)", "(choose a boss)")) };
                foreach (var b in BossChoices())
                    list.Add(MazeEdit.FindMark(floors, "boss:" + b.Key) is { } at && !(at.Floor == Floor && at.Cell == cells[0])
                        ? new Choice(b.Key, T($"{b.Label}(B{at.Floor + 1} にいる)", $"{b.Label} (on B{at.Floor + 1})")) : b);
                string? id = c.Mark?["boss:".Length..];
                var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(x => x.Key == (id ?? "")) };
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedItem is not Choice ch || ch.Key == (id ?? "")) return;
                    // ほかの所に置いてあるボスなら、動かしてよいか聞く(1体に1マスなので、前の場所からは消える)
                    if (ch.Key.Length > 0 && MazeEdit.FindMark(floors, "boss:" + ch.Key) is { } was && !(was.Floor == Floor && was.Cell == cells[0]))
                    {
                        string name = BossChoices().First(b => b.Key == ch.Key).ToString();
                        if (MessageBox.Show(T($"{name} は B{was.Floor + 1} ({was.Cell.X}, {was.Cell.Y}) に置いてある。ここへ動かしますか？", $"{name} is placed at B{was.Floor + 1} ({was.Cell.X}, {was.Cell.Y}). Move it here?"),
                                T("ボス", "Boss"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        {
                            box.SelectedItem = list.FirstOrDefault(x => x.Key == (id ?? ""));
                            return;
                        }
                    }
                    if (ch.Key.Length > 0) MazeEdit.RemoveMark(floors, "boss:" + ch.Key);   // 1体に1マス(ほかにいれば、ここへ動く)
                    c.Mark = "boss:" + ch.Key;
                    Edited();
                    ShowCell();
                };
                _cellPanel.Children.Add(Ui.Row(T("どのボス", "Which boss"), box, LabelWidth));
                if (string.IsNullOrEmpty(id))
                    _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("どのボスか選ぶ。ほかの所にいるボスを選ぶと、ここへ動く。", "Pick which boss. Picking one placed elsewhere moves it here.")), LabelWidth));
                else
                    _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("ボスは物語のタブで足す。1体に1マスで、ほかのマスに置くと動く。", "Add bosses on the Story tab. One cell per boss; placing it elsewhere moves it.")), LabelWidth));

                break;
            }
            case "message":
            {
                var key = Ui.Text(c.Mark?["message:".Length..], _ => { });
                key.LostFocus += (_, _) => { if (key.Text.Trim().Length > 0) Marks(v => "message:" + v, key.Text.Trim()); };
                _cellPanel.Children.Add(Ui.Row(T("文言のキー", "Text key"), key, LabelWidth));
                string? text = _context.Pack?.Texts.GetValueOrDefault(Lang)?.GetValueOrDefault(c.Mark?["message:".Length..] ?? "");
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(text ?? T("この文言はまだ無い。文言のタブで書く(「足りないキーを足す」)。", "This text doesn't exist yet. Write it on the Text tab (\"Add missing keys\").")), LabelWidth));
                break;
            }
            case "gate":
            {
                var name = Ui.Text(c.Mark?["gate:".Length..], _ => { });
                name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) Marks(v => "gate:" + v, name.Text.Trim()); };
                _cellPanel.Children.Add(Ui.Row(T("門の名前", "Gate name"), name, LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("イベントの手順「門を開ける」(open)で、この名前の門の錠を開ける。「門を閉める」(close)でまた閉まる。", "The event step \"Open gate\" (open) unlocks the gate with this name; \"Close gate\" (close) locks it again.")), LabelWidth));
                break;
            }
            case "dest":
            {
                var name = Ui.Text(c.Mark?["dest:".Length..], _ => { });
                name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) Marks(v => "dest:" + v, name.Text.Trim()); };
                _cellPanel.Children.Add(Ui.Row(T("着地点の名前", "Landing point name"), name, LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("イベントの手順「別のマスへ移す」(warp)で、パーティをこのマスへ移す。ほかの階からも移せる。", "The event step \"Move the party\" (warp) moves the party to this cell, also from other floors.")), LabelWidth));
                break;
            }
            case "elevator" when ShaftOf(c) is { } shaft && _context.Pack is { } pack && cells.Count == 1:
            {
                // 縦穴: 上の階と下の階を選ぶ。範囲の階のうち、このマスが空いている階に止まる
                var levels = Enumerable.Range(1, pack.Floors.Count).Select(n => $"B{n}").ToList();
                var top = new ComboBox { ItemsSource = levels, Width = 80, SelectedIndex = Math.Min(shaft.From, shaft.To) - 1 };
                var bottom = new ComboBox { ItemsSource = levels, Width = 80, SelectedIndex = Math.Max(shaft.From, shaft.To) - 1 };
                void Range()
                {
                    if (top.SelectedIndex < 0 || bottom.SelectedIndex < 0) return;
                    int a = top.SelectedIndex + 1, b = bottom.SelectedIndex + 1;
                    pack.SetElevators(pack.Elevators.Select(s => s == shaft ? s with { From = Math.Min(a, b), To = Math.Max(a, b) } : s));
                    Edited();
                    ShowCell();
                }
                top.SelectionChanged += (_, _) => Range();
                bottom.SelectionChanged += (_, _) => Range();
                var range = new WrapPanel();
                range.Children.Add(top);
                range.Children.Add(new TextBlock { Text = " 〜 ", VerticalAlignment = VerticalAlignment.Center });
                range.Children.Add(bottom);
                _cellPanel.Children.Add(Ui.Row(T("通す階", "Floors"), range, LabelWidth));
                var stops = Elevators.Stops(pack.Floors, shaft);
                _cellPanel.Children.Add(Ui.Row(T("止まる階", "Stops"), new TextBlock
                {
                    Text = string.Join("・", stops.Select(x => $"B{x + 1}")) + (stops.Count < 2 ? T("(2 つ以上の階に止まるようにする)", " (needs two or more)") : ""),
                    Foreground = stops.Count < 2 ? Ui.Warn : Theme.Brush(Theme.Palette.Text), VerticalAlignment = VerticalAlignment.Center,
                }, LabelWidth));
                // スイッチ: フラグが立つまで動かない(標準のイベント「エレベーターのスイッチ」が elevator_power を立てる)
                var power = new CheckBox
                {
                    Content = T("スイッチを入れるまで動かない", "Doesn't run until a switch is turned on"), IsChecked = shaft.Power != null, VerticalAlignment = VerticalAlignment.Center,
                };
                var flag = new TextBox { Text = shaft.Power ?? Elevators.DefaultPower, Width = 160, IsEnabled = shaft.Power != null, Margin = new Thickness(8, 0, 0, 0) };
                void Power()
                {
                    string? value = power.IsChecked == true ? (flag.Text.Trim() is { Length: > 0 } t ? t : Elevators.DefaultPower) : null;
                    pack.SetElevators(pack.Elevators.Select(s => s == shaft ? s with { Power = value } : s));
                    Edited();
                    ShowCell();
                }
                power.Click += (_, _) => Power();
                flag.LostFocus += (_, _) => { if (power.IsChecked == true && flag.Text.Trim() != shaft.Power) Power(); };
                var powerRow = new WrapPanel();
                powerRow.Children.Add(power);
                powerRow.Children.Add(flag);
                _cellPanel.Children.Add(Ui.Row(T("スイッチ", "Switch"), powerRow, LabelWidth));
                if (shaft.Power != null)
                    _cellPanel.Children.Add(Ui.Row("", Ui.Note(T($"フラグ {shaft.Power} が立つまで動かない。標準のイベント「エレベーターのスイッチ」(elevator_switch)は {Elevators.DefaultPower} を立てるので、それをどこかのマス(下の階がおすすめ)に置く。自分のイベントの「フラグを立てる」でもよい。",
                        $"Doesn't run until flag {shaft.Power} is set. The standard event \"Elevator switch\" (elevator_switch) sets {Elevators.DefaultPower}; place it on a cell somewhere (a lower floor works well). Your own event's \"Set flag\" also works.")), LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("縦穴は全部の階で同じ場所。範囲の階のうち、このマスが空いている階に自動で止まる(岩や壁、ほかの仕掛けのある階は素通り)。「なし」にすると縦穴ごと消える。",
                    "The shaft is at the same place on every floor. It stops automatically on floors in the range where this cell is open (it passes through rock, walls and other features). Setting \"Nothing\" removes the whole shaft.")), LabelWidth));
                break;
            }
            case "elevator":
            {
                var group = Ui.Text(c.Mark?.StartsWith("elevator:") == true ? c.Mark["elevator:".Length..] : "a", _ => { });
                group.LostFocus += (_, _) => { if (group.Text.Trim().Length > 0) Marks(v => "elevator:" + v, group.Text.Trim()); };
                _cellPanel.Children.Add(Ui.Row(T("組", "Group"), group, LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(T("同じ組のエレベーターがある階へ移れる。", "Goes to floors that have an elevator of the same group.")), LabelWidth));
                break;
            }
            case "teleport" or "chute" when cells.Count == 1:
            {
                var t = c.Target ?? new WarpTarget(null, p);
                var floor = new TextBox { Text = t.Floor is int tf ? (tf + 1).ToString() : "", Width = 50 };
                var x = new TextBox { Text = t.Cell.X.ToString(), Width = 50 };
                var y = new TextBox { Text = t.Cell.Y.ToString(), Width = 50 };
                void Apply()
                {
                    if (!int.TryParse(x.Text, out int tx) || !int.TryParse(y.Text, out int ty)) return;
                    int? tfl = int.TryParse(floor.Text, out int fl) && fl >= 1 ? fl - 1 : null;
                    c.Target = new WarpTarget(tfl, new GridPos(tx, ty));
                    Edited();
                }
                foreach (var box in new[] { floor, x, y }) box.LostFocus += (_, _) => Apply();
                var row = new WrapPanel();
                row.Children.Add(new TextBlock { Text = "B", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(floor);
                row.Children.Add(new TextBlock { Text = "  x ", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(x);
                row.Children.Add(new TextBlock { Text = "  y ", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(y);
                _cellPanel.Children.Add(Ui.Row(T("行き先", "Destination"), row, LabelWidth));
                _cellPanel.Children.Add(Ui.Row("", Ui.Note(kind == "chute" ? T("シュートは下の階へ落ちる(B の番号で書く)。", "A chute drops to a lower floor (enter the B number).")
                    : T("階が空なら同じ階。", "An empty floor means the same floor.")), LabelWidth));
                break;
            }
            case "other":
                _cellPanel.Children.Add(Ui.Row(T("印", "Mark"), Ui.Text(c.Mark, v => { c.Mark = v.Trim().Length == 0 ? null : v.Trim(); Edited(); }), LabelWidth));
                break;
        }
    }

    /// <summary>階の項目(名前・端のつながり)。</summary>
    private void ShowFloorProps()
    {
        _floorPanel.Children.Clear();
        if (Current is not { } f) return;
        // 1行目: 名前(日本語・English)と大きさ。2行目: 端のつながりと、着く場所・部屋の数
        var row1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = Ui.Dim };
        row1.Children.Add(Label($"B{Floor + 1}  " + T("名前", "Name")));
        var ja = Ui.Text(f.NameJa, v => { f.NameJa = v.Length == 0 ? null : v; _context.MarkEdited(); });
        ja.Width = 150;
        ja.ToolTip = T("日本語の名前(空なら物語のタブの名前)", "Japanese name (empty: the name on the Story tab)");
        var en = Ui.Text(f.NameEn, v => { f.NameEn = v.Length == 0 ? null : v; _context.MarkEdited(); });
        en.Width = 150;
        en.Margin = new Thickness(4, 0, 12, 0);
        en.ToolTip = T("English の名前", "English name");
        row1.Children.Add(ja);
        row1.Children.Add(en);
        row1.Children.Add(Label(T("大きさ", "Size")));
        var w = new TextBox { Text = f.Width.ToString(), Width = 40 };
        var h = new TextBox { Text = f.Height.ToString(), Width = 40 };
        row1.Children.Add(w);
        row1.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(h);
        row1.Children.Add(Ui.Button(T("変える", "Resize"), () =>
        {
            if (!int.TryParse(w.Text, out int nw) || !int.TryParse(h.Text, out int nh) || nw < 1 || nh < 1 || nw > 64 || nh > 64)
            {
                MessageBox.Show(T("幅と高さは 1〜64 にしてください", "Width and height must be 1-64"));
                return;
            }
            if ((nw < f.Width || nh < f.Height)
                && MessageBox.Show(T("右と下のはみ出すマスは、仕掛けや印ごと消えます。変えますか？", "Cells cut off on the right and bottom are removed along with their features and marks. Resize?"), T("迷宮", "Maze"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _context.Pack!.Floors[Floor] = MazeEdit.Resize(f, nw, nh);
            _context.MarkEdited();
            ShowFloor();
        }, T("左上をそろえて、右と下を足すか削る。足したところは壁で囲まれたマス", "Keeps the top-left fixed and adds or trims the right and bottom. Added cells are walled in")));
        var wx = new CheckBox { Content = T("東西の端がつながる", "East-west wraps"), IsChecked = f.WrapX, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        wx.Click += (_, _) => { f.WrapX = wx.IsChecked == true; Edited(); };
        var wy = new CheckBox { Content = T("南北の端がつながる", "North-south wraps"), IsChecked = f.WrapY, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        wy.Click += (_, _) => { f.WrapY = wy.IsChecked == true; Edited(); };
        row2.Children.Add(wx);
        row2.Children.Add(wy);
        row2.Children.Add(Label(T($"着く場所 ({f.Start.X}, {f.Start.Y}) {DirName(f.StartFacing)}向き、部屋 {f.Rooms.Count} 個", $"Arrival ({f.Start.X}, {f.Start.Y}) facing {DirName(f.StartFacing)}, {f.Rooms.Count} room(s)")));
        _floorPanel.Children.Add(row1);
        _floorPanel.Children.Add(row2);
    }

    private void AddFloor(bool blank)
    {
        if (_context.Pack is not { } p) return;
        var like = Current;
        int w = like?.Width ?? 16, h = like?.Height ?? 16;
        MazeFloor floor;
        if (blank) floor = MazeEdit.Blank($"b{p.Floors.Count + 1}", w, h);
        else
        {
            var request = new GenerationRequest(Seed(), p.Floors.Count + 1, FloorSizeMode.Fixed(w, h));
            floor = new MazeGenerator(MazeGenSettings.Load(_context.BaseContent)).Generate(request).Dungeon.Floors[^1];
        }
        p.Floors.Add(floor);
        Floor = p.Floors.Count - 1;
        _context.MarkEdited();
        ShowFloor();
    }

    private void RemoveFloor()
    {
        if (_context.Pack is not { } p || Current == null || p.Floors.Count <= 1) return;
        if (MessageBox.Show(T($"B{Floor + 1} を消しますか？", $"Delete B{Floor + 1}?"), T("迷宮", "Maze"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Core.Save.ScenarioEdit.RemoveFloor(p, Floor);   // 顔ぶれ・世界の文・名のある敵・行き先・縦穴も一緒に
        Floor = Math.Min(Floor, p.Floors.Count - 1);
        _context.MarkEdited();
        ShowFloor();
    }

    private void MoveFloor(int by)
    {
        if (_context.Pack is not { } p || Current is not { } f) return;
        int to = Floor + by;
        if (to < 0 || to >= p.Floors.Count) return;
        Core.Save.ScenarioEdit.MoveFloor(p, Floor, to);   // 顔ぶれ・世界の文・名のある敵・行き先も一緒に動かす
        Floor = to;
        _context.MarkEdited();
        ShowFloor();
    }

    private void Regenerate()
    {
        if (_context.Pack is not { } p || Current == null) return;
        try
        {
            p.Floors[Floor] = _context.RegenerateFloor(p, Floor, Seed());
            _context.MarkEdited();
            ShowFloor();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(T("作れません: ", "Cannot generate: ") + ex.Message);
        }
    }

    private ulong Seed() => Core.Rng.RngStreams.ParseSeed(_seed.Text) ?? 1;

    private static WrapPanel Stack(params UIElement[] items)
    {
        var s = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var i in items) s.Children.Add(i);
        return s;
    }

    private static ScrollViewer Scrolled(UIElement content, int column)
    {
        var s = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(s, column);
        return s;
    }
}

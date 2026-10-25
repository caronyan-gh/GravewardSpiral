using System.Text;

namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 迷宮1階ぶんのテキスト形式。人が読み書きでき、生成した迷宮の書き出しにも使う。
/// <code>
/// # コメント
/// floor test_b1
/// name.ja 試しの階
/// name.en Trial Floor
/// size 4 3
/// wrap none            (none / x / y / xy)
/// start 0 0 N
/// walls
/// +-+-+-+-+
/// |u  |  d|
/// + +-+D+ +
/// |   S   |
/// +-+-+-+-+
/// zones                (省略できる。1マス1文字: . なし / k 暗闇 / f 霧 / b 両方)
/// ....
/// .kk.
/// ....
/// antimagic            (省略できる。1マス1文字: . なし / x 魔法禁止)
/// ....
/// teleport 1 1 3 0     (テレポーター (1,1) の行き先 (3,0)。別の階なら最後に B3 のように書く)
/// chute 2 1 4 4        (シュート (2,1) の行き先。1つ下の階の (4,4))
/// entrance 0 1 W       (はめ込む部屋だけ: 外へ開ける入口の辺)
/// mark 1 1 key         (マスの印。key / boss:id / elevator:組 / message:キー)
/// room 2 1 3 2         (部屋。左上 (2,1)、幅 3、高さ 2。入ると、そこにいる敵と戦うことがある)
/// </code>
/// walls は (2×幅+1) 文字 × (2×高さ+1) 行。奇数の行・列の交わりがマスで、その間が辺になる。
/// 辺の文字: 横の辺は '-' 壁 / ' ' 通路 / 'D' 扉 / 'S' 隠し扉 / 'L' 錠のある扉 / '^' 北へだけ通れる / 'v' 南へだけ通れる。
/// 縦の辺は '|' 壁 / ' ' 通路 / 'D' / 'S' / 'L' / '&lt;' 西へだけ / '&gt;' 東へだけ。角の文字は何でもよい(ふつうは '+')。
/// マスの文字: ' ' か '.' ふつう / 'u' 上り階段 / 'd' 下り階段 / 's' 回転床 / 't' テレポーター / 'p' 落とし穴 / 'c' シュート /
/// 'e' エレベーター / '#' 岩盤。
/// 端のループがある向きでは、両端の辺は同じ辺なので、同じ文字にする。
/// </summary>
public static class MazeTextFormat
{
    public static MazeFloor Parse(string text, string sourceName = "map")
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? id = null, nameJa = null, nameEn = null, wrap = "none";
        int width = 0, height = 0;
        GridPos start = default;
        Direction facing = Direction.North;
        List<string>? walls = null, zones = null, antimagic = null;
        List<string>? collecting = null;
        int wallsLine = 0, zonesLine = 0, antimagicLine = 0;
        var warps = new List<(int Line, bool Chute, GridPos From, WarpTarget Target)>();
        var entrances = new List<(GridPos, Direction)>();
        var marks = new List<(int Line, GridPos Cell, string Mark)>();
        var rooms = new List<(int Line, MazeRoom Room)>();

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            int lineNo = i + 1;
            if (collecting != null)
            {
                int expected = collecting == walls ? 2 * height + 1 : height;
                if (collecting.Count < expected)
                {
                    collecting.Add(raw);
                    continue;
                }
                collecting = null;
            }
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string key = parts[0];
            string rest = parts.Length > 1 ? parts[1].Trim() : "";
            switch (key)
            {
                case "floor": id = rest; break;
                case "name.ja": nameJa = rest; break;
                case "name.en": nameEn = rest; break;
                case "wrap": wrap = rest; break;
                case "size":
                {
                    var n = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (n.Length != 2 || !int.TryParse(n[0], out width) || !int.TryParse(n[1], out height) || width < 1 || height < 1)
                        throw Error(sourceName, lineNo, "size は「幅 高さ」で書きます(例: size 20 20)");
                    break;
                }
                case "start":
                {
                    var n = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (n.Length != 3 || !int.TryParse(n[0], out var sx) || !int.TryParse(n[1], out var sy) || n[2].Length != 1)
                        throw Error(sourceName, lineNo, "start は「x y 向き」で書きます(例: start 0 0 N)");
                    start = new GridPos(sx, sy);
                    facing = DirectionExtensions.FromLetter(n[2][0]);
                    break;
                }
                case "walls":
                    if (width == 0) throw Error(sourceName, lineNo, "walls より前に size を書いてください");
                    walls = [];
                    collecting = walls;
                    wallsLine = lineNo + 1;
                    break;
                case "zones":
                    if (width == 0) throw Error(sourceName, lineNo, "zones より前に size を書いてください");
                    zones = [];
                    collecting = zones;
                    zonesLine = lineNo + 1;
                    break;
                case "antimagic":
                    if (width == 0) throw Error(sourceName, lineNo, "antimagic より前に size を書いてください");
                    antimagic = [];
                    collecting = antimagic;
                    antimagicLine = lineNo + 1;
                    break;
                case "entrance":
                {
                    var n = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (n.Length != 3 || !int.TryParse(n[0], out var ex) || !int.TryParse(n[1], out var ey) || n[2].Length != 1)
                        throw Error(sourceName, lineNo, "entrance は「x y 向き」で書きます(例: entrance 0 1 W)");
                    entrances.Add((new GridPos(ex, ey), DirectionExtensions.FromLetter(n[2][0])));
                    break;
                }
                case "mark":
                {
                    var n = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (n.Length != 3 || !int.TryParse(n[0], out var mx) || !int.TryParse(n[1], out var my))
                        throw Error(sourceName, lineNo, "mark は「x y 印」で書きます(例: mark 1 1 key)");
                    marks.Add((lineNo, new GridPos(mx, my), n[2]));
                    break;
                }
                case "teleport":
                case "chute":
                    warps.Add(ParseWarp(sourceName, lineNo, key == "chute", rest));
                    break;
                case "room":
                {
                    var n = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (n.Length != 4 || !n.All(v => int.TryParse(v, out _)))
                        throw Error(sourceName, lineNo, "room は「x y 幅 高さ」で書きます(例: room 2 1 3 2)");
                    var v = n.Select(int.Parse).ToArray();
                    if (v[2] < 1 || v[3] < 1) throw Error(sourceName, lineNo, "room の幅と高さは 1 以上です");
                    rooms.Add((lineNo, new MazeRoom(v[0], v[1], v[2], v[3])));
                    break;
                }
                default:
                    throw Error(sourceName, lineNo, $"'{key}' は読めません");
            }
        }

        if (string.IsNullOrWhiteSpace(id)) throw Error(sourceName, 0, "floor(階の id)がありません");
        if (walls == null) throw Error(sourceName, 0, "walls がありません");
        if (walls.Count != 2 * height + 1) throw Error(sourceName, wallsLine, $"walls は {2 * height + 1} 行要ります(今は {walls.Count} 行)");

        var floor = new MazeFloor(id, width, height)
        {
            NameJa = nameJa,
            NameEn = nameEn,
            WrapX = wrap.Contains('x'),
            WrapY = wrap.Contains('y'),
            Start = start,
            StartFacing = facing,
        };
        if (wrap is not ("none" or "x" or "y" or "xy")) throw Error(sourceName, 0, "wrap は none / x / y / xy のどれかです");
        foreach (var (line, room) in rooms)
        {
            if (room.X < 0 || room.Y < 0 || room.X + room.W > width || room.Y + room.H > height)
                throw Error(sourceName, line, "room が迷宮の外にはみ出しています");
            floor.Rooms.Add(room);
        }

        int rowWidth = 2 * width + 1;
        for (int r = 0; r < walls.Count; r++)
        {
            walls[r] = walls[r].PadRight(rowWidth);
            if (walls[r].Length > rowWidth && walls[r][rowWidth..].Trim().Length > 0)
                throw Error(sourceName, wallsLine + r, $"walls の行が長すぎます({rowWidth} 文字まで)");
        }

        char At(int row, int col) => walls[row][col];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int row = 2 * y + 1, col = 2 * x + 1;
            var cell = floor[x, y];
            cell.Feature = At(row, col) switch
            {
                ' ' or '.' => CellFeature.None,
                'u' => CellFeature.StairsUp,
                'd' => CellFeature.StairsDown,
                's' => CellFeature.Spinner,
                't' => CellFeature.Teleporter,
                'p' => CellFeature.Pit,
                'c' => CellFeature.Chute,
                'e' => CellFeature.Elevator,
                '#' => CellFeature.Solid,
                var c => throw Error(sourceName, wallsLine + row, $"マス ({x},{y}) の文字 '{c}' は読めません"),
            };
        }

        // 横の辺(各マスの北と南)
        for (int y = 0; y <= height; y++)
        for (int x = 0; x < width; x++)
        {
            char c = At(2 * y, 2 * x + 1);
            var (northSide, southSide) = c switch
            {
                '-' => (EdgeType.Wall, EdgeType.Wall),
                ' ' => (EdgeType.Open, EdgeType.Open),
                'D' => (EdgeType.Door, EdgeType.Door),
                'S' => (EdgeType.SecretDoor, EdgeType.SecretDoor),
                'L' => (EdgeType.Locked, EdgeType.Locked),
                '^' => (EdgeType.OneWayIn, EdgeType.OneWayOut),   // 南のマスから北へ出られる
                'v' => (EdgeType.OneWayOut, EdgeType.OneWayIn),
                _ => throw Error(sourceName, wallsLine + 2 * y, $"横の辺 ({x},{y}) の文字 '{c}' は読めません"),
            };
            // 辺の北側のマスにとっては南の辺、南側のマスにとっては北の辺
            if (y > 0) floor[x, y - 1][Direction.South] = northSide;
            if (y < height) floor[x, y][Direction.North] = southSide;
            if (floor.WrapY && y == 0) floor[x, height - 1][Direction.South] = northSide;
            if (floor.WrapY && y == height && At(0, 2 * x + 1) != c)
                throw Error(sourceName, wallsLine + 2 * y, $"南北の端がつながっているので、({x}) の上下の端の辺を同じ文字にしてください");
        }

        // 縦の辺(各マスの西と東)
        for (int y = 0; y < height; y++)
        for (int x = 0; x <= width; x++)
        {
            char c = At(2 * y + 1, 2 * x);
            var (westSide, eastSide) = c switch
            {
                '|' => (EdgeType.Wall, EdgeType.Wall),
                ' ' => (EdgeType.Open, EdgeType.Open),
                'D' => (EdgeType.Door, EdgeType.Door),
                'S' => (EdgeType.SecretDoor, EdgeType.SecretDoor),
                'L' => (EdgeType.Locked, EdgeType.Locked),
                '<' => (EdgeType.OneWayIn, EdgeType.OneWayOut),   // 東のマスから西へ出られる
                '>' => (EdgeType.OneWayOut, EdgeType.OneWayIn),
                _ => throw Error(sourceName, wallsLine + 2 * y + 1, $"縦の辺 ({x},{y}) の文字 '{c}' は読めません"),
            };
            if (x > 0) floor[x - 1, y][Direction.East] = westSide;
            if (x < width) floor[x, y][Direction.West] = eastSide;
            if (floor.WrapX && x == 0) floor[width - 1, y][Direction.East] = westSide;
            if (floor.WrapX && x == width && At(2 * y + 1, 0) != c)
                throw Error(sourceName, wallsLine + 2 * y + 1, $"東西の端がつながっているので、({y}) の左右の端の辺を同じ文字にしてください");
        }

        if (zones != null)
        {
            if (zones.Count != height) throw Error(sourceName, zonesLine, $"zones は {height} 行要ります");
            for (int y = 0; y < height; y++)
            {
                string row = zones[y].PadRight(width, '.');
                for (int x = 0; x < width; x++)
                {
                    floor[x, y].Zone = row[x] switch
                    {
                        '.' or ' ' => Zone.None,
                        'k' => Zone.Dark,
                        'f' => Zone.Fog,
                        'b' => Zone.Dark | Zone.Fog,
                        var c => throw Error(sourceName, zonesLine + y, $"zones の ({x},{y}) の文字 '{c}' は読めません"),
                    };
                }
            }
        }

        if (antimagic != null)
        {
            if (antimagic.Count != height) throw Error(sourceName, antimagicLine, $"antimagic は {height} 行要ります");
            for (int y = 0; y < height; y++)
            {
                string row = antimagic[y].PadRight(width, '.');
                for (int x = 0; x < width; x++)
                {
                    if (row[x] is 'x' or 'X') floor[x, y].Zone |= Zone.AntiMagic;
                    else if (row[x] is not ('.' or ' ')) throw Error(sourceName, antimagicLine + y, $"antimagic の ({x},{y}) の文字 '{row[x]}' は読めません");
                }
            }
        }

        floor.Entrances.AddRange(entrances);
        foreach (var (line, cell, mark) in marks)
        {
            if (!floor.InBounds(cell.X, cell.Y)) throw Error(sourceName, line, $"({cell.X},{cell.Y}) は階の外です");
            floor[cell].Mark = mark;
        }
        foreach (var (line, chute, from, target) in warps)
        {
            if (!floor.InBounds(from.X, from.Y)) throw Error(sourceName, line, $"({from.X},{from.Y}) は階の外です");
            var want = chute ? CellFeature.Chute : CellFeature.Teleporter;
            if (floor[from].Feature != want)
                throw Error(sourceName, line, $"({from.X},{from.Y}) のマスの文字を '{(chute ? 'c' : 't')}' にしてください");
            floor[from].Target = target;
        }

        return floor;
    }

    private static (int, bool, GridPos, WarpTarget) ParseWarp(string sourceName, int lineNo, bool chute, string rest)
    {
        var n = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nums = new int[4];
        bool ok = n.Length is 4 or 5 && Enumerable.Range(0, 4).All(i => int.TryParse(n[i], out nums[i]));
        int? targetFloor = null;
        if (ok && n.Length == 5)
        {
            ok = !chute && n[4].Length > 1 && n[4][0] is 'B' or 'b' && int.TryParse(n[4][1..], out var b) && b >= 1;
            if (ok) targetFloor = int.Parse(n[4][1..]) - 1;
        }
        if (!ok)
            throw Error(sourceName, lineNo, chute ? "chute は「x y 行き先x 行き先y」で書きます" : "teleport は「x y 行き先x 行き先y [B階]」で書きます");
        return (lineNo, chute, new GridPos(nums[0], nums[1]), new WarpTarget(targetFloor, new GridPos(nums[2], nums[3])));
    }

    public static string Write(MazeFloor floor)
    {
        var sb = new StringBuilder();
        sb.Append("floor ").Append(floor.Id).Append('\n');
        if (floor.NameJa != null) sb.Append("name.ja ").Append(floor.NameJa).Append('\n');
        if (floor.NameEn != null) sb.Append("name.en ").Append(floor.NameEn).Append('\n');
        sb.Append($"size {floor.Width} {floor.Height}\n");
        string wrap = (floor.WrapX ? "x" : "") + (floor.WrapY ? "y" : "");
        sb.Append("wrap ").Append(wrap.Length == 0 ? "none" : wrap).Append('\n');
        sb.Append($"start {floor.Start.X} {floor.Start.Y} {floor.StartFacing.Letter()}\n");
        sb.Append("walls\n");
        for (int r = 0; r <= 2 * floor.Height; r++)
        {
            for (int c = 0; c <= 2 * floor.Width; c++)
            {
                bool evenRow = r % 2 == 0, evenCol = c % 2 == 0;
                if (evenRow && evenCol) sb.Append('+');
                else if (!evenRow && !evenCol)
                {
                    sb.Append(floor[c / 2, r / 2].Feature switch
                    {
                        CellFeature.StairsUp => 'u',
                        CellFeature.StairsDown => 'd',
                        CellFeature.Spinner => 's',
                        CellFeature.Teleporter => 't',
                        CellFeature.Pit => 'p',
                        CellFeature.Chute => 'c',
                        CellFeature.Elevator => 'e',
                        CellFeature.Solid => '#',
                        _ => ' ',
                    });
                }
                else if (evenRow)
                {
                    int x = c / 2, y = r / 2;
                    // 辺の南側のマスの北の辺で決める(一番下の行だけは北側のマスの南の辺)
                    sb.Append(y < floor.Height ? HorizontalChar(floor[x, y][Direction.North], fromSouth: true)
                                               : HorizontalChar(floor[x, y - 1][Direction.South], fromSouth: false));
                }
                else
                {
                    int x = c / 2, y = r / 2;
                    sb.Append(x < floor.Width ? VerticalChar(floor[x, y][Direction.West], fromEast: true)
                                              : VerticalChar(floor[x - 1, y][Direction.East], fromEast: false));
                }
            }
            sb.Append('\n');
        }
        if (floor.AllCells().Any(p => (floor[p].Zone & (Zone.Dark | Zone.Fog)) != Zone.None))
        {
            sb.Append("zones\n");
            for (int y = 0; y < floor.Height; y++)
            {
                for (int x = 0; x < floor.Width; x++)
                {
                    sb.Append((floor[x, y].Zone & (Zone.Dark | Zone.Fog)) switch
                    {
                        Zone.Dark => 'k',
                        Zone.Fog => 'f',
                        Zone.Dark | Zone.Fog => 'b',
                        _ => '.',
                    });
                }
                sb.Append('\n');
            }
        }
        if (floor.AllCells().Any(p => floor[p].Zone.HasFlag(Zone.AntiMagic)))
        {
            sb.Append("antimagic\n");
            for (int y = 0; y < floor.Height; y++)
            {
                for (int x = 0; x < floor.Width; x++) sb.Append(floor[x, y].Zone.HasFlag(Zone.AntiMagic) ? 'x' : '.');
                sb.Append('\n');
            }
        }
        foreach (var p in floor.AllCells())
        {
            var c = floor[p];
            if (c.Target is not { } t) continue;
            sb.Append(c.Feature == CellFeature.Chute ? "chute" : "teleport").Append($" {p.X} {p.Y} {t.Cell.X} {t.Cell.Y}");
            if (c.Feature == CellFeature.Teleporter && t.Floor is int tf) sb.Append($" B{tf + 1}");
            sb.Append('\n');
        }
        foreach (var (cell, dir) in floor.Entrances) sb.Append($"entrance {cell.X} {cell.Y} {dir.Letter()}\n");
        foreach (var p in floor.AllCells().Where(p => floor[p].Mark != null)) sb.Append($"mark {p.X} {p.Y} {floor[p].Mark}\n");
        foreach (var r in floor.Rooms) sb.Append($"room {r.X} {r.Y} {r.W} {r.H}\n");
        return sb.ToString();
    }

    private static char HorizontalChar(EdgeType edge, bool fromSouth) => edge switch
    {
        EdgeType.Wall => '-',
        EdgeType.Open => ' ',
        EdgeType.Door => 'D',
        EdgeType.SecretDoor => 'S',
        EdgeType.Locked => 'L',
        EdgeType.OneWayOut => fromSouth ? '^' : 'v',
        EdgeType.OneWayIn => fromSouth ? 'v' : '^',
        _ => '-',
    };

    private static char VerticalChar(EdgeType edge, bool fromEast) => edge switch
    {
        EdgeType.Wall => '|',
        EdgeType.Open => ' ',
        EdgeType.Door => 'D',
        EdgeType.SecretDoor => 'S',
        EdgeType.Locked => 'L',
        EdgeType.OneWayOut => fromEast ? '<' : '>',
        EdgeType.OneWayIn => fromEast ? '>' : '<',
        _ => '|',
    };

    private static FormatException Error(string source, int line, string message) =>
        new(line > 0 ? $"{source}:{line}: {message}" : $"{source}: {message}");
}

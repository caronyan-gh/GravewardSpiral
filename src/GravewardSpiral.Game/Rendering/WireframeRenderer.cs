using GravewardSpiral.Core.Maze;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// 迷宮の線画(初代風のワイヤーフレーム)。MazeView の見えるマスを、奥から手前へ、外側から内側へ描く。
/// 面は背景の色で塗ってから枠を描くので、手前の壁が奥の線を隠す。
/// </summary>
public static class WireframeRenderer
{
    public static readonly Color Background = new(6, 8, 12);
    /// <summary>モノクロの線画(設定)。初代風に、壁も扉も階段も白い線で描く。</summary>
    public static bool Monochrome { get; set; }

    private static readonly Color MonoLine = new(225, 225, 225);
    public static Color WallLine => Monochrome ? MonoLine : new Color(120, 190, 255);
    public static Color DoorLine => Monochrome ? MonoLine : new Color(230, 180, 90);
    public static Color StairsLine => Monochrome ? MonoLine : new Color(150, 230, 160);
    public static readonly Color FogColor = new(150, 162, 175);

    /// <summary>霧の中を1マス進むごとに、どれだけ霧の色に溶けるか(1 で見えなくなる)。</summary>
    private const float FogPerCell = 0.42f;

    /// <summary>一番手前(今のマスの端)の奥行き。ここで壁が表示の端に届く。</summary>
    private const float NearZ = 0.36f;

    /// <summary>壁の縦の大きさ(横に対する割合)。</summary>
    private const float HeightRatio = 0.78f;

    private const float LineWidth = 1.6f;

    /// <param name="time">霧の帯を流すための時間(秒)。</param>
    public static void Draw(PrimitiveBatch pb, ExplorationState state, RectangleF area, double time)
    {
        pb.FillQuad(new Vector2(area.X, area.Y), new Vector2(area.X + area.Width, area.Y),
            new Vector2(area.X + area.Width, area.Y + area.Height), new Vector2(area.X, area.Y + area.Height), Background);
        if (state.InDark) return;

        var cam = new Camera(area);
        var facing = state.Position.Facing;
        var cells = MazeView.Compute(state)
            .OrderByDescending(c => c.Depth)
            .ThenByDescending(c => Math.Abs(c.Lateral));
        var floor = state.Floor;

        // 霧: 霧の中にいるときは足元から、外から見ているときは最初の霧のマスから、奥へ行くほど霧の色に溶ける
        bool inFog = state.Cell.Zone.HasFlag(Zone.Fog);
        var view = cells.ToList();
        int fogStart = inFog ? 0 : view.Where(c => floor[c.Cell].Zone.HasFlag(Zone.Fog)).Select(c => c.Depth).DefaultIfEmpty(int.MaxValue).Min();
        float Mist(int depth) => depth < fogStart ? 0f : (depth - fogStart + (inFog ? 0.6f : 1f)) * FogPerCell;

        foreach (var cell in view)
        {
            if (cell.Depth > 0 && state.IsDark(state.Position.Floor, cell.Cell)) continue;   // 暗闇のマスは何も見えない(明かりがあれば見える)
            float mist = Mist(cell.Depth);
            if (mist >= 1f) continue;
            var style = new Style(mist);
            float zNear = MathF.Max(NearZ, cell.Depth - 0.5f);
            float zFar = cell.Depth + 0.5f;
            float xL = cell.Lateral - 0.5f, xR = cell.Lateral + 0.5f;

            // マスの中では、壁を全部描いてから床の階段を描く(階段はマスの内側にあるので、そのマスの壁より手前にある)
            foreach (var (dir, edge) in cell.VisibleEdges)
            {
                if (MazeView.SeeThrough(edge)) continue;
                bool door = MazeView.LooksLikeDoor(edge);
                if (dir == facing) FrontWall(pb, cam, style, xL, xR, zFar, door);
                else if (dir == facing.Left()) SideWall(pb, cam, style, xL, zNear, zFar, door);
                else if (dir == facing.Right()) SideWall(pb, cam, style, xR, zNear, zFar, door);
            }

            // 足元の階段は見えないので描かない(着いたときにメッセージで知らせる)
            if (cell.Depth > 0) DrawFeature(pb, cam, style, floor[cell.Cell].Feature, cell.Lateral, zNear, zFar);
        }

        if (inFog || fogStart != int.MaxValue) DrawHaze(pb, area, time, inFog);
    }

    /// <summary>霧の色にどれだけ溶けるかで決まる、線と面の色。</summary>
    private readonly struct Style(float mist)
    {
        public Color Fill { get; } = Color.Lerp(Background, new Color(34, 38, 44), mist);
        public Color Wall { get; } = Color.Lerp(WallLine, FogColor, mist) * (1f - mist * 0.55f);
        public Color Door { get; } = Color.Lerp(DoorLine, FogColor, mist) * (1f - mist * 0.55f);
        public Color Stairs { get; } = Color.Lerp(StairsLine, FogColor, mist) * (1f - mist * 0.55f);
    }

    /// <summary>ゆっくり流れる霧の帯。霧の中にいるときは、画面全体もうっすら白く濁らせる。</summary>
    private static void DrawHaze(PrimitiveBatch pb, RectangleF area, double time, bool inFog)
    {
        var tl = new Vector2(area.X, area.Y);
        var tr = new Vector2(area.X + area.Width, area.Y);
        var br = new Vector2(area.X + area.Width, area.Y + area.Height);
        var bl = new Vector2(area.X, area.Y + area.Height);
        if (inFog) pb.FillQuad(tl, tr, br, bl, FogColor * 0.10f);

        float strength = inFog ? 0.13f : 0.07f;
        const int bands = 5;
        for (int i = 0; i < bands; i++)
        {
            double speed = 0.018 + 0.011 * (i % 3);
            float t = (float)((i / (double)bands + time * speed) % 1.0);
            float h = area.Height * (0.34f + 0.08f * (i % 2));
            float y = area.Y - h + t * (area.Height + h);
            float drift = MathF.Sin((float)time * 0.4f + i) * 30f;
            // 左右に揺れても端が空かないよう、表示より広く描く(外は切り取られる)
            float left = area.X - 60f + drift, right = area.X + area.Width + 60f + drift;
            var clear = Color.Transparent;
            var mistColor = FogColor * strength;
            var a = new Vector2(left, y);
            var b = new Vector2(right, y);
            var m1 = new Vector2(right, y + h / 2);
            var m0 = new Vector2(left, y + h / 2);
            var c = new Vector2(right, y + h);
            var d = new Vector2(left, y + h);
            pb.GradientQuad(a, b, m1, m0, clear, mistColor);
            pb.GradientQuad(m0, m1, c, d, mistColor, clear);
        }
    }

    private static void FrontWall(PrimitiveBatch pb, Camera cam, Style style, float xL, float xR, float z, bool door)
    {
        Vector2 tl = cam.Top(xL, z), tr = cam.Top(xR, z), br = cam.Bottom(xR, z), bl = cam.Bottom(xL, z);
        pb.FillQuad(tl, tr, br, bl, style.Fill);
        pb.Outline(tl, tr, br, bl, style.Wall, LineWidth);
        if (!door) return;
        // 扉: 壁の中に、床から立つ枠
        float w = xR - xL;
        float dl = xL + w * 0.22f, dr = xR - w * 0.22f;
        Vector2 dtl = cam.At(dl, z, 0.62f), dtr = cam.At(dr, z, 0.62f), dbr = cam.Bottom(dr, z), dbl = cam.Bottom(dl, z);
        pb.Line(dbl, dtl, style.Door, LineWidth);
        pb.Line(dtl, dtr, style.Door, LineWidth);
        pb.Line(dtr, dbr, style.Door, LineWidth);
    }

    private static void SideWall(PrimitiveBatch pb, Camera cam, Style style, float x, float zNear, float zFar, bool door)
    {
        Vector2 nt = cam.Top(x, zNear), ft = cam.Top(x, zFar), fb = cam.Bottom(x, zFar), nb = cam.Bottom(x, zNear);
        pb.FillQuad(nt, ft, fb, nb, style.Fill);
        pb.Outline(nt, ft, fb, nb, style.Wall, LineWidth);
        if (!door) return;
        float span = zFar - zNear;
        float z1 = zNear + span * 0.25f, z2 = zFar - span * 0.25f;
        Vector2 a = cam.Bottom(x, z1), b = cam.At(x, z1, 0.62f), c = cam.At(x, z2, 0.62f), d = cam.Bottom(x, z2);
        pb.Line(a, b, style.Door, LineWidth);
        pb.Line(b, c, style.Door, LineWidth);
        pb.Line(c, d, style.Door, LineWidth);
    }

    /// <summary>階段の印。下りは床の穴、上りは床から上がる段々。</summary>
    private static void DrawFeature(PrimitiveBatch pb, Camera cam, Style style, CellFeature feature, int lateral, float zNear, float zFar)
    {
        if (feature == CellFeature.None) return;
        float z1 = MathF.Max(zNear, zFar - 0.75f), z2 = zFar - 0.2f;
        float x1 = lateral - 0.28f, x2 = lateral + 0.28f;
        if (feature == CellFeature.StairsDown)
        {
            Vector2 a = cam.Bottom(x1, z1), b = cam.Bottom(x2, z1), c = cam.Bottom(x2, z2), d = cam.Bottom(x1, z2);
            pb.Outline(a, b, c, d, style.Stairs, LineWidth);
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                float z = z1 + (z2 - z1) * t;
                pb.Line(cam.Bottom(x1 + 0.06f * i, z), cam.Bottom(x2 - 0.06f * i, z), style.Stairs, LineWidth * 0.8f);
            }
        }
        else
        {
            // 上り: 床から上がる低い段々(天井まで描くと、奥の壁より背が高くはみ出して見えるため)。
            // 段は重ならない柱(1段ぶんの奥行きで、床から段の高さまで)に分け、奥の柱から順に面を背景の色で塗る(後ろの線を隠す)。
            // 線は全部塗ったあとに、階段の輪郭(段の角、踏み面の縁、床の線、一番奥の縦の線)だけを描く。
            // 柱の境目の縦の線は描かない(段の横の面に、床まで届く線が出ないように)
            const int steps = 3;
            const float rise = 0.11f;   // 1段の高さ(壁の高さに対する割合)
            Vector2 P(float x, float zz, float hh) => cam.At(x, zz, hh);
            float Z(int i) => z1 + (z2 - z1) * i / steps;
            float hiddenX = lateral < 0 ? x1 : x2, shownX = lateral < 0 ? x2 : x1;
            for (int i = steps - 1; i >= 0; i--)
            {
                float z = Z(i), zNext = Z(i + 1), h = rise * (i + 1);
                // 見えない横の面 → 見える横の面 → 上の面 → 前の面 の順に塗る
                pb.FillQuad(P(hiddenX, z, 0), P(hiddenX, zNext, 0), P(hiddenX, zNext, h), P(hiddenX, z, h), style.Fill);
                pb.FillQuad(P(shownX, z, 0), P(shownX, zNext, 0), P(shownX, zNext, h), P(shownX, z, h), style.Fill);
                pb.FillQuad(P(x1, z, h), P(x2, z, h), P(x2, zNext, h), P(x1, zNext, h), style.Fill);
                pb.FillQuad(P(x1, z, 0), P(x2, z, 0), P(x2, z, h), P(x1, z, h), style.Fill);
            }
            for (int i = 0; i < steps; i++)
            {
                float z = Z(i), zNext = Z(i + 1), h = rise * (i + 1), below = rise * i;
                // 段の前の面のうち、手前の段より上に出ている部分(蹴上げ)と、踏み面の縁
                pb.Line(P(x1, z, below), P(x1, z, h), style.Stairs, LineWidth);
                pb.Line(P(x2, z, below), P(x2, z, h), style.Stairs, LineWidth);
                pb.Line(P(x1, z, h), P(x2, z, h), style.Stairs, LineWidth);
                // くぼんだ角: 蹴上げの下の縁(1つ手前の段の踏み面の奥の縁)。一番手前の段は床の線で描く
                if (i > 0) pb.Line(P(x1, z, below), P(x2, z, below), style.Stairs, LineWidth * 0.8f);
                pb.Line(P(x1, z, h), P(x1, zNext, h), style.Stairs, LineWidth * 0.8f);
                pb.Line(P(x2, z, h), P(x2, zNext, h), style.Stairs, LineWidth * 0.8f);
            }
            float top = rise * steps;
            pb.Line(P(x1, z1, 0), P(x2, z1, 0), style.Stairs, LineWidth);                      // 一番手前の床の線
            pb.Line(P(x1, z2, top), P(x2, z2, top), style.Stairs, LineWidth * 0.8f);           // 一番奥の上の縁
            if (lateral != 0)
            {
                pb.Line(P(shownX, z1, 0), P(shownX, z2, 0), style.Stairs, LineWidth * 0.8f);   // 横の面の床の線
                pb.Line(P(shownX, z2, 0), P(shownX, z2, top), style.Stairs, LineWidth * 0.8f); // 横の面の一番奥の縦の線
            }
        }
    }

    /// <summary>見る人から横 x(マスの幅を 1 とする)、奥行き z の点を、表示の上の位置にする。</summary>
    private readonly struct Camera(RectangleF area)
    {
        private readonly float _cx = area.X + area.Width / 2f;
        private readonly float _cy = area.Y + area.Height / 2f;
        private readonly float _k = area.Width / 2f * NearZ;

        private float HalfW(float z) => _k / z;

        public Vector2 Top(float x, float z) => new(_cx + x * 2f * HalfW(z), _cy - HalfW(z) * HeightRatio);
        public Vector2 Bottom(float x, float z) => new(_cx + x * 2f * HalfW(z), _cy + HalfW(z) * HeightRatio);

        /// <summary>壁の高さの割合 h(0 が床、1 が天井)の点。</summary>
        public Vector2 At(float x, float z, float h)
        {
            var b = Bottom(x, z);
            var t = Top(x, z);
            return Vector2.Lerp(b, t, h);
        }
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>パーティの一覧(町と迷宮の下に出す)。左に前列3人、右に後列3人。名前・クラスとレベル・AC・HP。</summary>
public static class PartyPanel
{
    /// <param name="shown">見せる HP と状態(戦闘のログの途中の値)。null ならキャラクターの今の値。</param>
    /// <param name="fKeys">番号を F1〜F6 にする(そのキーで仲間を切り替えられる画面)。</param>
    /// <param name="usable">その画面を使える人か(使えない人は名前をグレーにする。呪文の準備で術者でない人など)。</param>
    public static void Draw(Draw2D d, GameServices services, RectangleF area, int? highlight = null,
        IReadOnlyList<(string Id, int Hp, LifeState State, string? Shape, int Ac)>? shown = null, bool fKeys = false, Func<Character, bool>? usable = null)
    {
        Theme.Panel(d, area);
        var session = services.Session;
        if (session == null) return;
        var members = session.Roster.PartyMembers.ToList();
        var s = services.Strings;
        if (members.Count == 0)
        {
            d.Text(s["party.empty"], new Vector2(area.X + area.Width / 2, area.Y + area.Height / 2 - 12), 20, Theme.Dim, TextAlign.Center);
            return;
        }
        float colW = area.Width / 2;
        float rowH = (area.Height - 12) / Roster.FrontRow;
        for (int i = 0; i < members.Count; i++)
        {
            var c = members[i];
            int col = i < Roster.FrontRow ? 0 : 1;
            int row = i % Roster.FrontRow;
            float x = area.X + 12 + col * colW, y = area.Y + 6 + row * rowH;
            if (highlight == i) d.FillRect(RowRect(area, i), Theme.SelectedFill);
            float size = Math.Min(19f, rowH - 8);
            var cls = services.Data.Classes.TryGetValue(c.ClassId, out var cd) ? services.Name(cd.Name) : c.ClassId;
            string level = $"Lv{c.Level}";
            int hpNow = c.Hp, hpMax = c.MaxHp, ac = c.ArmorClass(services.Data);
            var state = c.State;
            if (shown?.FirstOrDefault(x => x.Id == c.Id) is { Id: not null } snap)
            {
                (hpNow, state, ac) = (snap.Hp, snap.State, snap.Ac);   // 戦闘中は、そのときの AC(呪文などの分を含む)
                // ワイルドシェイプ中は、クラスのところに獣の名前、HP は獣のもの
                if (snap.Shape != null && services.Data.Monsters.TryGetValue(snap.Shape, out var beast))
                {
                    cls = services.Name(beast.Name);
                    level = "";
                    hpMax = beast.HpAverage;
                }
            }
            var color = state == LifeState.Alive ? (hpNow <= hpMax / 4 ? Theme.Warn : Theme.Text) : Theme.Disabled;
            // 欄の幅に収まらない文字は縮める(戦闘の画面は幅が狭い)
            float w = colW - 24;
            Fit(d, $"{(fKeys ? "F" : "")}{i + 1} {c.Name}", new Vector2(x, y + 2), w * 0.33f, size, usable == null || usable(c) ? color : Theme.Disabled);
            Fit(d, $"{cls} {level}".TrimEnd(), new Vector2(x + w * 0.34f, y + 2), w * 0.33f, size, level.Length == 0 ? Theme.Good : Theme.Dim);
            Fit(d, $"AC {ac}", new Vector2(x + w * 0.69f, y + 2), w * 0.14f, size, Theme.Dim);
            string hp = state == LifeState.Alive ? $"{hpNow}/{hpMax}" : s[$"state.{state.ToString().ToLowerInvariant()}"];
            // 状態のアイコン(毒など。死亡・灰も)。アイコンが無ければ、状態の名前を HP の前に出す
            string? condition = state == LifeState.Alive && c.Lasting.Count > 0 ? c.Lasting[0].Condition.ToString().ToLowerInvariant() : null;
            var icon = services.ConditionIcon(condition ?? state.ToString().ToLowerInvariant());
            if (condition != null)
            {
                if (icon == null) hp = $"{s["condition." + condition]} {hp}";
                color = Theme.Warn;
            }
            d.Text(hp, new Vector2(x + colW - 24, y + 2), size, color, TextAlign.Right);
            if (icon != null && (condition != null || state != LifeState.Alive))
            {
                float iw = size + 4;
                d.Image(icon, new RectangleF(x + colW - 24 - d.Fonts.Measure(hp, size).X - iw - 4, y, iw, iw), Color.White);
            }
        }
        d.Line(new Vector2(area.X + colW, area.Y + 8), new Vector2(area.X + colW, area.Y + area.Height - 8), Theme.Frame, 1f);
        d.Text(s["party.front"], new Vector2(area.X + 12, area.Y - 20), 14, Theme.Dim);
        d.Text(s["party.back"], new Vector2(area.X + colW + 12, area.Y - 20), 14, Theme.Dim);
    }

    /// <summary>i 番目の人の行(左に前列、右に後列)。</summary>
    public static RectangleF RowRect(RectangleF area, int i)
    {
        float colW = area.Width / 2;
        float rowH = (area.Height - 12) / Roster.FrontRow;
        int col = i < Roster.FrontRow ? 0 : 1;
        return new RectangleF(area.X + 6 + col * colW, area.Y + 6 + i % Roster.FrontRow * rowH, colW - 12, rowH - 2);
    }

    /// <summary>マウスの下の人の番号(count 人のうち)。いなければ null。</summary>
    public static int? HitTest(RectangleF area, int count, Vector2 mouse)
    {
        for (int i = 0; i < count; i++)
            if (RowRect(area, i).Contains(mouse)) return i;
        return null;
    }

    private static void Fit(Draw2D d, string text, Vector2 at, float width, float size, Color color)
    {
        float tw = d.Fonts.Measure(text, size).X;
        if (tw > width) size *= width / tw;
        d.Text(text, at, size, color);
    }
}

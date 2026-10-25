using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>パーティの一覧(町と迷宮の下に出す)。左に前列3人、右に後列3人。名前・クラスとレベル・AC・HP。</summary>
public static class PartyPanel
{
    /// <param name="shown">見せる HP と状態(戦闘のログの途中の値)。null ならキャラクターの今の値。</param>
    public static void Draw(Draw2D d, GameServices services, RectangleF area, int? highlight = null,
        IReadOnlyList<(string Id, int Hp, LifeState State)>? shown = null)
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
            if (highlight == i) d.FillRect(new RectangleF(x - 6, y, colW - 12, rowH - 2), Theme.SelectedFill);
            float size = Math.Min(19f, rowH - 8);
            var cls = services.Data.Classes.TryGetValue(c.ClassId, out var cd) ? services.Name(cd.Name) : c.ClassId;
            int hpNow = c.Hp;
            var state = c.State;
            if (shown?.FirstOrDefault(x => x.Id == c.Id) is { Id: not null } snap) (hpNow, state) = (snap.Hp, snap.State);
            var color = state == LifeState.Alive ? (hpNow <= c.MaxHp / 4 ? Theme.Warn : Theme.Text) : Theme.Disabled;
            // 欄の幅に収まらない文字は縮める(戦闘の画面は幅が狭い)
            float w = colW - 24;
            Fit(d, $"{i + 1} {c.Name}", new Vector2(x, y + 2), w * 0.33f, size, color);
            Fit(d, $"{cls} Lv{c.Level}", new Vector2(x + w * 0.34f, y + 2), w * 0.33f, size, Theme.Dim);
            Fit(d, $"AC {c.ArmorClass(services.Data)}", new Vector2(x + w * 0.69f, y + 2), w * 0.14f, size, Theme.Dim);
            string hp = state == LifeState.Alive ? $"{hpNow}/{c.MaxHp}" : s[$"state.{state.ToString().ToLowerInvariant()}"];
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

    private static void Fit(Draw2D d, string text, Vector2 at, float width, float size, Color color)
    {
        float tw = d.Fonts.Measure(text, size).X;
        if (tw > width) size *= width / tw;
        d.Text(text, at, size, color);
    }
}

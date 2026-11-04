using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>パーティの一覧(町と迷宮の下に出す)。左に前列3人、右に後列3人。名前・クラスとレベル・AC・HP。
/// 戦闘中に状態異常にかかっている人は、クラスとレベルの代わりに状態のアイコンを並べる(戦闘の外は、続く状態を HP の前に1つ)。</summary>
public static class PartyPanel
{
    /// <param name="shown">見せる HP と状態(戦闘のログの途中の値)。null ならキャラクターの今の値。</param>
    /// <param name="fKeys">番号を F1〜F6 にする(そのキーで仲間を切り替えられる画面)。</param>
    /// <param name="usable">その画面を使える人か(使えない人は名前をグレーにする。呪文の準備で術者でない人など)。</param>
    public static void Draw(Draw2D d, GameServices services, RectangleF area, int? highlight = null,
        IReadOnlyList<(string Id, int Hp, LifeState State, string? Shape, int Ac, Condition[] Conditions, string? Concentration, bool Raging)>? shown = null, bool fKeys = false, Func<Character, bool>? usable = null)
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
            // 戦闘中にかかっている状態異常(クラスの所にアイコンで並べる)
            IReadOnlyList<Condition> conditions = [];
            string? concentration = null;
            bool raging = false;
            if (shown?.FirstOrDefault(x => x.Id == c.Id) is { Id: not null } snap)
            {
                (hpNow, state, ac, conditions, concentration, raging) = (snap.Hp, snap.State, snap.Ac, snap.Conditions, snap.Concentration, snap.Raging);   // 戦闘中は、そのときの AC(呪文などの分を含む)
                // ワイルドシェイプ中は、クラスのところに獣の名前、HP は獣のもの
                if (snap.Shape != null && services.Data.Monsters.TryGetValue(snap.Shape, out var beast))
                {
                    cls = services.Name(beast.Name);
                    level = "";
                    hpMax = beast.HpAverage;
                }
            }
            var color = state == LifeState.Alive ? Theme.Text : Theme.Disabled;
            // 欄の幅に収まらない文字は縮める(戦闘の画面は幅が狭い)
            float w = colW - 24;
            Fit(d, $"{(fKeys ? "F" : "")}{i + 1} {c.Name}", new Vector2(x, y + 2), w * 0.33f, size, usable == null || usable(c) ? color : Theme.Disabled);
            if (state != LifeState.Alive || (conditions.Count == 0 && concentration == null && !raging))
                Fit(d, $"{cls} {level}".TrimEnd(), new Vector2(x + w * 0.34f, y + 2), w * 0.33f, size, level.Length == 0 ? Theme.Good : Theme.Dim);
            else ConditionIcons(d, services, conditions, new Vector2(x + w * 0.34f, y), w * 0.33f, size, concentration, raging);
            Fit(d, $"AC {ac}", new Vector2(x + w * 0.69f, y + 2), w * 0.14f, size, Theme.Dim);
            string hp = state == LifeState.Alive ? $"{hpNow}/{hpMax}" : s[$"state.{state.ToString().ToLowerInvariant()}"];
            // 状態のアイコン(戦闘の外の毒など。死亡・灰も)は HP の前に出す。アイコンが無ければ、状態の名前を HP の前に出す
            string? condition = shown == null && state == LifeState.Alive && c.Lasting.Count > 0 ? c.Lasting[0].Condition.ToString().ToLowerInvariant() : null;
            var icon = condition == null && state == LifeState.Alive ? null : services.ConditionIcon(condition ?? state.ToString().ToLowerInvariant());
            if (condition != null && icon == null) hp = $"{s["condition." + condition]} {hp}";
            // HP は、満タン・あんぜん・きけん(半分以下)・危険が危ない(4分の1以下)で色を分ける。状態異常はアイコンで分かる
            var hpColor = state == LifeState.Alive ? Theme.Hp(hpNow, hpMax) : Theme.Disabled;
            d.Text(hp, new Vector2(x + colW - 24, y + 2), size, hpColor, TextAlign.Right);
            if (icon != null)
            {
                float iw = size + 4;
                var at = new RectangleF(x + colW - 24 - d.Fonts.Measure(hp, size).X - iw - 4, y, iw, iw);
                d.Image(icon, at, Color.White);
                // アイコンにマウスを載せると、状態の名前と説明
                if (condition != null) HoverCondition(services, at, "condition." + condition);
                else HoverCondition(services, at, $"state.{state.ToString().ToLowerInvariant()}");
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

    /// <summary>状態異常のアイコンを横に並べる(幅に収まらなければ詰める)。アイコンの無い状態は名前の字で出す。
    /// 激怒していれば「激怒」、集中している呪文があれば「集中」の印を先頭に出す(載せると説明)。</summary>
    private static void ConditionIcons(Draw2D d, GameServices services, IReadOnlyList<Condition> conditions, Vector2 at, float width, float size, string? concentration = null, bool raging = false)
    {
        float iw = size + 4, gap = 4;
        float x = at.X;
        var s = services.Strings;
        if (raging) Mark("rage", FeatureText.Name(services, "rage"), Theme.Caution, FeatureText.Name(services, "rage"), s["condition.about.raging"]);
        if (concentration != null)
        {
            string spell = services.Data.Spells.TryGetValue(concentration, out var sp) ? services.Name(sp.Name) : concentration;
            Mark("concentration", s["spell.concentration"], Theme.Good, s.Format("condition.concentrating", spell), s["condition.about.concentrating"]);
        }

        // アイコン(art/conditions/<id>.png)があればそれ、無ければ字で出す印
        void Mark(string iconId, string text, Color color, string title, string about)
        {
            RectangleF rect;
            if (services.ConditionIcon(iconId) is { } icon)
            {
                rect = new RectangleF(x, at.Y, iw, iw);
                d.Image(icon, rect, Color.White);
            }
            else
            {
                d.Text(text, new Vector2(x, at.Y + 2), size * 0.8f, color);
                rect = new RectangleF(x, at.Y, d.Fonts.Measure(text, size * 0.8f).X, iw);
            }
            Tooltip.Hover(rect, title, about);
            x += rect.Width + gap;
            width -= rect.Width + gap;
        }
        float step = Math.Min(iw + gap, conditions.Count > 1 ? Math.Max(1, (width - iw) / (conditions.Count - 1)) : iw + gap);
        foreach (var cond in conditions)
        {
            string id = cond.ToString().ToLowerInvariant();
            if (services.ConditionIcon(id) is { } icon)
            {
                var rect = new RectangleF(x, at.Y, iw, iw);
                d.Image(icon, rect, Color.White);
                HoverCondition(services, rect, "condition." + id);   // 載せると、状態の名前と説明
                x += step;
            }
            else
            {
                string name = services.Strings["condition." + id];
                d.Text(name, new Vector2(x, at.Y + 2), size * 0.8f, Theme.Caution);
                float nw = d.Fonts.Measure(name, size * 0.8f).X;
                HoverCondition(services, new RectangleF(x, at.Y, nw, iw), "condition." + id);
                x += nw + gap;
            }
        }
    }

    /// <summary>状態のアイコンの上にマウスがあれば、名前(key)と説明(key の .about)の窓を出す。</summary>
    private static void HoverCondition(GameServices services, RectangleF rect, string key)
    {
        var s = services.Strings;
        string about = key.Insert(key.IndexOf('.') + 1, "about.");
        if (s.Has(about)) Tooltip.Hover(rect, s[key], s[about]);
    }

    private static void Fit(Draw2D d, string text, Vector2 at, float width, float size, Color color)
    {
        float tw = d.Fonts.Measure(text, size).X;
        if (tw > width) size *= width / tw;
        d.Text(text, at, size, color);
    }
}

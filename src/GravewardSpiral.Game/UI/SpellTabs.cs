using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 呪文を唱える画面の上の、レベルのタブ(初・1〜9。戦闘とキャンプで使う)。タブはそのまま「何レベルのスロットで唱えるか」で、
/// 選んだらそのレベルで唱える(聞き直さない)。上のスロットで強くなる呪文は、上のレベルのタブにも出す。
/// タブの幅と位置は固定。色: まだ持っていないレベルは黒、唱える呪文が無いレベルは灰、
/// それ以外はスロットの残りで 0 は赤、1 は黄、2〜3 は緑、4 以上は青。
/// </summary>
public static class SpellTabs
{
    public const int Count = 10;   // 初(0)と 1〜9

    public static readonly Color Empty = new(240, 90, 80);
    public static readonly Color Plenty = new(80, 140, 255);
    public static readonly Color NotYet = new(0, 0, 0);

    /// <summary>
    /// そのタブに出す呪文か。そのレベルの呪文と、上のスロットで強くなる、もっと低いレベルの呪文。
    /// 強くならない呪文も、自分のレベルのスロットが無い(使い切った、ウォーロックのように持っていない)ときは、
    /// 払える一番低い上のタブに出す(出さないと、唱える手段が無くなる)。
    /// </summary>
    public static bool Shows(GameData data, Character c, SpellDef sp, int tab) => Spellcasting.ListedAt(data, c, sp, tab);

    /// <summary>そのレベルのスロットを持っているか(初級呪文のタブはいつも持っている)。</summary>
    public static bool Has(GameData data, Character c, int tab) =>
        tab == 0 || Spellcasting.CasterClass(data, c) is { } cls && Spellcasting.MaxSlots(cls, c, tab) > 0;

    /// <summary>選べるタブ(スロットを持っていて、出す呪文のあるタブ)。左右で回る先。</summary>
    public static List<int> Tabs(GameData data, Character c, IEnumerable<SpellDef> spells)
    {
        var list = spells.ToList();
        return Enumerable.Range(0, Count).Where(t => Has(data, c, t) && list.Any(sp => Shows(data, c, sp, t))).ToList();
    }

    /// <summary>そのタブで唱えられるか(初級呪文はいつも。ほかはそのレベルのスロットが残っているか)。</summary>
    public static bool CanCast(GameData data, Character c, int tab) =>
        tab == 0 || Spellcasting.CasterClass(data, c) is { } cls && Spellcasting.SlotsLeft(cls, c, tab) > 0;

    /// <summary>選んでいるタブに出す、そのレベルのスロットの残り(「(3)」。初級呪文のタブは無し)。</summary>
    public static string? Left(GameData data, Character c, int tab) =>
        tab > 0 && Spellcasting.CasterClass(data, c) is { } cls ? $"({Spellcasting.SlotsLeft(cls, c, tab)})" : null;

    /// <summary>はじめに開くタブ: 唱えられる一番低いタブ(無ければ、選べる一番低いタブ)。</summary>
    public static int First(GameData data, Character c, IEnumerable<SpellDef> spells)
    {
        var tabs = Tabs(data, c, spells);
        return tabs.Where(t => CanCast(data, c, t)).DefaultIfEmpty(tabs.DefaultIfEmpty(0).First()).First();
    }

    /// <summary>今のタブから step だけ動かした先(選べるタブだけを回る)。動けなければ今のまま。</summary>
    public static int Move(List<int> tabs, int current, int step)
    {
        if (tabs.Count == 0) return current;
        int i = tabs.IndexOf(current);
        if (i < 0) return tabs[0];
        return tabs.Count < 2 ? current : tabs[(i + step + tabs.Count) % tabs.Count];
    }

    /// <summary>そのタブの色。</summary>
    public static Color ColorFor(GameData data, Character c, int tab, List<int> tabs)
    {
        if (!Has(data, c, tab)) return NotYet;
        if (!tabs.Contains(tab)) return Theme.Disabled;
        if (tab == 0 || Spellcasting.CasterClass(data, c) is not { } cls) return Theme.Text;
        return Spellcasting.SlotsLeft(cls, c, tab) switch
        {
            0 => Empty,
            1 => Theme.Gold,
            <= 3 => Theme.Good,
            _ => Plenty,
        };
    }

    public static RectangleF TabRect(RectangleF area, int tab) => new(area.X + tab * area.Width / Count, area.Y, area.Width / Count, area.Height);

    /// <summary>マウスの指すタブ(帯の外なら null)。</summary>
    public static int? At(RectangleF area, Vector2 mouse) =>
        area.Contains(mouse) ? Math.Clamp((int)((mouse.X - area.X) / (area.Width / Count)), 0, Count - 1) : null;

    public static void Draw(Draw2D d, RectangleF area, GameData data, Character c, int selected, List<int> tabs, string cantripLabel, float size = 20f)
    {
        for (int tab = 0; tab < Count; tab++)
        {
            var r = TabRect(area, tab);
            if (tab == selected)
            {
                d.FillRect(r, Theme.SelectedFill);
                d.Rect(r, Theme.SelectedFrame);
            }
            // 選んでいるタブは、レベルの数字の代わりにスロットの残り「(3)」を出す(何レベルかは位置と枠で分かる)
            string label = tab == 0 ? cantripLabel : tab == selected ? Left(data, c, tab) ?? tab.ToString() : tab.ToString();
            d.Text(label, new Vector2(r.X + r.Width / 2, r.Y + (r.Height - size) / 2 - 1), size, ColorFor(data, c, tab, tabs), TextAlign.Center);
        }
    }
}

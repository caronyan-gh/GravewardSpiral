using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// 20 の能力で呪文を1つ選ぶ画面(訓練場)。呪文の熟達(ウィザード L18: 枠を使わずに何度でも唱える呪文)と、
/// 森の主(ドルイド L20: 戦闘の始めからいる召喚)。選ぶとすぐ決まり、閉じる。今選んでいるものには印を付ける。
/// </summary>
public sealed class SpellChoiceScene(Character c, SpellChoiceScene.Kind kind, Action? onClose = null) : MenuScene
{
    public enum Kind { Mastery, Companion }

    public override void Leave() => onClose?.Invoke();

    private IEnumerable<SpellDef> Choices => kind == Kind.Mastery
        ? Spellcasting.MasteryChoices(Services.Data, c)
        : Spellcasting.CompanionChoices(Services.Data, c);

    private string? Current => kind == Kind.Mastery ? c.MasterySpell : c.CompanionSpell;

    protected override string Title => Services.Strings.Format(kind == Kind.Mastery ? "choice.mastery_title" : "choice.companion_title", c.Name);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        bool any = false;
        foreach (var sp in Choices)
        {
            any = true;
            yield return new MenuItem(Services.Name(sp.Name), sp.Id == Current ? s["choice.current"] : s.Format("spell.level", sp.Level), Tag: sp, Icon: Services.SpellIcon(sp));
        }
        if (!any) yield return new MenuItem(s["choice.none"], Enabled: false);
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not SpellDef sp)
        {
            Stack.Pop();
            return;
        }
        if (kind == Kind.Mastery) c.MasterySpell = sp.Id;
        else c.CompanionSpell = sp.Id;
        Services.Audio.PlaySe(Sounds.Confirm);
        Stack.Pop();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = Line(d, area, area.Y + 20, s[kind == Kind.Mastery ? "choice.mastery_about" : "choice.companion_about"], Theme.Dim, 17);
        if (List.Current?.Tag is SpellDef sp)
        {
            y = Line(d, area, y + 10, Services.Name(sp.Name), Theme.Title, 22);
            foreach (var line in SpellText.Effects(s, sp, sp.Level)) y = Line(d, area, y, line, Theme.Text, 17);
        }
    }
}

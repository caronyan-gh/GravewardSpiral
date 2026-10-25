using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 呪文を選ぶ画面(初級呪文、覚える呪文・呪文書、準備する呪文)。決定で印を付け外しし、数の上限まで選べる。
/// 「おまかせ」で足りない分を埋め、「終わる」か取消で戻る。
/// マルチクラスで、その種類の呪文を持つ術者のクラスが2つ以上あれば、左右でクラスを切り替える(呪文はクラスごと)。
/// </summary>
public sealed class SpellPickScene : MenuScene
{
    public enum Kind { Cantrips, Known, Prepared }

    private readonly Character _c;
    private readonly Kind _kind;

    public SpellPickScene(Character c, Kind kind)
    {
        _c = c;
        _kind = kind;
    }

    private int _class;

    /// <summary>この種類の呪文を持つ術者のクラス(初級呪文・覚える・準備する、のどれがあるかで選ぶ)。</summary>
    private List<ClassDef> Classes => Spellcasting.CasterClasses(Services.Data, _c).Where(cls => _kind switch
    {
        Kind.Cantrips => Spellcasting.CantripsKnown(cls, _c) > 0,
        Kind.Known => Spellcasting.Kind(cls) is CasterKind.Known or CasterKind.Pact or CasterKind.Book && Spellcasting.SpellsKnown(cls, _c) > 0,
        _ => Spellcasting.Kind(cls) is CasterKind.Prepared or CasterKind.Book && Spellcasting.PreparedCount(cls, _c) > 0,
    }).ToList();

    private ClassDef Cls => Classes is { Count: > 0 } list ? list[Math.Clamp(_class, 0, list.Count - 1)] : Spellcasting.CasterClass(Services.Data, _c)!;

    private SpellBook Book => _c.Book(Cls.Id);

    private List<string> Target => _kind switch
    {
        Kind.Cantrips => Book.Cantrips,
        Kind.Known => Book.Known,
        _ => Book.Prepared,
    };

    public override void Update(GameTime time, Input.InputState input)
    {
        var classes = Classes;
        if (classes.Count > 1 && (input.Repeat(Input.GameAction.Left) || input.Repeat(Input.GameAction.Right)))
        {
            _class = (_class + (input.Repeat(Input.GameAction.Right) ? 1 : classes.Count - 1)) % classes.Count;
            Services.Audio.PlaySe(Audio.Sounds.Cursor);
            List.SetItems(Items(), 0);
            return;
        }
        base.Update(time, input);
    }

    private int Limit => _kind switch
    {
        Kind.Cantrips => Spellcasting.CantripsKnown(Cls, _c),
        Kind.Known => Spellcasting.SpellsKnown(Cls, _c),
        _ => Spellcasting.PreparedCount(Cls, _c),
    };

    /// <summary>選べる呪文。準備はウィザードなら呪文書から、ほかはクラスの呪文から。</summary>
    private IEnumerable<SpellDef> Pool() => _kind switch
    {
        Kind.Cantrips => Spellcasting.ClassSpells(Services.Data, Cls, _c, cantrips: true),
        Kind.Prepared when Spellcasting.Kind(Cls) == CasterKind.Book =>
            Book.Known.Select(id => Services.Data.Spells.GetValueOrDefault(id)).OfType<SpellDef>().OrderBy(s => s.Level),
        _ => Spellcasting.ClassSpells(Services.Data, Cls, _c, cantrips: false),
    };

    protected override string Title
    {
        get
        {
            string title = Services.Strings.Format("pick.title." + _kind.ToString().ToLowerInvariant(), _c.Name);
            // マルチクラスなら、どのクラスの呪文か(左右で切り替え)
            return Classes.Count > 1 ? $"{title}  ◀ {Services.Name(Cls.Name)} ▶" : title;
        }
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        yield return new MenuItem(s["pick.done"], s.Format("pick.count", Target.Count, Limit), Tag: "done");
        yield return new MenuItem(s["pick.auto"], Tag: "auto");
        foreach (var sp in Pool())
        {
            bool on = Target.Contains(sp.Id);
            yield return new MenuItem($"{(on ? "◆" : "◇")} {Services.Name(sp.Name)}", SpellText.Level(s, sp),
                on || Target.Count < Limit, sp, Icon: Services.SpellIcon(sp));
        }
    }

    protected override void Choose(MenuItem item, int index)
    {
        switch (item.Tag)
        {
            case "done":
                Cancel();
                return;
            case "auto":
                Spellcasting.AutoPick(Services.Data, _c, Cls);
                break;
            case SpellDef sp:
                if (!Target.Remove(sp.Id) && Target.Count < Limit) Target.Add(sp.Id);
                // 呪文書から外した呪文は、準備からも外す
                if (_kind == Kind.Known && Spellcasting.Kind(Cls) == CasterKind.Book) Book.Prepared.RemoveAll(id => !Book.Known.Contains(id));
                break;
        }
        int cursor = List.Cursor;
        Refresh();
        List.Cursor = cursor;
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        if (List.Current?.Tag is SpellDef sp)
        {
            y = Line(d, area, y, Services.Name(sp.Name), Theme.Title, 26);
            y = Line(d, area, y, SpellText.Header(s, sp), Theme.Dim, 18);
            int level = sp.Scale == "character" ? _c.Level : Math.Max(1, sp.Level);
            foreach (var line in SpellText.Effects(s, sp, level)) y = Line(d, area, y, line, Theme.Text, 19);
            if (!sp.InCombat) y = Line(d, area, y, s["spell.not_in_combat"], Theme.Warn, 17);
            if (sp.Note != null) Line(d, area, y + 6, sp.Note, Theme.Dim, 16);
            return;
        }
        y = Line(d, area, y, s["pick.about." + _kind.ToString().ToLowerInvariant()], Theme.Text, 19);
        if (SpellText.Slots(s, Services.Data, _c) is { } slots) Line(d, area, y + 10, slots, Theme.Dim, 18);
    }
}

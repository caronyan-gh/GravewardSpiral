using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// キャンプ(迷宮でメニュー)。小休憩(1時間。ヒットダイスで HP を戻す。休んでいるあいだに敵が来ることがある)、
/// 呪文を唱える(回復など)、呪文の準備、状態を見る。
/// </summary>
public sealed class CampScene : MenuScene
{
    private enum Mode { Main, CastWho, CastSpell, CastSlot, CastTarget, Wish, PrepareWho, StatusWho, ItemsWho, Reorder }

    /// <summary>新しい隊列(選んだ順)。</summary>
    private readonly List<Character> _newOrder = [];

    /// <summary>小休憩の時間(歩数で数える。1歩 = 1分)。</summary>
    public const int ShortRestSteps = 60;

    private readonly Func<bool> _restInterrupted;
    private readonly Action _returnToTown;
    private Mode _mode;
    private Character? _caster;
    private SpellDef? _spell;
    private int _slot;

    /// <summary>呪文の一覧で見ているレベルのタブ(0 は初級呪文、1〜9 は呪文のレベル)。戦闘と同じ形。</summary>
    private int _spellTab;

    /// <summary>呪文を選ぶときは、一覧の上にレベルのタブの帯を出し、一覧をその分だけ下げる。</summary>
    private static readonly RectangleF SpellTabArea = new(ListArea.X, ListArea.Y - 4, ListArea.Width, 34);
    private static readonly RectangleF SpellListArea = new(ListArea.X, ListArea.Y + 42, ListArea.Width, ListArea.Height - 42);

    /// <param name="restInterrupted">小休憩のあとで呼ぶ。敵が来たら true(戦闘は呼んだ側が始める)。</param>
    /// <param name="returnToTown">ウィッシュで町へ帰るときに呼ぶ。</param>
    public CampScene(Func<bool> restInterrupted, Action returnToTown)
    {
        _restInterrupted = restInterrupted;
        _returnToTown = returnToTown;
    }

    private Roster Roster => Services.Session!.Roster;
    private List<Character> Party => Roster.PartyMembers.ToList();

    protected override string Title => Services.Strings["camp.title"];

    private static bool IsCaster(GameData data, Character c) => Spellcasting.CasterClass(data, c) != null;

    private static bool Prepares(GameData data, Character c) =>
        Spellcasting.CasterClasses(data, c).Any(cls => Spellcasting.Kind(cls) is CasterKind.Prepared or CasterKind.Book);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        var data = Services.Data;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["camp.rest"], Tag: "rest");
                yield return new MenuItem(s["camp.cast"], NoMagic ? s["lock.antimagic"] : null, !NoMagic && Party.Any(c => c.CanAct && CampSpells(c).Any()), "cast");
                yield return new MenuItem(s["camp.prepare"], Enabled: Party.Any(c => Prepares(data, c)), Tag: "prepare");
                yield return new MenuItem(s["camp.items"], Tag: "items");
                yield return new MenuItem(s["camp.status"], Tag: "status");
                yield return new MenuItem(s["camp.swap"], Enabled: Party.Count > 1, Tag: "swap");
                yield return new MenuItem(s["camp.memo"], Enabled: Services.Session?.Exploration?.Notes.Count > 0, Tag: "memo");
                if (StoryScene.Available(Services)) yield return new MenuItem(s["camp.story"], Tag: "story");
                yield return new MenuItem(s["camp.backlog"], Enabled: Services.LastCombatLog.Count > 0, Tag: "backlog");
                yield return new MenuItem(s["camp.options"], Tag: "options");
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.CastWho:
                foreach (var c in Party) yield return new MenuItem(c.Name, Enabled: c.CanAct && CampSpells(c).Any(), Tag: c);
                break;
            case Mode.PrepareWho:
                foreach (var c in Party) yield return new MenuItem(c.Name, Enabled: Prepares(data, c), Tag: c);
                break;
            case Mode.StatusWho:
            case Mode.ItemsWho:
                foreach (var c in Party) yield return new MenuItem(c.Name, Tag: c);
                break;
            case Mode.Reorder:
                // まだ選んでいない人だけ(選んだ並びは文で見せる)
                foreach (var c in Party.Where(c => !_newOrder.Contains(c)))
                    yield return new MenuItem(c.Name, c.IsAlive ? $"{c.Hp}/{c.MaxHp}" : s[$"state.{c.State.ToString().ToLowerInvariant()}"], Tag: c);
                break;
            case Mode.CastSpell:
            {
                // 今のタブに出す呪文(上のスロットで強くなる呪文も)。タブがそのまま唱えるスロットのレベルなので、レベルと残りの数は出さない
                bool castable = SpellTabs.CanCast(data, _caster!, _spellTab);
                bool any = false;
                foreach (var sp in CampSpells(_caster!).Where(x => SpellTabs.Shows(x, _spellTab)))
                {
                    any = true;
                    yield return SpellText.CastItem(Services, _caster!, sp, null, castable);
                }
                if (!any) yield return new MenuItem(s["combat.no_spells_at_level"], Enabled: false);
                break;
            }
            case Mode.CastSlot:
            {
                var cls = Spellcasting.CasterClass(data, _caster!)!;
                for (int l = _spell!.Level; l <= 9; l++)
                {
                    int left = Spellcasting.SlotsLeft(cls, _caster!, l);
                    if (left > 0) yield return new MenuItem(s.Format("spell.level", l), $"({left})", Tag: l);
                }
                break;
            }
            case Mode.Wish:
                foreach (var id in data.WishOrder)
                {
                    var w = data.Wishes[id];
                    yield return new MenuItem(Services.Name(w.Name), w.Available ? null : s["town.not_yet"], w.Available, w);
                }
                break;
            case Mode.CastTarget:
                foreach (var c in Party)
                    yield return new MenuItem(c.Name, c.IsAlive ? $"{c.Hp}/{c.MaxHp}" : s[$"state.{c.State.ToString().ToLowerInvariant()}"],
                        c.State is LifeState.Alive or LifeState.Unconscious, c);
                break;
        }
    }

    private bool NoMagic => Services.Session?.Exploration is { } e && e.Cell.Zone.HasFlag(Core.Maze.Zone.AntiMagic);

    /// <summary>キャンプで唱えられる呪文(唱えた人が持っているもの)。</summary>
    private IEnumerable<SpellDef> CampSpells(Character c) => Spellcasting.Castable(Services.Data, c).Where(x => x.InCamp);

    private void Go(Mode mode)
    {
        _mode = mode;
        List.Area = mode == Mode.CastSpell ? SpellListArea : ListArea;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));   // 選べる最初の項目から
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (_mode, item.Tag)
        {
            case (Mode.Main, "rest"): Rest(); break;
            case (Mode.Main, "cast"): Go(Mode.CastWho); break;
            case (Mode.Main, "prepare"): Go(Mode.PrepareWho); break;
            case (Mode.Main, "status"): Go(Mode.StatusWho); break;
            case (Mode.Main, "swap"):
                _newOrder.Clear();
                Message = s["camp.order_first"];
                Go(Mode.Reorder);
                break;
            case (Mode.Reorder, Character c):
                _newOrder.Add(c);
                // 残りが1人なら、その人が最後
                var rest = Party.Where(x => !_newOrder.Contains(x)).ToList();
                if (rest.Count == 1) _newOrder.Add(rest[0]);
                if (_newOrder.Count >= Party.Count)
                {
                    Roster.Reorder(_newOrder);
                    Message = s["camp.order_done"];
                    Go(Mode.Main);
                    break;
                }
                Message = OrderMessage();
                Go(Mode.Reorder);
                break;
            case (Mode.Main, "memo") when Services.Session?.Exploration is { } memoState: Stack.Push(new MemoScene(memoState)); break;
            case (Mode.Main, "story"): Stack.Push(new StoryScene()); break;
            case (Mode.Main, "backlog"): Stack.Push(new BacklogScene(Services.LastCombatLog)); break;
            case (Mode.Main, "options"): Stack.Push(new OptionsScene(overlay: true)); break;
            case (Mode.Main, "items"): Go(Mode.ItemsWho); break;
            case (Mode.ItemsWho, Character c): Stack.Push(new InventoryScene(c)); break;
            case (Mode.Main, "back"): Stack.Pop(); break;
            case (Mode.StatusWho, Character c): Stack.Push(new StatusScene(c)); break;
            case (Mode.PrepareWho, Character c): Stack.Push(new SpellPickScene(c, SpellPickScene.Kind.Prepared)); break;
            case (Mode.CastWho, Character c):
                _caster = c;
                // はじめのタブは、唱えられる一番低いタブ
                _spellTab = SpellTabs.First(Services.Data, c, CampSpells(c));
                Go(Mode.CastSpell);
                break;
            case (Mode.CastSpell, SpellDef sp):
                _spell = sp;
                _slot = _spellTab;   // タブのレベルのスロットで唱える
                AfterSlot();
                break;
            case (Mode.CastSlot, int level):
                _slot = level;
                AfterSlot();
                break;
            case (Mode.CastTarget, Character target):
                Cast(target);
                break;
            case (Mode.Wish, Core.Magic.WishDef wish):
                Wish(wish);
                break;
        }
    }

    private void AfterSlot()
    {
        if (_spell!.Effects.Any(e => e.Type == Core.Effects.EffectPart.Wish)) Go(Mode.Wish);
        else if (_spell.Target == SpellTarget.Ally) Go(Mode.CastTarget);
        else Cast(null);
    }

    /// <summary>ウィッシュ: 選んだ大きな効果を起こす(スロットを使う)。</summary>
    private void Wish(Core.Magic.WishDef wish)
    {
        Spellcasting.UseSlot(_caster!, Math.Max(9, _slot));
        Services.Audio.PlaySe(Sounds.SpellCast);
        if (wish.Special == Core.Magic.WishDef.ReturnTown)
        {
            Stack.Pop();
            _returnToTown();
            return;
        }
        var state = new CombatState { Party = Party.Select(c => new PartyMember(c, Services.Data)).ToList(), Groups = [] };
        foreach (var p in state.Party) Lasting.Load(p);
        var engine = new CombatEngine(Services.Data, Services.Session!.Rng[RngStreams.Names.Combat]);
        engine.ApplyToParty(state, state.Party.First(p => p.Character == _caster), wish.Effects);
        foreach (var p in state.Party) Lasting.Save(p);
        Message = Services.Strings.Format("camp.wished", Services.Name(wish.Name));
        Go(Mode.Main);
    }

    /// <summary>戦闘の外で唱える(敵のいない戦闘の状態を作って、同じ処理を使う)。結果の最後の行を出す。</summary>
    private void Cast(Character? target)
    {
        var party = Party;
        var state = new CombatState { Party = party.Select(c => new PartyMember(c, Services.Data)).ToList(), Groups = [] };
        var engine = new CombatEngine(Services.Data, Services.Session!.Rng[RngStreams.Names.Combat]);
        foreach (var p in state.Party) Lasting.Load(p);
        var me = state.Party.First(p => p.Character == _caster);
        int member = target == null ? -1 : party.IndexOf(target);
        engine.CastSpell(state, me, new PlayerCommand(CommandKind.Cast, -1, member, _spell!.Id, _slot));
        foreach (var p in state.Party) Lasting.Save(p);   // 解けた状態を消す
        // 迷宮の呪文(明かり、落下を防ぐ、町へ帰る)
        var exploration = Services.Session!.Exploration;
        foreach (var part in _spell.Effects)
        {
            switch (part.Type)
            {
                case Core.Effects.EffectPart.Light when exploration != null:
                    exploration.LightMinutes = Math.Max(exploration.LightMinutes, part.Value ?? 60);
                    break;
                case Core.Effects.EffectPart.FeatherFall when exploration != null:
                    exploration.FeatherMinutes = Math.Max(exploration.FeatherMinutes, part.Value ?? 60);
                    break;
                case Core.Effects.EffectPart.Recall:
                    Stack.Pop();
                    _returnToTown();
                    return;
            }
        }
        Message = string.Join(" ", state.Log.Skip(1).Select(e => CombatText.Format(Services, e)).TakeLast(2));
        Services.Audio.PlaySe(_spell.Effects.Any(e => e.Type == Core.Effects.EffectPart.Heal) ? Sounds.Heal : Sounds.SpellCast);
        // 続けて唱えられるよう、同じ人の呪文の一覧に戻る(タブはそのまま、カーソルは今の呪文に)
        _mode = Mode.CastSpell;
        List.Area = SpellListArea;
        var items = Items().ToList();
        int at = items.FindIndex(i => i.Tag == _spell);
        List.SetItems(items, at >= 0 && items[at].Enabled ? at : Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    /// <summary>小休憩。全員がヒットダイスを使い、ウォーロックはスロットが戻る。敵が来たら戦闘へ。</summary>
    private void Rest()
    {
        var s = Services.Strings;
        var rng = Services.Session!.Rng[RngStreams.Names.Combat];
        // 人ごとに、戻った HP と残りのヒットダイスを知らせる(HP が満タンで使わなかった人は出さない)
        var rested = new List<string>();
        foreach (var c in Party)
        {
            int hp = c.Hp;
            int used = c.ShortRest(Services.Data, rng);
            if (!c.IsAlive) continue;
            if (used > 0) rested.Add(s.Format("camp.rest_healed", c.Name, c.Hp - hp, c.HitDice, c.Level));
            else if (c.Hp < c.MaxHp) rested.Add(s.Format("camp.rest_no_dice", c.Name, c.Hp, c.MaxHp));
        }
        // 休むあいだに、1人1個ずつ未鑑定の品を調べる(休みが邪魔されたら分からない)
        var unknownNames = Party.SelectMany(c => c.Inventory.Items.Concat(c.Inventory.Equipped.Values)).Where(i => !i.Identified)
            .ToDictionary(i => (object)i, i => ItemText.Name(Services, i, count: false), ReferenceEqualityComparer.Instance);
        if (Services.Session.Exploration is { } e)
        {
            e.Steps += ShortRestSteps;
            e.StepsSinceCombat += ShortRestSteps;
        }
        Services.Audio.PlaySe(Sounds.InnRest);
        if (_restInterrupted())
        {
            Stack.Pop();
            return;
        }
        var studied = RestStudy.Run(Party);
        Message = string.Join("\n", studied.Select(x => s.Format("camp.studied", x.Who.Name, unknownNames[x.Item], ItemText.Name(Services, x.Item, count: false)))
            .Prepend(string.Join("\n", rested.Prepend(s["camp.rested"]))));
        if (studied.Count > 0) Services.Audio.PlaySe(Sounds.Confirm);
        Refresh();
    }

    /// <summary>並べ直しの途中の文(ここまでの並びと、次は何番目か)。</summary>
    private string OrderMessage()
    {
        var s = Services.Strings;
        if (_newOrder.Count == 0) return s["camp.order_first"];
        string sofar = string.Join("  ", _newOrder.Select((c, i) => s.Format("camp.order_n", i + 1, c.Name)));
        return sofar + "\n" + s.Format("camp.order_next", _newOrder.Count + 1);
    }

    protected override void Cancel()
    {
        if (_mode == Mode.Main) Stack.Pop();
        else if (_mode is Mode.CastSpell) Go(Mode.CastWho);
        else if (_mode is Mode.CastSlot or Mode.CastTarget or Mode.Wish) Go(Mode.CastSpell);
        else if (_mode is Mode.Reorder && _newOrder.Count > 0)
        {
            // 1人戻す
            _newOrder.RemoveAt(_newOrder.Count - 1);
            Message = OrderMessage();
            Go(Mode.Reorder);
        }
        else Go(Mode.Main);
    }

    public override void Update(GameTime time, Input.InputState input)
    {
        // 呪文: 左右かタブのクリックで、呪文のレベルを切り替える(呪文のあるタブだけを回る)
        if (_mode == Mode.CastSpell)
        {
            var tabs = SpellTabs.Tabs(Services.Data, _caster!, CampSpells(_caster!));
            int next = _spellTab;
            if (input.Repeat(Input.GameAction.Left)) next = SpellTabs.Move(tabs, _spellTab, -1);
            else if (input.Repeat(Input.GameAction.Right)) next = SpellTabs.Move(tabs, _spellTab, 1);
            else if (input.LeftClicked && input.Mouse is { } m && SpellTabs.At(SpellTabArea, m) is int tab && tabs.Contains(tab)) next = tab;
            if (next != _spellTab)
            {
                _spellTab = next;
                Services.Audio.PlaySe(Sounds.Cursor);
                Go(Mode.CastSpell);
                return;
            }
        }
        base.Update(time, input);
    }

    public override void Draw(Draw2D d)
    {
        base.Draw(d);
        if (_mode == Mode.CastSpell)
            SpellTabs.Draw(d, SpellTabArea, Services.Data, _caster!, _spellTab, SpellTabs.Tabs(Services.Data, _caster!, CampSpells(_caster!)), Services.Strings["combat.tab_cantrip"], 22);
    }

    protected override int? PartyHighlight => _mode is Mode.CastWho or Mode.PrepareWho or Mode.StatusWho or Mode.ItemsWho or Mode.CastTarget ? List.Cursor
        : _mode == Mode.Reorder && List.Current?.Tag is Character c ? Roster.Party.IndexOf(c.Id) : null;

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        if (List.Current?.Tag is SpellDef sp)
        {
            y = Line(d, area, y, Services.Name(sp.Name), Theme.Title, 26);
            y = Line(d, area, y, SpellText.Header(s, sp), Theme.Dim, 18);
            int level = sp.Scale == "character" ? _caster!.Level : Math.Max(1, _mode == Mode.CastSpell ? Math.Max(sp.Level, _spellTab) : sp.Level);
            foreach (var line in SpellText.Effects(s, sp, level)) y = Line(d, area, y, line, Theme.Text, 19);
            return;
        }
        if (List.Current?.Tag is Character c)
        {
            StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, y));
            return;
        }
        string key = List.Current?.Tag switch
        {
            "rest" => "camp.about_rest",
            "cast" => "camp.about_cast",
            "prepare" => "camp.about_prepare",
            "status" => "camp.about_status",
            "swap" => "camp.about_swap",
            "memo" => "camp.about_memo",
            "story" => "camp.about_story",
            "backlog" => "camp.about_backlog",
            "options" => "camp.about_options",
            _ => "camp.about",
        };
        y = Line(d, area, y, s[key], Theme.Text, 19);
        // 小休憩では、みんなのヒットダイスの残りを薄く見せる(休んだあとの知らせが出ているあいだは出さない)
        if (List.Current?.Tag is "rest" && Message == null)
        {
            y += 16;
            d.Text(s["camp.hit_dice"], new Vector2(area.X + 20, y), 18, Theme.Dim);
            foreach (var m in Party)
            {
                y += 26;
                var color = m.HitDice == 0 ? Color.Lerp(Theme.Warn, Theme.Dim, 0.4f) : Theme.Dim;
                d.Text(m.Name, new Vector2(area.X + 20, y), 18, color);
                d.Text($"{m.HitDice}/{m.Level}", new Vector2(area.X + 260, y), 18, color, TextAlign.Right);
            }
        }
    }
}

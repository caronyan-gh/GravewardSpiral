using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using GravewardSpiral.Game.Input;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 1人の持ち物。上の切り替え(クリックか左右)で「装備」と「袋」を分けて見る。
/// 装備: 部位を選ぶと、右の欄に「外す」と、袋の中のその部位に付けられる品を並べ、選んだものに替える。
/// 袋: 品を選ぶと「装備する・使う・渡す・捨てる」を選べる。
/// 町とキャンプから開く(戦闘の中のアイテムは、戦闘のコマンドで使う)。members を渡すと、その並びの仲間に切り替えられる(MemberSwitch)。
/// </summary>
public sealed class InventoryScene(Character first, IReadOnlyList<Character>? members = null) : MenuScene
{
    private readonly MemberSwitch _switch = new(members ?? [first], first) { PartyArea = PartyArea };

    protected override MemberSwitch? Switch => _switch;

    private Character Owner => _switch.Current;

    private enum Mode { List, Actions, GiveTo, UseOn, SlotPick }

    /// <summary>部位を選んだときの、右の欄の候補(外す・付けられる品)。</summary>

    private Mode _mode;

    private enum Tab { Equip, Bag }

    private Tab _tab;

    private static RectangleF TabRect(Tab t) => new(ListArea.X - 12 + (int)t * 130, 90, 120, 32);   // 右に AC と同調の数を置くので、短めに
    private object? _picked;   // 選んだもの(EquipSlot か、袋の中の ItemInstance)

    private Inventory Inv => Owner.Inventory;
    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();

    protected override string Title => Services.Strings.Format("inv.title", Owner.Name);

    protected override int? PartyHighlight => _mode is Mode.GiveTo or Mode.UseOn ? List.Cursor : Party.IndexOf(Owner) is >= 0 and var i ? i : null;

    public override void Enter()
    {
        Inv.MergeStacks(Data.Items);   // 重ねられるようになる前に別々に拾った品もまとめる
        base.Enter();
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.List when _tab == Tab.Bag:
                foreach (var it in Inv.Items) yield return new MenuItem(ItemText.Name(Services, it), ItemText.Hands(Services, it), Tag: it);
                if (Inv.Items.Count == 0) yield return new MenuItem(s["inv.empty"], Enabled: false);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.List:
                foreach (var slot in Enum.GetValues<EquipSlot>())
                {
                    string name = Inv[slot] is { } it ? ItemText.Name(Services, it) : "-";
                    // 付けているか、袋に付けられる品があれば選べる
                    yield return new MenuItem($"{s["slot." + slot.ToString().ToLowerInvariant()]}: {name}",
                        Inv[slot] is { } i2 ? (i2.Attuned ? "◆" : ItemText.Hands(Services, i2)) : null,
                        Inv[slot] != null || Candidates(slot).Any(), slot);
                }
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.SlotPick when _picked is EquipSlot ps:
            {
                // 部位の付け替え: 1行目は部位と今付けているもの(選べない)、次に外す、袋の候補(習熟のあるものを上に)
                string now = Inv[ps] is { } cur ? ItemText.Name(Services, cur) : "-";
                yield return new MenuItem($"{s["slot." + ps.ToString().ToLowerInvariant()]}: {now}", Enabled: false);
                if (Inv[ps] != null) yield return new MenuItem(s["inv.unequip"], Tag: "unequip");
                foreach (var i in Candidates(ps).OrderBy(i => Owner.Proficient(Data, Data.Items[i.ItemId]) ? 0 : 1))
                    yield return Owner.CanWear(Data, Data.Items[i.ItemId])
                        ? new MenuItem(ItemText.Name(Services, i), ItemText.Hands(Services, i), Tag: i)
                        : new MenuItem(ItemText.Name(Services, i), s["inv.no_proficiency"], false, i);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            }
            case Mode.Actions:
                if (_picked is EquipSlot)
                {
                    yield return new MenuItem(s["inv.unequip"], Tag: "unequip");
                }
                else if (_picked is ItemInstance item && Data.Items.TryGetValue(item.ItemId, out var def))
                {
                    if (def.IsEquippable)
                    {
                        bool wear = Owner.CanWear(Data, def);   // 習熟の無い鎧と盾は付けられない
                        yield return new MenuItem(s["inv.equip"], wear ? null : s["inv.no_proficiency"], wear, "equip");   // 習熟の無い武器は、品の名前の色で知らせる
                    }
                    if (def.IsUsable) yield return new MenuItem(s["inv.use"], Tag: "use");
                    yield return new MenuItem(s["inv.give"], Enabled: Party.Count > 1, Tag: "give");
                    // 大事なもの(鍵・紋章など)は捨てられない
                    yield return new MenuItem(s["inv.drop"], def.Kind == ItemKind.Key ? s["inv.key_item"] : null, def.Kind != ItemKind.Key, "drop");
                }
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.GiveTo:
            case Mode.UseOn:
                foreach (var c in Party)
                    yield return new MenuItem(c.Name, c.IsAlive ? $"{c.Hp}/{c.MaxHp}" : s[$"state.{c.State.ToString().ToLowerInvariant()}"],
                        _mode == Mode.UseOn || c != Owner, c);
                break;
        }
    }

    private void Go(Mode m)
    {
        _mode = m;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (_mode, item.Tag)
        {
            case (Mode.List, "back"): Stack.Pop(); return;
            case (Mode.SlotPick, "back"): BackToList(); return;
            case (Mode.SlotPick, _):
                PickForSlot(item);
                return;
            case (Mode.List, EquipSlot slot):
                _picked = slot;
                OpenSlot(slot);
                return;
            case (Mode.List, ItemInstance):
                _picked = item.Tag;
                Go(Mode.Actions);
                return;
            case (Mode.Actions, "back"): Go(Mode.List); return;
            case (Mode.Actions, "unequip"):
                Message = Result(Inv.Unequip((EquipSlot)_picked!, Data.Items));
                Go(Mode.List);
                return;
            case (Mode.Actions, "equip"):
            {
                var it = (ItemInstance)_picked!;
                var def = Data.Items[it.ItemId];
                var slot = def.Kind == ItemKind.Weapon && Inv[EquipSlot.MainHand] != null && def.Has("light") && Inv[EquipSlot.OffHand] == null
                    ? EquipSlot.OffHand : Inv.SlotFor(def);
                Message = slot is { } sl ? Result(Inv.Equip(Inv.Items.IndexOf(it), sl, Data.Items)) : s["inv.cannot_equip"];
                if (def.Cursed && Inv.Equipped.ContainsValue(it)) Message = s.Format("inv.cursed_now", ItemText.Name(Services, it));
                Go(Mode.List);
                return;
            }
            case (Mode.Actions, "use"):
            {
                var it = (ItemInstance)_picked!;
                var def = Data.Items[it.ItemId];
                var spell = def.UseSpell != null ? Data.Spells.GetValueOrDefault(def.UseSpell) : null;
                bool targetsAlly = spell?.Target == SpellTarget.Ally || (spell == null && def.UseTarget is null or "ally");
                if (spell != null && !spell.InCamp)
                {
                    Message = s["inv.combat_only"];
                    Go(Mode.List);
                    return;
                }
                if (targetsAlly) Go(Mode.UseOn);
                else Use(null);
                return;
            }
            case (Mode.Actions, "give"): Go(Mode.GiveTo); return;
            case (Mode.Actions, "drop"):
            {
                var it = (ItemInstance)_picked!;
                Stack.Push(new ConfirmScene(s.Format("inv.confirm_drop", ItemText.Name(Services, it)), () =>
                {
                    Inv.Items.Remove(it);
                    Go(Mode.List);
                }));
                return;
            }
            case (Mode.GiveTo, Character to):
            {
                var it = (ItemInstance)_picked!;
                // 重ねた品は、渡す個数を小窓で選ぶ
                if (it.Count > 1)
                    Stack.Push(new CountScene(Services.Strings.Format("inv.give_count", ItemText.Name(Services, it, count: false), to.Name), it.Count,
                        n => Give(to, it, n)));
                else Give(to, it, 1);
                return;
            }
            case (Mode.UseOn, Character target):
                Use(target);
                return;
        }
    }

    /// <summary>n 個を渡す(追加効果なども、そのまま渡る)。相手の袋に入らなければ渡さない。</summary>
    private void Give(Character to, ItemInstance it, int n)
    {
        var s = Services.Strings;
        var part = it.SplitOne();
        part.Count = n;
        // 相手の袋に入りきらなければ、入るぶんだけ渡す
        int fit = Math.Min(n, to.Inventory.Room(part, Data.Items));
        part.Count = fit;
        if (fit > 0 && to.Inventory.TryAdd(part, Data.Items))
        {
            Message = fit < n ? s.Format("inv.given_some", ItemText.Name(Services, part), to.Name, fit)
                : s.Format("inv.given", ItemText.Name(Services, part), to.Name);
            it.Count -= fit;
            if (it.Count <= 0) Inv.Items.Remove(it);
        }
        else Message = s.Format("inv.bag_full", to.Name);
        Go(Mode.List);
    }

    /// <summary>袋の中のアイテムを使う(敵のいない戦闘の状態を作って、戦闘と同じ処理を使う)。</summary>
    private void Use(Character? target)
    {
        var it = (ItemInstance)_picked!;
        var party = Party;
        var state = new CombatState { Party = party.Select(c => new PartyMember(c, Data)).ToList(), Groups = [] };
        foreach (var p in state.Party) Lasting.Load(p);
        var me = state.Party.FirstOrDefault(p => p.Character == Owner) ?? new PartyMember(Owner, Data);
        var engine = new CombatEngine(Data, Services.Session!.Rng[RngStreams.Names.Combat]);
        engine.UseItem(state, me, new PlayerCommand(CommandKind.Item, -1, target == null ? -1 : party.IndexOf(target), ItemIndex: Inv.Items.IndexOf(it)));
        foreach (var p in state.Party) Lasting.Save(p);
        Message = string.Join(" ", state.Log.Skip(1).Select(e => CombatText.Format(Services, e)).TakeLast(2));
        Services.Audio.PlaySe(Sounds.Heal);
        Go(Mode.List);
    }

    /// <summary>袋の中の、その部位に付けられる品。</summary>
    private IEnumerable<ItemInstance> Candidates(EquipSlot slot) =>
        Inv.Items.Where(i => Data.Items.TryGetValue(i.ItemId, out var d) && Inventory.Fits(d, slot));

    /// <summary>部位の付け替えを開く(左の一覧を、その部位の候補にする。右は説明だけ)。</summary>
    private void OpenSlot(EquipSlot slot)
    {
        _mode = Mode.SlotPick;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    /// <summary>部位の候補から選んだもの: 外すか、その品に替える。</summary>
    private void PickForSlot(MenuItem item)
    {
        var s = Services.Strings;
        var slot = (EquipSlot)_picked!;
        if (item.Tag is "unequip") Message = Result(Inv.Unequip(slot, Data.Items));
        else if (item.Tag is ItemInstance it)
        {
            Message = Result(Inv.Equip(Inv.Items.IndexOf(it), slot, Data.Items));
            if (Data.Items[it.ItemId].Cursed && Inv.Equipped.ContainsValue(it)) Message = s.Format("inv.cursed_now", ItemText.Name(Services, it));
        }
        BackToList();
    }

    /// <summary>一覧へ戻る(付け替えから戻ったときは、その部位の行に)。</summary>
    private void BackToList()
    {
        _mode = Mode.List;
        var items = Items().ToList();
        int at = _picked is EquipSlot slot ? items.FindIndex(i => i.Tag is EquipSlot e && e == slot) : -1;
        List.SetItems(items, at >= 0 ? at : null);
    }

    private void SwitchTab(Tab t)
    {
        if (_tab == t) return;
        _tab = t;
        Message = null;
        Services.Audio.PlaySe(Sounds.Cursor);
        _mode = Mode.List;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    public override void Update(GameTime time, InputState input)
    {
        if (_mode == Mode.List && _switch.Update(input, Services.Audio))
        {
            // 仲間を切り替えた: 同じタブの一覧の頭から
            Message = null;
            Inv.MergeStacks(Data.Items);
            var items = Items().ToList();
            List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
            return;
        }
        if (_mode == Mode.List)
        {
            // 装備と袋の切り替え(左右キーか、上のボタンをクリック)
            if (input.Repeat(GameAction.Left)) SwitchTab(Tab.Equip);
            else if (input.Repeat(GameAction.Right)) SwitchTab(Tab.Bag);
            else if (input.LeftClicked && input.Mouse is { } m)
            {
                foreach (var t in Enum.GetValues<Tab>())
                {
                    if (TabRect(t).Contains(m))
                    {
                        SwitchTab(t);
                        return;
                    }
                }
            }
        }
        base.Update(time, input);
    }

    private string Result(EquipResult r) => Services.Strings["inv.result." + r.ToString().ToLowerInvariant()];

    protected override void Cancel()
    {
        if (_mode == Mode.List) Stack.Pop();
        else if (_mode == Mode.SlotPick) BackToList();
        else if (_mode == Mode.Actions) Go(Mode.List);
        else Go(Mode.Actions);
    }

    public override void Draw(Draw2D d)
    {
        base.Draw(d);
        var s = Services.Strings;
        foreach (var t in Enum.GetValues<Tab>())
        {
            var r = TabRect(t);
            bool on = t == _tab;
            d.FillRect(r, on ? Theme.SelectedFill : Theme.PanelFill);
            d.Rect(r, on ? Theme.SelectedFrame : Theme.Frame, 1.5f);
            string label = t == Tab.Equip ? s["inv.tab_equip"] : s.Format("inv.tab_bag", Inv.Items.Count, Inv.Capacity(Data.Items));
            d.Text(label, new Vector2(r.X + r.Width / 2, r.Y + 6), 20, on ? Color.White : Theme.Dim, TextAlign.Center);
        }
        // タブの右、左の欄の右端にそろえて AC と同調の数(袋の数はタブに出ている)
        d.Text(s.Format("inv.summary_top", Owner.ArmorClass(Data), Inv.AttunedCount, Inventory.MaxAttuned),
            new Vector2(ListArea.X + ListArea.Width + 4, TabRect(Tab.Equip).Y + 7), 18, Theme.Dim, TextAlign.Right);
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        var it = List.Current?.Tag switch
        {
            EquipSlot slot => Inv[slot],
            ItemInstance i => i,
            // 付け替えで、外すや部位の行を指しているときは、今付けているもの
            _ when _mode == Mode.SlotPick && _picked is EquipSlot ps2 => Inv[ps2],
            _ => _picked as ItemInstance ?? (_picked is EquipSlot ps ? Inv[ps] : null),
        };
        if (it != null)
        {
            y = Line(d, area, y, ItemText.Name(Services, it), Theme.Title, 24);
            foreach (var line in ItemText.Lines(Services, it)) y = Line(d, area, y, line, Theme.Text, 18);
            if (ItemText.ProficiencyNote(Services, it, Owner) is { } pn) y = Line(d, area, y + 4, pn, Theme.Caution, 18);   // この人に習熟が無ければ
        }
    }
}

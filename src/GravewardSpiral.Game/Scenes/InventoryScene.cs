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
/// 町とキャンプから開く(戦闘の中のアイテムは、戦闘のコマンドで使う)。
/// </summary>
public sealed class InventoryScene(Character owner) : MenuScene
{
    private enum Mode { List, Actions, GiveTo, GiveCount, UseOn, SlotPick }

    /// <summary>部位を選んだときの、右の欄の候補(外す・付けられる品)。</summary>
    private readonly MenuList _slotMenu = new(new RectangleF(InfoArea.X + 20, InfoArea.Y + 64, InfoArea.Width - 40, 190), 36f, 21f);

    private Mode _mode;

    private enum Tab { Equip, Bag }

    private Tab _tab;

    private static RectangleF TabRect(Tab t) => new(ListArea.X - 12 + (int)t * 190, 90, 180, 32);
    private object? _picked;   // 選んだもの(EquipSlot か、袋の中の ItemInstance)
    private Character? _giveTo;   // 重ねた品を渡す相手(個数を選ぶあいだ)

    private Inventory Inv => owner.Inventory;
    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();

    protected override string Title => Services.Strings.Format("inv.title", owner.Name);

    protected override int? PartyHighlight => _mode is Mode.GiveTo or Mode.UseOn ? List.Cursor : null;

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
            case Mode.Actions:
                if (_picked is EquipSlot)
                {
                    yield return new MenuItem(s["inv.unequip"], Tag: "unequip");
                }
                else if (_picked is ItemInstance item && Data.Items.TryGetValue(item.ItemId, out var def))
                {
                    if (def.IsEquippable) yield return new MenuItem(s["inv.equip"], Tag: "equip");
                    if (def.IsUsable) yield return new MenuItem(s["inv.use"], Tag: "use");
                    yield return new MenuItem(s["inv.give"], Enabled: Party.Count > 1, Tag: "give");
                    // 大事なもの(鍵・紋章など)は捨てられない
                    yield return new MenuItem(s["inv.drop"], def.Kind == ItemKind.Key ? s["inv.key_item"] : null, def.Kind != ItemKind.Key, "drop");
                }
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.GiveCount when _picked is ItemInstance stack:
                for (int n = 1; n <= Math.Min(stack.Count, 10); n++) yield return new MenuItem(s.Format("inv.give_n", n), Tag: n);
                if (stack.Count > 10) yield return new MenuItem(s.Format("inv.give_all", stack.Count), Tag: stack.Count);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.GiveTo:
            case Mode.UseOn:
                foreach (var c in Party)
                    yield return new MenuItem(c.Name, c.IsAlive ? $"{c.Hp}/{c.MaxHp}" : s[$"state.{c.State.ToString().ToLowerInvariant()}"],
                        _mode == Mode.UseOn || c != owner, c);
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
                if (it.Count > 1)
                {
                    _giveTo = to;
                    Go(Mode.GiveCount);
                    return;
                }
                Give(to, it, 1);
                return;
            }
            case (Mode.GiveCount, "back"): Go(Mode.GiveTo); return;
            case (Mode.GiveCount, int n):
                Give(_giveTo!, (ItemInstance)_picked!, n);
                return;
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
        if (to.Inventory.TryAdd(part, Data.Items))
        {
            Message = s.Format("inv.given", ItemText.Name(Services, part), to.Name);
            it.Count -= n;
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
        var me = state.Party.FirstOrDefault(p => p.Character == owner) ?? new PartyMember(owner, Data);
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

    private void OpenSlot(EquipSlot slot)
    {
        var s = Services.Strings;
        var items = new List<MenuItem>();
        if (Inv[slot] != null) items.Add(new MenuItem(s["inv.unequip"], Tag: "unequip"));
        items.AddRange(Candidates(slot).Select(i => new MenuItem(ItemText.Name(Services, i), ItemText.Hands(Services, i), Tag: i)));
        _slotMenu.SetItems(items, 0);
        _mode = Mode.SlotPick;
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

    /// <summary>一覧へ戻る(カーソルは今の行のまま)。</summary>
    private void BackToList()
    {
        _mode = Mode.List;
        Refresh();
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
        if (_mode != Mode.SlotPick)
        {
            base.Update(time, input);
            return;
        }
        if (_slotMenu.Update(input, Services.Audio) is int i) PickForSlot(_slotMenu.Items[i]);
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            BackToList();
        }
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
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        if (_mode == Mode.SlotPick && _picked is EquipSlot ps2)
        {
            // 右の欄: 部位の候補と、指している品の説明
            Line(d, area, y, s.Format("inv.slot_pick", s["slot." + ps2.ToString().ToLowerInvariant()]), Theme.Title, 22);
            _slotMenu.Draw(d);
            float yy = _slotMenu.Area.Y + Math.Min(_slotMenu.Items.Count, _slotMenu.VisibleRows) * 36f + 8;
            var cur = _slotMenu.Current?.Tag as ItemInstance ?? Inv[ps2];
            if (cur != null)
                foreach (var line in ItemText.Lines(Services, cur)) yy = Line(d, area, yy, line, Theme.Dim, 17);
            return;
        }
        var it = List.Current?.Tag switch
        {
            EquipSlot slot => Inv[slot],
            ItemInstance i => i,
            _ => _picked as ItemInstance ?? (_picked is EquipSlot ps ? Inv[ps] : null),
        };
        if (it != null)
        {
            y = Line(d, area, y, ItemText.Name(Services, it), Theme.Title, 24);
            foreach (var line in ItemText.Lines(Services, it)) y = Line(d, area, y, line, Theme.Text, 18);
        }
        Line(d, area, Math.Max(y + 10, area.Y + area.Height - 110), s.Format("inv.summary",
            owner.ArmorClass(Data), Inv.Items.Count, Inv.Capacity(Data.Items), Inv.AttunedCount, Inventory.MaxAttuned), Theme.Dim, 17);
    }
}

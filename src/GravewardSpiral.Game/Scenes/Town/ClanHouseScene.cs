using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// クランハウス。倉庫に品を預ける・引き出す、金を投資して次の段階にする。
/// 倉庫はクランで共有で、クランファイルと一緒に持ち歩く(シナリオが変わっても残る)。
/// </summary>
public sealed class ClanHouseScene : MenuScene
{
    private enum Mode { Main, DepositWho, Deposit, Withdraw, WithdrawTo }

    private Mode _mode;
    private Character? _who;
    private ItemInstance? _taking;
    private List<ClanHouseLevel> _levels = [];

    private ClanHouse House => Services.Session!.ClanHouse;
    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();

    protected override string Title => House.Def(_levels) is { } def ? Services.Name(def.Name) : Services.Strings["town.house"];

    protected override int? PartyHighlight => _mode is Mode.DepositWho or Mode.WithdrawTo ? List.Cursor : null;

    public override void Enter()
    {
        _levels = ClanHouseLevel.Load(Services.Content);
        base.Enter();
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
            {
                yield return new MenuItem(s["town.items"], Enabled: Party.Count > 0, Tag: "items");
                yield return new MenuItem(s["camp.status"], Enabled: Party.Count > 0, Tag: "status");
                if (House.Has(_levels, ClanHouseLevel.Bed))
                    yield return new MenuItem(s["house.sleep"], s[House.Has(_levels, ClanHouseLevel.BedFull) ? "house.sleep_full" : "house.sleep_half"], Tag: "sleep");
                yield return new MenuItem(s["house.deposit"], Enabled: Party.Any(c => c.Inventory.Items.Count > 0), Tag: Mode.DepositWho);
                yield return new MenuItem(s["house.withdraw"], s.Format("house.slots", House.Storage.Count, House.Capacity(_levels)),
                    House.Storage.Count > 0, Mode.Withdraw);
                var next = House.Next(_levels);
                if (House.ExpandCost(_levels) is long expand)
                    yield return new MenuItem(s["house.expand"], s.Format("shop.price", expand), PartyGold.Total(Party) >= expand, "expand");
                else
                    yield return new MenuItem(s["house.upgrade"], next == null ? s["house.max"] : s.Format("shop.price", next.Cost),
                        next != null && PartyGold.Total(Party) >= next.Cost && next.Materials.All(m => House.Count(Party, m.Key) >= m.Value), "upgrade");
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            }
            case Mode.DepositWho:
            case Mode.WithdrawTo:
                foreach (var c in Party)
                {
                    bool ok = _mode == Mode.DepositWho ? c.Inventory.Items.Count > 0 : c.Inventory.FreeSlots(Data.Items) > 0
                        || _taking != null && Data.Items[_taking.ItemId].Stack > 1 && c.Inventory.Items.Any(i => i.SameKind(_taking));
                    yield return new MenuItem(c.Name, s.Format("shop.slots", c.Inventory.Items.Count, c.Inventory.Capacity(Data.Items)), ok, c);
                }
                break;
            case Mode.Deposit:
                foreach (var it in _who!.Inventory.Items) yield return new MenuItem(ItemText.Name(Services, it), Tag: it);
                break;
            case Mode.Withdraw:
                // 種類ごとに見出しを付けて並べる
                foreach (var group in House.Storage.GroupBy(KindOf).OrderBy(g => Array.IndexOf(KindOrder, g.Key)))
                {
                    yield return new MenuItem($"― {s["house.kind." + group.Key.ToString().ToLowerInvariant()]} ―", Enabled: false);
                    foreach (var it in group.OrderBy(i => ItemText.Name(Services, i), StringComparer.CurrentCulture))
                        yield return new MenuItem("  " + ItemText.Name(Services, it), ItemText.Hands(Services, it), Tag: it);
                }
                break;
        }
    }

    /// <summary>倉庫の並びの順(見出し)。</summary>
    private static readonly ItemKind[] KindOrder =
        [ItemKind.Weapon, ItemKind.Armor, ItemKind.Shield, ItemKind.Accessory, ItemKind.Backpack, ItemKind.Ammo, ItemKind.Consumable, ItemKind.Gear, ItemKind.Key];

    private ItemKind KindOf(ItemInstance it) => Data.Items.TryGetValue(it.ItemId, out var d) ? d.Kind : ItemKind.Gear;

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
            case (Mode.Main, "back"): Stack.Pop(); return;
            case (Mode.Main, "items"):
                // 持ち物の整理(装備の付け替え、仲間に渡す)
                Stack.Push(new MemberPickScene(Services.Strings["town.items"], _ => true, c => Stack.Push(new InventoryScene(c))));
                return;
            case (Mode.Main, "status"):
                // 仲間の詳しい状態(訓練場やキャンプと同じ画面)
                Stack.Push(new MemberPickScene(s["camp.status"], _ => true, c => Stack.Push(new StatusScene(c))));
                return;
            case (Mode.Main, Mode m): Go(m); return;
            case (Mode.Main, "sleep"):
            {
                // 寝床: ただで大休憩(段階で戻る HP が違う)
                double hp = House.Has(_levels, ClanHouseLevel.BedFull) ? 1.0 : 0.7;   // 宿屋の馬小屋(半分)よりは休まる
                foreach (var c in Party.Where(c => c.IsAlive)) c.LongRest(hp);
                Services.Audio.PlaySe(Sounds.InnRest);
                Message = s["inn.rested"];
                Refresh();
                return;
            }
            case (Mode.Main, "expand"):
                if (House.Expand(Party, _levels))
                {
                    Services.Audio.PlaySe(Sounds.Coins);
                    Message = s.Format("house.expanded", House.Capacity(_levels));
                }
                Refresh();
                return;
            case (Mode.Main, "upgrade"):
                if (House.Upgrade(Party, _levels))
                {
                    Services.Audio.PlaySe(Sounds.Coins);
                    Message = s.Format("house.upgraded", Services.Name(House.Def(_levels)!.Name));
                }
                Refresh();
                return;
            case (Mode.DepositWho, Character c):
                _who = c;
                Go(Mode.Deposit);
                return;
            case (Mode.Deposit, ItemInstance it):
                if (House.Deposit(it, Data.Items, _levels))
                {
                    _who!.Inventory.Items.Remove(it);
                    Message = s.Format("house.deposited", ItemText.Name(Services, it));
                }
                else Message = s["house.full"];
                if (_who!.Inventory.Items.Count > 0) Go(Mode.Deposit);
                else Go(Mode.Main);
                return;
            case (Mode.Withdraw, ItemInstance it):
                _taking = it;
                Go(Mode.WithdrawTo);
                return;
            case (Mode.WithdrawTo, Character c):
                var taking = _taking!;
                if (c.Inventory.TryAdd(taking, Data.Items))
                {
                    House.Storage.Remove(taking);
                    Message = s.Format("house.withdrawn", ItemText.Name(Services, taking), c.Name);
                }
                else Message = s.Format("inv.bag_full", c.Name);
                Go(House.Storage.Count > 0 ? Mode.Withdraw : Mode.Main);
                return;
        }
    }

    protected override void Cancel()
    {
        switch (_mode)
        {
            case Mode.Main: Stack.Pop(); break;
            case Mode.Deposit: Go(Mode.DepositWho); break;
            case Mode.WithdrawTo: Go(Mode.Withdraw); break;
            default: Go(Mode.Main); break;
        }
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        if (List.Current?.Tag is ItemInstance it)
        {
            y = Line(d, area, y, ItemText.Name(Services, it), Theme.Title, 24);
            foreach (var line in ItemText.Lines(Services, it)) y = Line(d, area, y, line, Theme.Text, 18);
            return;
        }
        if (House.Def(_levels) is { } def)
        {
            y = Line(d, area, y, Services.Name(def.Name), Theme.Title, 24);
            if (def.Description != null) y = Line(d, area, y, Services.Name(def.Description), Theme.Text, 18);
            // 使える設備と、次の段階で増えるもの
            var have = def.Facilities.Select(f => s["house.facility." + f]).ToList();
            if (have.Count > 0) y = Line(d, area, y + 6, s.Format("house.facilities", s.List(have)), Theme.Gold, 17);
            if (List.Current?.Tag is "upgrade" && House.Next(_levels) is { } next)
            {
                var add = next.Facilities.Except(def.Facilities).Select(f => s["house.facility." + f]).ToList();
                y = Line(d, area, y + 6, s.Format("house.next", Services.Name(next.Name), next.StorageSlots), Theme.Text, 17);
                if (add.Count > 0) y = Line(d, area, y, s.Format("house.next_adds", s.List(add)), Theme.Text, 17);
                // 要る素材と、今あるぶん(持ち物と倉庫)
                foreach (var (id, n) in next.Materials)
                {
                    int owned = House.Count(Party, id);
                    y = Line(d, area, y, s.Format("house.material", Data.Items.TryGetValue(id, out var md) ? Services.Name(md.Name) : id, owned, n),
                        owned >= n ? Theme.Good : Theme.Gold, 17);
                }
            }
            if (List.Current?.Tag is "expand" && def.ExpandSlots > 0)
                y = Line(d, area, y + 6, s.Format("house.expand_info", def.ExpandSlots), Theme.Text, 17);
        }
        Line(d, area, y + 10, s.Format("house.slots_info", House.Storage.Count, House.Capacity(_levels)), Theme.Dim, 17);
    }
}

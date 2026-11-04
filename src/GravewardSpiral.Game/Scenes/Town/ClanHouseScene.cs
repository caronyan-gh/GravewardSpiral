using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// クランハウス。持ち物の整理、仲間の管理(パーティを組む・状態)、倉庫に品を預ける・引き出す、金を投資して次の段階にする。
/// 倉庫はクランで共有で、クランファイルと一緒に持ち歩く(シナリオが変わっても残る)。
/// </summary>
public sealed class ClanHouseScene(Action? onLeave = null) : MenuScene
{
    private enum Mode { Main, Storage, DepositWho, Deposit, Withdraw, WithdrawTo }

    private Mode _mode;
    private Character? _who;
    private ItemInstance? _taking;
    private List<ClanHouseLevel> _levels = [];

    private ClanHouse House => Services.Session!.ClanHouse;
    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();

    protected override string Title => _mode == Mode.Deposit && _who != null ? Services.Strings.Format("inv.title", _who.Name)   // 誰の品を預けるか(左右で切り替わる)
        : House.Def(_levels) is { } def ? Services.Name(def.Name) : Services.Strings["town.house"];

    protected override int? PartyHighlight => _mode is Mode.DepositWho or Mode.WithdrawTo ? (List.Cursor < Party.Count ? List.Cursor : null)
        : _mode == Mode.Deposit && _who != null && Party.IndexOf(_who) is >= 0 and var i ? i : null;

    /// <summary>預ける品を選ぶあいだの、人の切り替え(持ち物のある人だけ。左右キーでも回す)。</summary>
    private MemberSwitch? _depositSwitch;

    protected override MemberSwitch? Switch => _mode == Mode.Deposit ? _depositSwitch : null;

    /// <summary>上に重ねた画面(荷物の整理など)から戻ったか見るための、画面の重なりの版。</summary>
    private int _version = -1;

    public override void Update(GameTime time, InputState input)
    {
        // 荷物の整理などから戻ったら、倉庫の数や選べるかを作り直す
        if (_version != Stack.Version)
        {
            if (_version >= 0 && _mode == Mode.Main) Refresh();
            _version = Stack.Version;
        }
        if (_mode == Mode.Deposit && _depositSwitch is { } sw
            && (sw.Update(input, Services.Audio) || input.Repeat(GameAction.Left) && sw.Step(-1, Services.Audio) || input.Repeat(GameAction.Right) && sw.Step(1, Services.Audio)))
        {
            // 人を切り替えた: その人の品の頭から
            Message = null;
            _who = sw.Current;
            Go(Mode.Deposit);
            return;
        }
        base.Update(time, input);
    }

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
                yield return new MenuItem(s["house.party"], Tag: "party");
                yield return new MenuItem(s["house.items"], Enabled: Party.Count > 0, Tag: "items");
                if (House.Has(_levels, ClanHouseLevel.Bed))
                    yield return House.Has(_levels, ClanHouseLevel.BedFull)
                        ? new MenuItem(s["house.sleep_good"], s["house.sleep_full"], Tag: "sleep")
                        : new MenuItem(s["house.sleep"], s["house.sleep_half"], Tag: "sleep");
                yield return new MenuItem(s["house.storage"], s.Format("house.slots", House.Used(Data.Items), House.Capacity(_levels)), Tag: Mode.Storage);
                var next = House.Next(_levels);
                if (House.ExpandCost(_levels) is long expand)
                    yield return new MenuItem(s["house.expand"], s.Format("shop.price", expand), PartyGold.Total(Party) >= expand, "expand");
                else
                    yield return new MenuItem(s["house.upgrade"], next == null ? s["house.max"] : s.Format("shop.price", next.Cost),
                        next != null && PartyGold.Total(Party) >= next.Cost && next.Materials.All(m => House.Count(Party, m.Key) >= m.Value), "upgrade");
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            }
            case Mode.Storage:
                // 倉庫: ふつうの品と大事なもの(枠を使わない)を分けて、しまう・取り出す
                yield return new MenuItem(s["house.deposit"], Enabled: Party.Any(c => Depositable(c, false).Any()), Tag: (Mode.DepositWho, false));
                yield return new MenuItem(s["house.withdraw"], s.Format("house.slots", House.Used(Data.Items), House.Capacity(_levels)),
                    Stored(false).Any(), (Mode.Withdraw, false));
                yield return new MenuItem(s["house.deposit_key"], Enabled: Party.Any(c => Depositable(c, true).Any()), Tag: (Mode.DepositWho, true));
                yield return new MenuItem(s["house.withdraw_key"], Enabled: Stored(true).Any(), Tag: (Mode.Withdraw, true));
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.DepositWho:
            case Mode.WithdrawTo:
                foreach (var c in Party)
                {
                    bool ok = _mode == Mode.DepositWho ? Depositable(c).Any() : c.Inventory.FreeSlots(Data.Items) > 0
                        || _taking != null && Data.Items[_taking.ItemId].Stack > 1 && c.Inventory.Items.Any(i => i.SameKind(_taking));
                    yield return new MenuItem(c.Name, s.Format("shop.slots", c.Inventory.Items.Count, c.Inventory.Capacity(Data.Items)), ok, c);
                }
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.Deposit:
                foreach (var it in Depositable(_who!)) yield return new MenuItem(ItemText.Name(Services, it), Tag: it);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.Withdraw when _keys:
                foreach (var it in Stored(true).OrderBy(i => ItemText.Name(Services, i), StringComparer.CurrentCulture))
                    yield return new MenuItem(ItemText.Name(Services, it), Tag: it);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.Withdraw:
                // 種類ごとに見出しを付けて並べる
                foreach (var group in Stored(false).GroupBy(KindOf).OrderBy(g => Array.IndexOf(KindOrder, g.Key)))
                {
                    yield return new MenuItem($"― {s["house.kind." + group.Key.ToString().ToLowerInvariant()]} ―", Enabled: false);
                    foreach (var it in group.OrderBy(i => ItemText.Name(Services, i), StringComparer.CurrentCulture))
                        yield return new MenuItem("  " + ItemText.Name(Services, it), ItemText.Hands(Services, it), Tag: it);
                }
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
        }
    }

    /// <summary>大事なものを扱っているか(倉庫の「大事なものをしまう・取り出す」)。</summary>
    private bool _keys;

    /// <summary>その人の袋の品のうち、今しまえるもの(大事なものか、ふつうの品か)。</summary>
    private IEnumerable<ItemInstance> Depositable(Character c) => Depositable(c, _keys);

    private IEnumerable<ItemInstance> Depositable(Character c, bool keys) => c.Inventory.Items.Where(i => ClanHouse.IsKey(i, Data.Items) == keys);

    private IEnumerable<ItemInstance> Stored(bool keys) => House.Storage.Where(i => ClanHouse.IsKey(i, Data.Items) == keys);

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
            case (_, "back"): Cancel(); return;   // どの一覧でも、戻るは取消と同じ
            case (Mode.Main, "items"):
                // 荷物の整理(装備の付け替え、仲間に渡す)。人の次に倉庫のページがあり、渡すで預ける・引き出す
                Stack.Push(new MemberPickScene(s["house.items"], _ => true, c => Stack.Push(new InventoryScene(c, Party, storage: true)))
                {
                    // 人の行の右に袋の埋まり具合、倉庫の行の右に倉庫の埋まり具合
                    Detail = c => s.Format("shop.slots", c.Inventory.Items.Count, c.Inventory.Capacity(Data.Items)),
                    ExtraDetail = () => s.Format("house.slots", House.Used(Data.Items), House.Capacity(_levels)),
                    // 戻るの前に倉庫(選ぶと倉庫のページから開く)
                    Extra = (s["house.storage"], () => s.Format("house.slots_info", House.Used(Data.Items), House.Capacity(_levels)),
                        () => Stack.Push(new InventoryScene(Party[0], Party, storage: true, atStorage: true))),
                });
                return;
            case (Mode.Main, "party"):
                // 仲間の管理(パーティを組む・状態)
                Stack.Push(new PartyScene(Refresh));
                return;
            case (Mode.Main, Mode m): Go(m); return;
            case (Mode.Storage, (Mode m, bool keys)):
                _keys = keys;
                Go(m);
                return;
            case (Mode.Main, "sleep"):
            {
                // 寝床: ただで大休憩(段階で戻る HP が違う)
                double hp = House.Has(_levels, ClanHouseLevel.BedFull) ? 1.0 : 0.7;   // 宿屋の馬小屋(半分)よりは休まる
                foreach (var c in Party.Where(c => c.IsAlive)) c.LongRest(hp, Data);
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
                _depositSwitch = new MemberSwitch(Party, c, x => Depositable(x).Any()) { PartyArea = PartyArea };
                Go(Mode.Deposit);
                return;
            case (Mode.Deposit, ItemInstance it):
                if (House.Deposit(it, Data.Items, _levels))
                {
                    _who!.Inventory.Items.Remove(it);
                    Message = s.Format("house.deposited", ItemText.Name(Services, it));
                }
                else Message = s["house.full"];
                if (Depositable(_who!).Any()) Go(Mode.Deposit);
                else Go(Mode.Storage);
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
                Go(Stored(_keys).Any() ? Mode.Withdraw : Mode.Storage);
                return;
        }
    }

    protected override void Cancel()
    {
        switch (_mode)
        {
            case Mode.Main:
                Stack.Pop();
                onLeave?.Invoke();
                break;
            case Mode.Deposit: Go(Mode.DepositWho); break;
            case Mode.WithdrawTo: Go(Mode.Withdraw); break;
            case Mode.DepositWho or Mode.Withdraw: Go(Mode.Storage); break;
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
            y = Lines(d, area, y, ItemText.Lines(Services, it), Theme.Text, 18, reserve: 24);
            return;
        }
        if (House.Def(_levels) is { } def)
        {
            y = Line(d, area, y, Services.Name(def.Name), Theme.Title, 24);
            if (def.Description != null) y = Line(d, area, y, Services.Name(def.Description), Theme.Text, 18);
            // 使える設備と、次の段階で増えるもの
            // 良い寝床があれば、ふつうの寝床は選べないので並べない
            var have = def.Facilities.Where(f => f != ClanHouseLevel.Bed || !def.Facilities.Contains(ClanHouseLevel.BedFull))
                .Select(f => s["house.facility." + f]).ToList();
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
        Line(d, area, y + 10, s.Format("house.slots_info", House.Used(Data.Items), House.Capacity(_levels)), Theme.Dim, 17);
    }
}

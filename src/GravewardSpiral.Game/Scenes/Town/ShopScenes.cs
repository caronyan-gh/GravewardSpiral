using GravewardSpiral.Core.Save;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// 商店。買う(いつもの品と、売られた品)、売る(半額)、鑑定、呪いを解く。金はパーティの金から払い、売った金はパーティで分ける。
/// </summary>
public sealed class ShopScene : MenuScene
{
    /// <summary>クランハウスの設備で安くなる値段(書庫で鑑定、礼拝堂で呪い解きが半額)。</summary>
    private long House(string facility, long price) =>
        Services.Session!.ClanHouse.Price(ClanHouseLevel.Load(Services.Content), facility, price);

    /// <summary>値段は見える姿で付ける(呪いがまだ分からない品は、見せかけの品の値段)。</summary>
    private long Shown(Func<Core.Items.ItemDef, Core.Items.ItemInstance, long> price, Core.Items.ItemInstance it)
    {
        var a = it.Apparent(Data.Items);
        return price(Data.Items[a.ItemId], a);
    }

    private enum Mode { Main, Buy, BuyFor, SellWho, Sell, IdentifyWho, Identify, CurseWho }

    private Mode _mode;
    private Character? _who;
    private (ItemDef Def, ItemInstance Item, bool Sold)? _buying;

    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();
    private List<ItemInstance> Sold => Services.Session!.ShopSold;
    private long Gold => PartyGold.Total(Party);

    protected override string Title => Services.Strings["town.shop"];

    protected override int? PartyHighlight => _mode is Mode.BuyFor or Mode.SellWho or Mode.IdentifyWho or Mode.CurseWho ? List.Cursor : null;

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["shop.buy"], Tag: Mode.Buy);
                yield return new MenuItem(s["shop.sell"], Tag: Mode.SellWho);
                yield return new MenuItem(s["shop.identify"], Tag: Mode.IdentifyWho);
                yield return new MenuItem(s["shop.uncurse"], s.Format("shop.price", House(ClanHouseLevel.Chapel, Shop.RemoveCursePrice)), Tag: Mode.CurseWho);
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.Buy:
                foreach (var it in Sold)
                {
                    var def = Data.Items[it.ItemId];
                    long p = Shown(Shop.BuyPrice, it);
                    yield return new MenuItem("★ " + ItemText.Name(Services, it), s.Format("shop.price", p), p <= Gold, (def, it, true));
                }
                foreach (var def in Shop.Standard(Data))
                {
                    var it = ItemInstance.Of(def.Id, Math.Max(1, def.Bundle));   // 弾は束(20本)で売る
                    long p = Shown(Shop.BuyPrice, it);
                    yield return new MenuItem(Services.Name(def.Name), s.Format("shop.price", p), p <= Gold, (def, it, false));
                }
                break;
            case Mode.Sell:
                foreach (var it in _who!.Inventory.Items)
                {
                    // 買い戻しの倍率の無い大事なもの(鍵など)は売れない
                    if (!Shop.CanSell(Data.Items[it.ItemId])) yield return new MenuItem(ItemText.Name(Services, it), s["inv.key_item"], false, it);
                    else yield return new MenuItem(ItemText.Name(Services, it), s.Format("shop.price", Shown(Shop.SellPrice, it)), Tag: it);
                }
                break;
            case Mode.Identify:
                foreach (var it in _who!.Inventory.Items.Concat(_who.Inventory.Equipped.Values).Where(i => !i.Identified))
                {
                    long p = House(ClanHouseLevel.Library, Shown(Shop.IdentifyPrice, it));
                    yield return new MenuItem(ItemText.Name(Services, it), s.Format("shop.price", p), p <= Gold, it);
                }
                break;
            default:
                foreach (var c in Party)
                {
                    bool ok = _mode switch
                    {
                        Mode.BuyFor => c.Inventory.FreeSlots(Data.Items) > 0 || (_buying?.Def.Stack > 1 && c.Inventory.Items.Any(i => i.ItemId == _buying.Value.Def.Id)),
                        Mode.SellWho => c.Inventory.Items.Count > 0,
                        Mode.IdentifyWho => c.Inventory.Items.Concat(c.Inventory.Equipped.Values).Any(i => !i.Identified),
                        Mode.CurseWho => c.Inventory.Equipped.Values.Any(i => Inventory.IsStuck(i, Data.Items)),
                        _ => true,
                    };
                    yield return new MenuItem(c.Name, s.Format("shop.slots", c.Inventory.Items.Count, c.Inventory.Capacity(Data.Items)), ok, c);
                }
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
            case (Mode.Main, "back"): Stack.Pop(); return;
            case (Mode.Main, Mode m): Go(m); return;
            case (Mode.Buy, ValueTuple<ItemDef, ItemInstance, bool> b):
                _buying = b;
                Go(Mode.BuyFor);
                return;
            case (Mode.BuyFor, Character c):
            {
                var (def, it, sold) = _buying!.Value;
                long price = Shown(Shop.BuyPrice, it);
                var copy = it.Copy();
                copy.Identified = true;
                if (!c.Inventory.TryAdd(copy, Data.Items)) Message = s.Format("inv.bag_full", c.Name);
                else if (!PartyGold.Pay(Party, price))
                {
                    c.Inventory.Items.Remove(copy);
                    Message = s["shop.no_money"];
                }
                else
                {
                    if (sold) Sold.Remove(it);
                    Services.Audio.PlaySe(Sounds.Coins);
                    Message = s.Format("shop.bought", c.Name, ItemText.Name(Services, copy));
                }
                Go(Mode.Buy);
                return;
            }
            case (Mode.SellWho, Character c):
                _who = c;
                Go(Mode.Sell);
                return;
            case (Mode.Sell, ItemInstance it):
                // 重ねた品は、売る個数を小窓で選ぶ(値段はその個数の分)
                if (it.Count > 1)
                    Stack.Push(new CountScene(s.Format("shop.sell_count", ItemText.Name(Services, it, count: false)), it.Count,
                        n => Sell(it, n), n => s.Format("shop.price", SellPriceOf(it, n))));
                else Sell(it, 1);
                return;
            case (Mode.IdentifyWho, Character c):
                _who = c;
                Go(Mode.Identify);
                return;
            case (Mode.Identify, ItemInstance it):
                if (PartyGold.Pay(Party, House(ClanHouseLevel.Library, Shown(Shop.IdentifyPrice, it))))
                {
                    it.Identified = true;
                    Services.Audio.PlaySe(Sounds.Coins);
                    Message = s.Format("shop.identified", ItemText.Name(Services, it));
                }
                Go(Mode.Identify);
                return;
            case (Mode.CurseWho, Character c):
                if (PartyGold.Pay(Party, House(ClanHouseLevel.Chapel, Shop.RemoveCursePrice)))
                {
                    c.Inventory.BreakCurses(Data.Items);
                    Message = s.Format("shop.uncursed", c.Name);
                }
                else Message = s["shop.no_money"];
                Go(Mode.Main);
                return;
        }
    }

    /// <summary>重ねた品のうち n 個の売値。</summary>
    private long SellPriceOf(ItemInstance stack, int n)
    {
        var part = stack.SplitOne();
        part.Count = n;
        return Shown(Shop.SellPrice, part);
    }

    /// <summary>n 個を売る(重ねた品は分けて、残りは袋に残す)。売った分は店に並ぶ。</summary>
    private void Sell(ItemInstance it, int n)
    {
        var s = Services.Strings;
        var part = it.Count > n ? it.SplitOne() : it;
        part.Count = n;
        long price = Shown(Shop.SellPrice, part);
        if (part == it) _who!.Inventory.Items.Remove(it);
        else it.Count -= n;
        PartyGold.Share(Party, price);
        Shop.Stock(Sold, part);
        Services.Audio.PlaySe(Sounds.Coins);
        Message = s.Format("shop.sold", ItemText.Name(Services, part), price);
        Go(_who!.Inventory.Items.Count > 0 ? Mode.Sell : Mode.Main);
    }

    protected override void Cancel()
    {
        switch (_mode)
        {
            case Mode.Main: Stack.Pop(); break;
            case Mode.BuyFor: Go(Mode.Buy); break;
            case Mode.Sell: Go(Mode.SellWho); break;
            case Mode.Identify: Go(Mode.IdentifyWho); break;
            default: Go(Mode.Main); break;
        }
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        float y = area.Y + 20;
        ItemInstance? it = List.Current?.Tag switch
        {
            ValueTuple<ItemDef, ItemInstance, bool> b => b.Item2,
            ItemInstance i => i,
            _ => _mode == Mode.BuyFor ? _buying?.Item : null,
        };
        if (it != null)
        {
            y = Line(d, area, y, ItemText.Name(Services, it), Theme.Title, 24);
            foreach (var line in ItemText.Lines(Services, it)) y = Line(d, area, y, line, Theme.Text, 18);
            return;
        }
        if (List.Current?.Tag is Character c)
        {
            StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, y));
            return;
        }
        Line(d, area, y, Services.Strings["town.about_shop"], Theme.Text, 19);
    }
}

/// <summary>寺院: 残っている状態を治す、死んだ人を蘇生する(失敗すると灰、灰からの失敗で消失)。料金はパーティの金から。</summary>
public sealed class TempleScene : MenuScene
{
    /// <summary>礼拝堂があれば、寄進は半分。</summary>
    private long House(string facility, long price) =>
        Services.Session!.ClanHouse.Price(ClanHouseLevel.Load(Services.Content), facility, price);

    protected override string Title => Services.Strings["town.temple"];

    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();

    protected override int? PartyHighlight => List.Current?.Tag is Character ? List.Cursor : null;

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        // 名簿の全員(パーティにいない死者も、ここで蘇生できる)
        foreach (var c in Services.Session!.Roster.Characters)
        {
            if (Services.Session.Roster.Away.ContainsKey(c.Id)) continue;   // ほかの世界の迷宮にいる人は、ここにはいない
            if (c.State is LifeState.Dead or LifeState.Ashes)
                yield return new MenuItem(s.Format("temple.revive", c.Name), s.Format("temple.revive_detail", House(ClanHouseLevel.Chapel, Temple.RevivePrice(c)), Temple.ReviveChance(c, sure: !Services.Session!.Difficulty.RiskyRevive)),
                    House(ClanHouseLevel.Chapel, Temple.RevivePrice(c)) <= PartyGold.Total(Party), c);
            else if (c.IsAlive && c.Lasting.Count > 0)
                yield return new MenuItem(s.Format("temple.cure", c.Name), s.Format("shop.price", Temple.CurePrice), Temple.CurePrice <= PartyGold.Total(Party), c);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        if (item.Tag is not Character c)
        {
            Stack.Pop();
            return;
        }
        if (c.State is LifeState.Dead or LifeState.Ashes)
        {
            if (!PartyGold.Pay(Party, House(ClanHouseLevel.Chapel, Temple.RevivePrice(c)))) return;
            var r = Temple.Revive(c, Services.Session!.Rng[RngStreams.Names.Loot], sure: !Services.Session.Difficulty.RiskyRevive);
            Message = s.Format("temple.result." + r.ToString().ToLowerInvariant(), c.Name);
            Services.Audio.PlaySe(r == Temple.ReviveResult.Revived ? Sounds.Heal : Sounds.PartyDown);
        }
        else if (PartyGold.Pay(Party, Temple.CurePrice))
        {
            Temple.Cure(c);
            Message = s.Format("temple.cured", c.Name);
            Services.Audio.PlaySe(Sounds.Heal);
        }
        Refresh();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        if (List.Current?.Tag is Character c) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
        else Line(d, area, area.Y + 20, Services.Strings["town.about_temple"], Theme.Text, 19);
    }
}

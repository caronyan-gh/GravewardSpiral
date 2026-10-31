using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// クランハウスの「仲間の管理」: パーティを組む(最大6人。前の3人が前列)、状態を見る。
/// 前は酒場で組んでいたが、クランの拠点で組むほうが自然なので、ここへ移した。
/// </summary>
public sealed class PartyScene(Action onLeave) : MenuScene
{
    private enum Mode { Main, Add, Remove, Reorder }

    private Mode _mode;

    /// <summary>新しい隊列(選んだ順)。</summary>
    private readonly List<Character> _newOrder = [];

    private Roster Roster => Services.Session!.Roster;

    protected override string Title => Services.Strings["house.party"];

    protected override int? PartyHighlight => _mode switch
    {
        Mode.Remove => List.Cursor,
        Mode.Reorder when List.Current?.Tag is Character c => Roster.Party.IndexOf(c.Id),
        _ => null,
    };

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["tavern.add"], Enabled: Roster.Party.Count < Roster.MaxParty && Available().Any(), Tag: Mode.Add);
                yield return new MenuItem(s["tavern.remove"], Enabled: Roster.Party.Count > 0, Tag: Mode.Remove);
                yield return new MenuItem(s["tavern.swap"], Enabled: Roster.Party.Count > 1, Tag: Mode.Reorder);
                yield return new MenuItem(s["camp.status"], Enabled: Roster.Party.Count > 0, Tag: "status");
                yield return new MenuItem(s["common.back"], Tag: "back");
                break;
            case Mode.Add:
                foreach (var c in Available()) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
            case Mode.Reorder:
                // まだ選んでいない人だけ(選んだ並びは文で見せる)
                foreach (var c in Roster.PartyMembers.Where(c => !_newOrder.Contains(c))) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
            default:
                foreach (var c in Roster.PartyMembers) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
        }
    }

    /// <summary>パーティが6人なら「仲間に加える」は選べないので、選べる最初の行から始める。</summary>
    public override void Enter()
    {
        base.Enter();
        SetMode(Mode.Main);
    }

    private IEnumerable<Character> Available() =>
        Roster.Characters.Where(c => !Roster.InParty(c) && Roster.Available(c));

    private string Describe(Character c) => $"{Services.Name(Services.Data.Classes[c.ClassId].Name)} Lv{c.Level}";

    private void SetMode(Mode mode)
    {
        _mode = mode;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                switch (item.Tag)
                {
                    case "back": Cancel(); break;
                    case "status":
                        // 仲間の詳しい状態(訓練場やキャンプと同じ画面)
                        Stack.Push(new MemberPickScene(s["camp.status"], _ => true, c => Stack.Push(new StatusScene(c, Roster.PartyMembers.ToList()))));
                        break;
                    case Mode m:
                        if (m == Mode.Reorder)
                        {
                            _newOrder.Clear();
                            Message = OrderMessage();
                        }
                        SetMode(m);
                        break;
                }
                break;
            case Mode.Add:
                Roster.AddToParty((Character)item.Tag!);
                Message = s.Format("tavern.added", item.Label);
                if (Roster.Party.Count >= Roster.MaxParty || !Available().Any()) SetMode(Mode.Main);
                else Refresh();
                break;
            case Mode.Remove:
                Roster.RemoveFromParty((Character)item.Tag!);
                Message = s.Format("tavern.removed", item.Label);
                SetMode(Roster.Party.Count > 0 ? Mode.Remove : Mode.Main);
                break;
            case Mode.Reorder:
            {
                _newOrder.Add((Character)item.Tag!);
                // 残りが1人なら、その人が最後
                var rest = Roster.PartyMembers.Where(x => !_newOrder.Contains(x)).ToList();
                if (rest.Count == 1) _newOrder.Add(rest[0]);
                if (rest.Count <= 1)
                {
                    Roster.Reorder(_newOrder);
                    Message = s["camp.order_done"];
                    SetMode(Mode.Main);
                    break;
                }
                Message = OrderMessage();
                SetMode(Mode.Reorder);
                break;
            }
        }
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
        if (_mode == Mode.Main)
        {
            Stack.Pop();
            onLeave();   // 戻った先の一覧(迷宮に入れるか、倉庫に預けられるか)を作り直す
        }
        else if (_mode == Mode.Reorder && _newOrder.Count > 0)
        {
            _newOrder.RemoveAt(_newOrder.Count - 1);   // 1人戻す
            Message = OrderMessage();
            SetMode(Mode.Reorder);
        }
        else
        {
            Message = null;
            SetMode(Mode.Main);
        }
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        string key = _mode switch
        {
            Mode.Main => List.Current?.Tag switch { "status" => "camp.about_status", _ => "house.about_party" },
            _ => $"tavern.about_{_mode.ToString().ToLowerInvariant()}",
        };
        float y = Line(d, area, area.Y + 20, s[key], Theme.Text, 19);
        if (List.Current?.Tag is Character c) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, y + 10));
    }
}

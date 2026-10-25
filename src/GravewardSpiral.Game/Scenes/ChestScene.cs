using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 宝箱(Wizardry 風)。調べる(誰が調べるかを選ぶ)、罠を外す(誰がやるかと、罠の名前を選ぶ)、そのまま開ける、立ち去る。
/// 罠がテレポーターなら onTeleport、警報なら onAlarm を呼ぶ(迷宮の画面が処理する)。
/// </summary>
public sealed class ChestScene(Chest chest, Action onTeleport, Action onAlarm) : MenuScene
{
    private enum Mode { Main, InspectWho, DisarmWho, DisarmTrap, OpenWho, Done }

    private Mode _mode;
    private Character? _who;
    private readonly List<string> _lines = [];

    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();
    private IRandom Rng => Services.Session!.Rng[RngStreams.Names.Loot];

    protected override string Title => Services.Strings["chest.title"];

    protected override int? PartyHighlight => _mode is Mode.InspectWho or Mode.DisarmWho or Mode.OpenWho ? List.Cursor : null;

    public override void Enter()
    {
        base.Enter();
        Say(Services.Strings["chest.found"]);
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["chest.inspect"], Tag: Mode.InspectWho);
                yield return new MenuItem(s["chest.disarm"], Tag: Mode.DisarmWho);
                yield return new MenuItem(s["chest.open"], Tag: Mode.OpenWho);
                yield return new MenuItem(s["chest.leave"], Tag: "leave");
                break;
            case Mode.DisarmTrap:
                foreach (var t in Enum.GetValues<ChestTrap>()) yield return new MenuItem(s["chest.trap_name." + t.ToString().ToLowerInvariant()], Tag: t);
                break;
            case Mode.Done:
                yield return new MenuItem(s["chest.close"], Tag: "leave");
                break;
            default:
                foreach (var c in Party) yield return new MenuItem(c.Name, Enabled: c.CanAct && !c.Lasting.Any(l => l.Condition == Condition.Paralyzed), Tag: c);
                break;
        }
    }

    private void Go(Mode m)
    {
        _mode = m;
        var items = Items().ToList();
        List.SetItems(items, Math.Max(0, items.FindIndex(i => i.Enabled)));
    }

    private void Say(string line)
    {
        _lines.Add(line);
        if (_lines.Count > 8) _lines.RemoveAt(0);
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (_mode, item.Tag)
        {
            case (_, "leave"): Stack.Pop(); return;
            case (Mode.Main, Mode m): Go(m); return;
            case (Mode.InspectWho, Character c):
            {
                var (seen, triggered) = chest.Inspect(Data, c, Rng);
                if (triggered)
                {
                    Say(s.Format("chest.inspect_triggered", c.Name));
                    Trigger(c);
                    return;
                }
                Say(s.Format("chest.inspect_result", c.Name, s["chest.trap_name." + seen.ToString().ToLowerInvariant()]));
                Go(Mode.Main);
                return;
            }
            case (Mode.DisarmWho, Character c):
                _who = c;
                Go(Mode.DisarmTrap);
                return;
            case (Mode.DisarmTrap, ChestTrap guess):
                switch (chest.Disarm(Data, _who!, guess, Rng))
                {
                    case Chest.DisarmResult.Disarmed:
                        Say(s.Format("chest.disarmed", _who!.Name));
                        Open();
                        break;
                    case Chest.DisarmResult.NoTrap:
                        Say(s["chest.no_trap"]);
                        Open();
                        break;
                    case Chest.DisarmResult.Failed:
                        Say(s.Format("chest.disarm_failed", _who!.Name));
                        Go(Mode.Main);
                        break;
                    case Chest.DisarmResult.Triggered:
                        Say(s.Format("chest.disarm_triggered", _who!.Name));
                        Trigger(_who);
                        break;
                }
                return;
            case (Mode.OpenWho, Character c):
                if (chest.OpenTriggers(Rng)) Trigger(c);
                else Open();
                return;
        }
    }

    private void Trigger(Character opener)
    {
        Services.Audio.PlaySe(Sounds.Trap);
        var r = chest.Trigger(Data, Party, opener, Rng);
        foreach (var (key, args) in r.Lines)
            Say(Services.Strings.Format(key, args.Select(a => a is Condition c ? Services.Strings["condition." + c.ToString().ToLowerInvariant()] : a).ToArray()));
        if (r.Teleport)
        {
            Stack.Pop();
            onTeleport();
            return;
        }
        if (r.Alarm)
        {
            Stack.Pop();
            onAlarm();
            return;
        }
        Open();
    }

    private void Open()
    {
        var s = Services.Strings;
        Services.Audio.PlaySe(Sounds.ChestOpen);
        var left = chest.Loot(Data, Party);
        Say(s.Format("chest.gold", chest.Gold));
        foreach (var it in chest.Items.Except(left)) Say(s.Format("chest.item", ItemText.Name(Services, it)));
        foreach (var it in left) Say(s.Format("chest.item_left", ItemText.Name(Services, it)));
        Go(Mode.Done);
    }

    protected override void Cancel()
    {
        if (_mode is Mode.Main or Mode.Done) Stack.Pop();
        else if (_mode == Mode.DisarmTrap) Go(Mode.DisarmWho);
        else Go(Mode.Main);
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        float y = area.Y + 20;
        foreach (var line in _lines) y = Line(d, area, y, line, Theme.Text, 19);
    }
}

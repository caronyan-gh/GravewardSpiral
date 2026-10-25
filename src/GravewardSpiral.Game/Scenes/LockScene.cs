using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 錠のある扉の前。鍵を使う(この階の鍵を拾っていれば必ず開く)、盗賊道具で開ける(敏捷力の判定)、
/// ノックを唱える(スロットを使う。魔法禁止区域では唱えられない)、蹴り開ける(筋力の判定。失敗すると音で敵が来ることがある)。
/// </summary>
public sealed class LockScene(ExplorationState state, Direction dir, int tier, Action onOpened, Action onNoise) : MenuScene
{
    private enum Mode { Main, PickWho, KickWho, KnockWho }

    private Mode _mode;
    private readonly List<string> _lines = [];

    private int Dc => 13 + tier;
    private GameData Data => Services.Data;
    private List<Character> Party => Services.Session!.Roster.PartyMembers.ToList();
    private IRandom Rng => Services.Session!.Rng[RngStreams.Names.Loot];
    private bool NoMagic => state.Cell.Zone.HasFlag(Zone.AntiMagic);

    protected override string Title => Services.Strings["lock.title"];

    protected override int? PartyHighlight => _mode != Mode.Main ? List.Cursor : null;

    public override void Enter()
    {
        base.Enter();
        _lines.Add(Services.Strings["lock.found"]);
    }

    /// <summary>ノックを唱えられる人(ノックを持っていて、スロットが残っている)。</summary>
    private bool CanKnock(Character c) =>
        c.CanAct && Spellcasting.CasterClass(Data, c) is { } cls && Data.Spells.TryGetValue("knock", out var k)
        && Spellcasting.Castable(Data, c).Contains(k) && Spellcasting.LowestSlot(cls, c, k) != null;

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["lock.key"], state.Keys.Contains(state.Position.Floor) ? null : s["lock.no_key"],
                    state.Keys.Contains(state.Position.Floor), "key");
                yield return new MenuItem(s["lock.pick"], Tag: Mode.PickWho);
                yield return new MenuItem(s["lock.knock"], NoMagic ? s["lock.antimagic"] : null, !NoMagic && Party.Any(CanKnock), Mode.KnockWho);
                yield return new MenuItem(s["lock.kick"], Tag: Mode.KickWho);
                yield return new MenuItem(s["lock.leave"], Tag: "leave");
                break;
            default:
                foreach (var c in Party)
                    yield return new MenuItem(c.Name, Enabled: c.CanAct && (_mode != Mode.KnockWho || CanKnock(c)), Tag: c);
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
            case (Mode.Main, "leave"): Stack.Pop(); return;
            case (Mode.Main, "key"):
                _lines.Add(s["lock.key_used"]);
                Open();
                return;
            case (Mode.Main, Mode m): Go(m); return;
            case (Mode.PickWho, Character c):
            {
                bool tools = c.Inventory.Items.Any(i => i.ItemId == "thieves_tools");
                bool proficient = tools && c.ClassIds.Any(id => Data.Classes[id].Tools.Contains("thieves_tools"));
                if (!tools)
                {
                    _lines.Add(s.Format("lock.no_tools", c.Name));
                    Go(Mode.Main);
                    return;
                }
                int total = Rng.Roll(20) + c.Stats(Data).Mod(Ability.Dex) + (proficient ? c.ProficiencyBonus : 0) + c.PassiveBonus(Data, "disarm");
                if (total >= Dc)
                {
                    _lines.Add(s.Format("lock.picked", c.Name));
                    Open();
                }
                else
                {
                    _lines.Add(s.Format("lock.pick_failed", c.Name));
                    Go(Mode.Main);
                }
                return;
            }
            case (Mode.KnockWho, Character c):
            {
                var cls = Spellcasting.CasterClass(Data, c)!;
                var knock = Data.Spells["knock"];
                Spellcasting.UseSlot(c, Spellcasting.LowestSlot(cls, c, knock)!.Value);
                Services.Audio.PlaySe(Sounds.SpellCast);
                _lines.Add(s.Format("lock.knocked", c.Name));
                Open();
                return;
            }
            case (Mode.KickWho, Character c):
            {
                int athletics = c.IsProficient("athletics") ? c.ProficiencyBonus : 0;
                if (Rng.Roll(20) + c.Stats(Data).Mod(Ability.Str) + athletics >= Dc + 2)
                {
                    _lines.Add(s.Format("lock.kicked", c.Name));
                    Open();
                    return;
                }
                Services.Audio.PlaySe(Sounds.Bump);
                _lines.Add(s.Format("lock.kick_failed", c.Name));
                if (Rng.NextDouble() < 0.4)
                {
                    Stack.Pop();
                    onNoise();
                    return;
                }
                Go(Mode.Main);
                return;
            }
        }
    }

    private void Open()
    {
        state.Unlock(state.Position.Floor, state.Position.Cell, dir);
        state.AutoMap.Record(state);
        Services.Audio.PlaySe(Sounds.Door);
        Stack.Pop();
        onOpened();
    }

    protected override void Cancel()
    {
        if (_mode == Mode.Main) Stack.Pop();
        else Go(Mode.Main);
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        if (List.Current?.Tag is Character c)
        {
            StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
            return;
        }
        float y = area.Y + 20;
        foreach (var line in _lines.TakeLast(8)) y = Line(d, area, y, line, Theme.Text, 19);
    }
}

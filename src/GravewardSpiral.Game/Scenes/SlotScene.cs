using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>セーブの枠を選ぶ画面。続きから(読む)と、新規ゲームで枠が全部埋まっているとき(上書き)に使う。</summary>
public sealed class SlotScene : MenuScene
{
    public enum Purpose { Load, Overwrite }

    private readonly Purpose _purpose;
    private readonly Action<int>? _onOverwrite;
    private List<SaveStore.SlotSummary> _slots = [];

    public SlotScene(Purpose purpose, Action<int>? onOverwrite = null)
    {
        _purpose = purpose;
        _onOverwrite = onOverwrite;
    }

    protected override string Title => Services.Strings[_purpose == Purpose.Load ? "slot.title_load" : "slot.title_overwrite"];

    public override void Enter()
    {
        _slots = Services.Saves.List();
        base.Enter();
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        for (int slot = 1; slot <= SaveStore.SlotCount; slot++)
        {
            var info = _slots.FirstOrDefault(x => x.Slot == slot);
            if (info == null)
            {
                yield return new MenuItem(s.Format("slot.label", slot), s["slot.empty"], Enabled: _purpose == Purpose.Overwrite, Tag: slot);
                continue;
            }
            string where = info.Location == "maze" ? s["slot.in_maze"] : s["slot.in_town"];
            yield return new MenuItem(s.Format("slot.label", slot), $"{info.SavedAt:MM/dd HH:mm}  {where}", Tag: slot);
        }
        yield return new MenuItem(s["common.back"]);
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not int slot)
        {
            Cancel();
            return;
        }
        var s = Services.Strings;
        if (_purpose == Purpose.Overwrite)
        {
            Stack.Push(new ConfirmScene(s.Format("slot.confirm_overwrite", slot), () =>
            {
                Stack.Pop();
                _onOverwrite?.Invoke(slot);
            }));
            return;
        }
        // シナリオのセーブなら、先にそのシナリオを読む
        string? scenarioId = Services.Saves.List().FirstOrDefault(x => x.Slot == slot)?.Scenario;
        if (scenarioId != null)
        {
            var scenario = Services.FindScenarios().FirstOrDefault(x => x.Id == scenarioId);
            if (scenario == null)
            {
                Message = s.Format("slot.scenario_missing", scenarioId);
                return;
            }
            if (Services.UseScenario(scenario) is { Count: > 0 } problems)
            {
                Message = s.Format("slot.load_failed", problems[0]);
                return;
            }
        }
        else Services.UseScenario(null);
        try
        {
            var session = Services.Saves.Load(slot);
            Services.Session = session;
            if (session.Location == Location.Maze && session.Exploration is { } state)
                Stack.Replace(new MazeScene(state, session.Request.Seed));
            else
                Stack.Replace(new TownScene());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or FormatException)
        {
            Message = s.Format("slot.load_failed", ex.Message);
        }
    }

    protected override void Cancel() => Stack.Replace(new TitleScene());

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        if (List.Current?.Tag is not int slot || _slots.FirstOrDefault(x => x.Slot == slot) is not { } info)
        {
            Line(d, area, area.Y + 20, s[_purpose == Purpose.Load ? "slot.about_load" : "slot.about_overwrite"], Theme.Text, 19);
            return;
        }
        float y = area.Y + 20;
        y = Line(d, area, y, s.Format("slot.label", slot), Theme.Title, 26);
        y = Line(d, area, y, s.Format("slot.saved_at", info.SavedAt), Theme.Text);
        y = Line(d, area, y, s.Format("slot.dungeon", info.Floors, info.DeepestFloor, info.Seed), Theme.Text);
        y = Line(d, area, y, s.Format("slot.party", info.Party.Count == 0 ? s["slot.no_party"] : Services.Strings.List(info.Party)), Theme.Text);
        Line(d, area, y, info.Location == "maze" ? s["slot.in_maze"] : s["slot.in_town"], Theme.Dim);
    }
}

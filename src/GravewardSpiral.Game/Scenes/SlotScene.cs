using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 続きから(1つ目): クランを選ぶ。最近遊んだ順に並べる。選ぶと、そのクランのシナリオを選ぶ画面へ。
/// </summary>
public sealed class ContinueScene : MenuScene
{
    private List<SaveStore.ClanSummary> _clans = [];

    protected override string Title => Services.Strings["slot.title_clan"];

    public override void Enter()
    {
        _clans = Services.Saves.ListClans();
        base.Enter();
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        // 最近遊んだ順。一番上(カーソルの合う所)が前回遊んだクラン
        foreach (var (clan, i) in _clans.Select((c, i) => (c, i)))
            yield return new MenuItem(Services.ClanDisplayName(clan.Name, clan.Id), i == 0 ? s["slot.last_played"] : $"{clan.SavedAt:MM/dd HH:mm}", Tag: clan.Id);
        yield return new MenuItem(s["common.back"]);
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not string clanId)
        {
            Cancel();
            return;
        }
        Stack.Push(new SlotScene(clanId, onChanged: () =>
        {
            _clans = Services.Saves.ListClans();
            if (_clans.Count == 0) Stack.Replace(new TitleScene());
            else Refresh();
        }));
    }

    protected override void Cancel() => Stack.Replace(new TitleScene());

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        if (List.Current?.Tag is not string clanId || _clans.FirstOrDefault(x => x.Id == clanId) is not { } clan)
        {
            Line(d, area, area.Y + 20, s[_clans.Count == 0 ? "slot.no_saves" : "slot.about_clan"], Theme.Text, 19);
            return;
        }
        float y = area.Y + 20;
        y = Line(d, area, y, Services.ClanDisplayName(clan.Name, clan.Id), Theme.Title, 26);
        y = Line(d, area, y, s.Format("slot.members", clan.Characters), Theme.Text);
        if (clan.Latest is { } latest)
            y = Line(d, area, y, s.Format("slot.party", latest.Party.Count == 0 ? s["slot.no_party"] : s.List(latest.Party)), Theme.Text);
        y = Line(d, area, y + 6, s.Format("slot.worlds", clan.Worlds.Count), Theme.Dim, 18);
        foreach (var w in clan.Worlds)
            y = Line(d, area, y, $"{Services.ScenarioDisplayName(w.Scenario)}  {w.SavedAt:MM/dd HH:mm}", Theme.Text, 18);
    }
}

/// <summary>
/// 続きから(2つ目): クランが遊ぶシナリオを選ぶ。シナリオのフォルダにあるものを全部出し、遊んだものは日時と場所、
/// まだ行っていないものには「未踏」を付ける。直前に遊んだ世界にカーソルを合わせておく。
/// 遊んだ世界を選ぶと続きから。まだ行っていないシナリオと「新しい自由な迷宮」は、新しく始めるの画面を、そのクランを選んだ状態で開く。
/// 世界(シナリオごとのセーブ)は消さない。「クランを解散する」で、名簿(キャラと持ち物)・全部の世界・クリアの印をまとめて消す(クランの名前を打たせてから。うっかり消さないため)。
/// </summary>
public sealed class SlotScene : MenuScene
{
    private readonly string _clanId;
    private readonly Action? _onChanged;
    private SaveStore.ClanSummary? _clan;
    private List<ScenarioInfo> _scenarios = [];

    public SlotScene(string clanId, Action? onChanged = null)
    {
        _clanId = clanId;
        _onChanged = onChanged;
    }

    private string ClanName => _clan is { } c ? Services.ClanDisplayName(c.Name, c.Id) : "";

    private IReadOnlyList<SaveStore.WorldSummary> Worlds => _clan?.Worlds ?? [];

    protected override string Title => Services.Strings.Format("slot.title_worlds", ClanName);

    public override void Enter()
    {
        _scenarios = Services.FindScenarios();
        Load();
        base.Enter();
        List.SetItems(Items(), 0);   // 直前に遊んだ世界(一番上)を選んでおく
    }

    private void Load() => _clan = Services.Saves.ListClans().FirstOrDefault(x => x.Id == _clanId);

    /// <summary>行の中身: 遊んだ世界(key)、まだ行っていないシナリオ(id)、新しい自由な迷宮、消す、戻る。</summary>
    private sealed record Unvisited(string ScenarioId);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var w in Worlds)
        {
            string where = w.Location == "maze" ? s.Format("slot.in_maze_floor", Math.Max(1, w.CurrentFloor)) : s["slot.in_town"];
            // 日時は右の欄に出す。一番上(カーソルの合う所)が前回遊んだ世界
            yield return new MenuItem(Services.ScenarioDisplayName(w.Scenario), w == Worlds[0] ? s.Format("slot.last_played_at", where) : where, Tag: w);
        }
        foreach (var sc in _scenarios.Where(sc => Worlds.All(w => w.Scenario != sc.Id)))
            yield return new MenuItem(Services.ScenarioDisplayName(sc.Id), s["slot.unvisited"], Tag: new Unvisited(sc.Id));
        yield return new MenuItem(s["slot.new_free"], Tag: "free");
        yield return new MenuItem(s["slot.delete_clan"], Tag: "delete_clan");
        yield return new MenuItem(s["common.back"]);
    }

    protected override void Choose(MenuItem item, int index)
    {
        switch (item.Tag)
        {
            case "delete_clan":
                AskDeleteClan();
                return;
            case "free":
                Stack.Replace(new NewGameScene(free: true, clanId: _clanId));
                return;
            case Unvisited u:
                // まだ行っていないシナリオ: 新しく始めるで、このクランとシナリオを選んだのと同じ
                Stack.Replace(new NewGameScene(scenarioId: u.ScenarioId, clanId: _clanId));
                return;
            case SaveStore.WorldSummary w:
                Continue(w);
                return;
            default:
                Cancel();
                return;
        }
    }

    private void Continue(SaveStore.WorldSummary w)
    {
        var s = Services.Strings;
        // シナリオの世界なら、先にそのシナリオを読む
        if (w.Scenario != null)
        {
            var scenario = _scenarios.FirstOrDefault(x => x.Id == w.Scenario);
            if (scenario == null)
            {
                Message = s.Format("slot.scenario_missing", w.Scenario);
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
            var session = Services.Saves.Load(_clanId, w.Key);
            session.ClanName ??= Services.ClanDisplayName(null, session.ClanId);   // 名前の無いクランには、決まった名前を付けておく
            Services.GiveScenarioPrologue(session);   // 序文の無いころに始めた世界にも、シナリオの序文を付ける(いわれを読み返せる)
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

    /// <summary>
    /// クランを解散する(名簿のキャラと持ち物・全部の世界・クリアの印を消す)。うっかり消さないよう、クランの名前を打たせ、合ったときだけ消す。
    /// </summary>
    private void AskDeleteClan()
    {
        var s = Services.Strings;
        string expected = ClanName;
        void Delete()
        {
            Services.Saves.DeleteClan(_clanId);
            Stack.Pop();
            _onChanged?.Invoke();
        }
        if (expected.Trim().Length == 0)
        {
            Stack.Push(new ConfirmScene(s.Format("slot.confirm_delete_clan", expected, Worlds.Count), Delete));
            return;
        }
        Stack.Push(new TextEntryScene(s.Format("slot.type_clan", expected, Worlds.Count), "", Math.Max(expected.Length, NewGameScene.ClanNameLength), typed =>
        {
            if (typed.Trim() == expected.Trim()) Delete();
            else
            {
                Message = s["slot.type_mismatch"];
                Services.Audio.PlaySe(Audio.Sounds.Bump);
            }
        }));
    }

    protected override void Cancel() => Stack.Pop();

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = area.Y + 20;
        switch (List.Current?.Tag)
        {
            case SaveStore.WorldSummary info:
                y = Line(d, area, y, Services.ScenarioDisplayName(info.Scenario), Theme.Title, 26);
                y = Line(d, area, y, s.Format("slot.saved_at", info.SavedAt), Theme.Text);
                y = Line(d, area, y, s.Format("slot.dungeon", info.Floors, info.DeepestFloor, info.Seed), Theme.Text);
                y = Line(d, area, y, s.Format("slot.party", info.Party.Count == 0 ? s["slot.no_party"] : s.List(info.Party)), Theme.Text);
                Line(d, area, y, info.Location == "maze" ? s["slot.in_maze"] : s["slot.in_town"], Theme.Dim);
                break;
            case Unvisited u when _scenarios.FirstOrDefault(x => x.Id == u.ScenarioId) is { } sc:
                y = Line(d, area, y, Services.ScenarioDisplayName(sc.Id), Theme.Title, 26);
                if (sc.Manifest.Description != null) y = Line(d, area, y, Services.Name(sc.Manifest.Description), Theme.Text);
                Line(d, area, y + 6, s["slot.about_unvisited"], Theme.Dim, 18);
                break;
            case "free":
                Line(d, area, y, s["slot.about_new_free"], Theme.Text, 19);
                break;
            case "delete_clan":
                foreach (var line in s.Format("slot.about_delete_clan", Worlds.Count).Split('\n'))   // 消えるものを箇条書きで
                    y = Line(d, area, y, line, Theme.Text, 19);
                break;
            default:
                Line(d, area, y, s["slot.about_load"], Theme.Text, 19);
                break;
        }
    }
}

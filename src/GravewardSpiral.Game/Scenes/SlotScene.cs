using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 続きから(1つ目): クランを選ぶ。最近遊んだ順に並べる。選ぶと、そのクランのシナリオを選ぶ画面へ。
/// 一覧の下のボタンで、クランを書き出す・読み込む・解散する(書き出すと解散は、ボタンを押してからクランを選ぶ)。
/// ボタンはメニューのキーで行き来する(左右で選んで決定。メニューのキーか取消で一覧に戻る)。マウスはボタンを直接押す。
/// 書き出しと読み込みは Windows のファイルの窓で場所を選ぶ。書き出しはクランを世界ごと持ち出す(書けたら一覧から消す)。
/// クランが無くても開ける(読み込むため)。
/// </summary>
public sealed class ContinueScene : MenuScene
{
    private enum Pick { Play, Export, Delete }

    private static readonly string[] Buttons = ["export", "import", "delete"];

    private List<SaveStore.ClanSummary> _clans = [];
    private string? _importPath;
    private Pick _pick;

    /// <param name="importPath">開いたらすぐ読み込むクランのファイル(ダブルクリックやタイトルに落としたもの)。</param>
    public ContinueScene(string? importPath = null) => _importPath = importPath;
    private int? _button;   // ボタンにカーソルがあるとき、その番
    private Vector2? _mouse;

    protected override string Title => Services.Strings[_pick switch
    {
        Pick.Export => "slot.title_pick_export",
        Pick.Delete => "slot.title_pick_delete",
        _ => "slot.title_clan",
    }];

    public override void Enter()
    {
        _clans = Services.Saves.ListClans();
        base.Enter();
        if (_importPath != null)
        {
            Import(_importPath);
            _importPath = null;
        }
    }

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        // 最近遊んだ順。「前回」は、遊んだ世界のあるクランのうち一番新しいもの(読み込んだだけのクランには付けない)
        var last = _clans.FirstOrDefault(c => c.Worlds.Count > 0);
        foreach (var clan in _clans)
            yield return new MenuItem(Services.ClanDisplayName(clan.Name, clan.Id), clan == last ? s["slot.last_played"] : $"{clan.SavedAt:MM/dd HH:mm}", Tag: clan.Id);
        yield return new MenuItem(s["common.back"]);
    }

    private static RectangleF ButtonRect(int i) => new(InfoArea.X + i * 200, InfoArea.Y + InfoArea.Height + 22, 188, 40);

    public override void Update(GameTime time, Input.InputState input)
    {
        _mouse = input.Mouse;
        // マウスでボタンを押す(どこにカーソルがあっても)
        if (input.LeftClicked && input.Mouse is { } m)
            for (int i = 0; i < Buttons.Length; i++)
                if (ButtonRect(i).Contains(m))
                {
                    Press(i);
                    return;
                }
        if (_button is int b)
        {
            if (input.Pressed(Input.GameAction.Menu) || input.Pressed(Input.GameAction.Cancel))
            {
                Services.Audio.PlaySe(Audio.Sounds.Cancel);
                _button = null;
            }
            else if (input.Repeat(Input.GameAction.Left) || input.Repeat(Input.GameAction.Right))
            {
                _button = (b + (input.Repeat(Input.GameAction.Left) ? Buttons.Length - 1 : 1)) % Buttons.Length;
                Services.Audio.PlaySe(Audio.Sounds.Cursor);
            }
            else if (input.Pressed(Input.GameAction.Confirm)) Press(b);
            return;
        }
        if (input.Pressed(Input.GameAction.Menu))
        {
            Services.Audio.PlaySe(Audio.Sounds.Cursor);
            _button = 0;
            return;
        }
        base.Update(time, input);
    }

    /// <summary>ボタンを押す。書き出すと解散は、次に選ぶクランへの操作にする。読み込むは、すぐファイルを選ぶ。</summary>
    private void Press(int i)
    {
        var s = Services.Strings;
        Services.Audio.PlaySe(Audio.Sounds.Confirm);
        _button = null;
        Message = null;
        switch (Buttons[i])
        {
            case "import":
                _pick = Pick.Play;
                Import();
                break;
            case "export" when _clans.Count > 0:
                _pick = Pick.Export;
                break;
            case "delete" when _clans.Count > 0:
                _pick = Pick.Delete;
                break;
            default:
                Message = s["slot.no_saves"];
                break;
        }
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not string clanId)
        {
            Cancel();
            return;
        }
        switch (_pick)
        {
            case Pick.Export:
                _pick = Pick.Play;
                if (_clans.FirstOrDefault(c => c.Id == clanId) is { } target)
                    Stack.Push(new ConfirmScene(Services.Strings.Format("slot.confirm_export", Name(target)), () => Export(clanId)));
                return;
            case Pick.Delete:
                _pick = Pick.Play;
                AskDelete(clanId);
                return;
        }
        Stack.Push(new SlotScene(clanId, onChanged: Reload));
    }

    /// <summary>クランの一覧を読み直す。</summary>
    private void Reload()
    {
        _clans = Services.Saves.ListClans();
        Refresh();
    }

    private string Name(SaveStore.ClanSummary clan) => Services.ClanDisplayName(clan.Name, clan.Id);

    /// <summary>
    /// セーブのクランを世界ごと、選んだ場所にクランファイルとして書き出し、書けたファイルを読み直して確かめてから、一覧から消す(引っ越し)。
    /// </summary>
    private void Export(string clanId)
    {
        var s = Services.Strings;
        if (_clans.FirstOrDefault(c => c.Id == clanId) is not { } clan) return;
        string name = Name(clan);
        try
        {
            // 確かめの起動(--screenshot)では窓を開かず、セーブのフォルダの中に書く(本物のクランのフォルダに書かない)
            string? path = Services.PersistOptions
                ? FileDialogs.Save(Services.Display, s["slot.export_dialog"], s["slot.clan_filter"], AppPaths.Clans, SaveStore.ClanFileNameFor(name), SaveStore.ClanExtension.TrimStart('.'))
                : Path.Combine(Services.Saves.Root, "export", SaveStore.ClanFileNameFor(name));
            if (path == null) return;
            Services.Saves.ExportClan(clanId, path);   // 書いたファイルを読み直して確かめる
            Services.Saves.DeleteClan(clanId);
            Reload();
            Message = s.Format("slot.exported", name, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Message = ex.Message;
        }
    }

    /// <summary>書き出したクランのファイルを選んで読み込み、クランの一覧に入れる(名簿も世界も元どおり)。</summary>
    private void Import()
    {
        if (!Services.PersistOptions) return;   // 確かめの起動では窓を開かない
        string? path = FileDialogs.Open(Services.Display, Services.Strings["slot.import_dialog"], Services.Strings["slot.clan_filter"], AppPaths.Clans);
        if (path != null) Import(path);
    }

    /// <summary>クランのファイルを読み込む(ファイルの窓のほか、関連付けでゲームに渡されたファイルも)。</summary>
    public void Import(string path)
    {
        var s = Services.Strings;
        try
        {
            var (id, name) = Services.Saves.ImportClan(path);
            Reload();
            List.SetItems(Items(), Math.Max(0, _clans.FindIndex(c => c.Id == id)));
            Message = s.Format("slot.imported", Services.ClanDisplayName(name, id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Message = s.Format("slot.import_failed", ex.Message);
        }
    }

    /// <summary>クランを解散する(クランの名前を打たせてから。うっかり消さないため)。</summary>
    private void AskDelete(string clanId)
    {
        var s = Services.Strings;
        if (_clans.FirstOrDefault(c => c.Id == clanId) is not { } clan) return;
        string expected = Name(clan);
        int worlds = clan.Worlds.Count;
        // 先に、ごみ箱へ移すか完全に消すかを選び、そのあとクランの名前を打って確かめる
        Stack.Push(new ConfirmScene(s.Format("slot.confirm_delete_clan", expected, worlds), [s["slot.to_recycle"], s["slot.delete_forever"], s["title.drop_cancel"]], choice =>
        {
            if (choice == 2) return;
            void Delete()
            {
                try { Trash.Delete(Services.Saves.ClanDir(clanId), toRecycleBin: choice == 0); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { Message = ex.Message; }
                Reload();
            }
            if (expected.Trim().Length == 0)
            {
                Delete();
                return;
            }
            Stack.Push(new TextEntryScene(s.Format("slot.type_clan", expected, worlds), "", Math.Max(expected.Length, NewGameScene.ClanNameLength), typed =>
            {
                if (typed.Trim() == expected.Trim()) Delete();
                else
                {
                    Message = s["slot.type_mismatch"];
                    Services.Audio.PlaySe(Audio.Sounds.Bump);
                }
            }));
        }, cancelIndex: 2));
    }

    protected override void Cancel()
    {
        // 書き出すか解散するクランを選んでいるときは、ふつうの選び方に戻る
        if (_pick != Pick.Play)
        {
            _pick = Pick.Play;
            return;
        }
        Stack.Replace(new TitleScene());
    }

    public override void Draw(Draw2D d)
    {
        base.Draw(d);
        var s = Services.Strings;
        for (int i = 0; i < Buttons.Length; i++)
        {
            var r = ButtonRect(i);
            bool on = _button == i || _mouse is { } m && r.Contains(m)
                || _pick == Pick.Export && Buttons[i] == "export" || _pick == Pick.Delete && Buttons[i] == "delete";
            if (on) d.FillRect(r, Theme.SelectedFill);
            d.Rect(r, on ? Theme.SelectedFrame : Theme.Frame, on ? 1.5f : 1f);
            d.Text(s["slot.button_" + Buttons[i]], new Vector2(r.X + r.Width / 2, r.Y + 9), 20, on ? Color.White : Theme.Text, TextAlign.Center);
        }
        d.Text(s["slot.buttons_hint"], new Vector2(InfoArea.X + 4, InfoArea.Y + InfoArea.Height + 72), 15, Theme.Dim);
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        if (_pick != Pick.Play && List.Current?.Tag is not string)
        {
            Line(d, area, area.Y + 20, s[_pick == Pick.Export ? "slot.about_pick_export" : "slot.about_pick_delete"], Theme.Text, 19);
            return;
        }
        if (List.Current?.Tag is not string clanId || _clans.FirstOrDefault(x => x.Id == clanId) is not { } clan)
        {
            Line(d, area, area.Y + 20, s[_clans.Count == 0 ? "slot.no_saves_import" : "slot.about_clan"], Theme.Text, 19);
            return;
        }
        float y = area.Y + 20;
        if (_pick != Pick.Play)
            y = Line(d, area, y, s[_pick == Pick.Export ? "slot.about_pick_export" : "slot.about_pick_delete"], Theme.Gold, 19) + 6;
        y = Line(d, area, y, Name(clan), Theme.Title, 26);
        y = Line(d, area, y, s.Format("slot.members", clan.Characters), Theme.Text);
        if (clan.Latest is { } latest)
            y = Line(d, area, y, s.Format("slot.party", latest.Party.Count == 0 ? s["slot.no_party"] : s.List(latest.Party)), Theme.Text);
        if (_pick == Pick.Delete)
        {
            foreach (var line in s.Format("slot.about_delete_clan", clan.Worlds.Count).Split('\n'))   // 消えるものを箇条書きで
                y = Line(d, area, y, line, Theme.Text, 18);
            return;
        }
        y = Line(d, area, y + 6, s.Format("slot.worlds", clan.Worlds.Count), Theme.Dim, 18);
        foreach (var w in clan.Worlds)
            y = Line(d, area, y, $"{Services.ScenarioDisplayName(w.Scenario)}  {w.SavedAt:MM/dd HH:mm}", Theme.Text, 18);
    }
}

/// <summary>
/// 続きから(2つ目): クランが遊ぶシナリオを選ぶ。シナリオのフォルダにあるものを全部出し、遊んだものは日時と場所、
/// まだ行っていないものには「未踏」を付ける。直前に遊んだ世界にカーソルを合わせておく。
/// 遊んだ世界を選ぶと続きから。まだ行っていないシナリオと「新しい自由な迷宮」は、新しく始めるの画面を、そのクランを選んだ状態で開く。
/// 世界(シナリオごとのセーブ)は消さない。クランの解散は、クランを選ぶ画面(ContinueScene)のボタンで行う。
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

    /// <summary>シナリオのパックが見つからない世界(遊べない。選ぶと消すかを聞く)。</summary>
    private sealed record Missing(SaveStore.WorldSummary World);

    /// <summary>
    /// このクランを、まだ行っていないシナリオへ連れて入れるか(入れないシナリオは一覧に出さない)。
    /// 使えるクラス・種族・呪文は、シナリオの差を土台に重ねて読む(ScenarioIds。データを全部は読まない)。
    /// </summary>
    private bool CanEnter(ScenarioInfo sc)
    {
        if (Services.Saves.LoadClan(_clanId) is not { } clan) return true;
        var played = Worlds.Select(w => w.Scenario).Concat(clan.PlayedBefore).Distinct().ToList();
        return ClanImport.CanEnter(sc, ScenarioIds.Read(SrdDir, sc.Directory), played, clan.ClearedScenarios,
            clan.Characters, id => _scenarios.FirstOrDefault(x => x.Id == id)?.Manifest.Series);
    }

    private static string SrdDir => Path.Combine(AppPaths.Content, "srd");

    private bool PackMissing(SaveStore.WorldSummary w) => w.Scenario != null && _scenarios.All(x => x.Id != w.Scenario);

    private string WorldName(SaveStore.WorldSummary w) =>
        w.Key.StartsWith("free-") && PackMissing(w) ? Services.Strings.Format("slot.free_world_seed", w.Seed) : Services.ScenarioDisplayName(w.Scenario);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var w in Worlds.Where(w => !PackMissing(w)))
        {
            string where = w.Location == "maze" ? s.Format("slot.in_maze_floor", Math.Max(1, w.CurrentFloor)) : s["slot.in_town"];
            // 日時は右の欄に出す。一番上(カーソルの合う所)が前回遊んだ世界
            yield return new MenuItem(Services.ScenarioDisplayName(w.Scenario), w == Worlds.FirstOrDefault(x => !PackMissing(x)) ? s.Format("slot.last_played_at", where) : where, Tag: w);
        }
        foreach (var sc in _scenarios.Where(sc => Worlds.All(w => w.Scenario != sc.Id) && CanEnter(sc)))
            yield return new MenuItem(Services.ScenarioDisplayName(sc.Id), s["slot.unvisited"], Tag: new Unvisited(sc.Id));
        // 自由な迷宮は土台だけなので、土台に無いクラス・種族・呪文の人がいるクランは入れない
        if (Services.Saves.LoadClan(_clanId) is not { } clan || ClanImport.Missing(ScenarioIds.Read(SrdDir, SrdDir), clan.Characters).Count == 0)
            yield return new MenuItem(s["slot.new_free"], Tag: "free");
        foreach (var w in Worlds.Where(PackMissing))
            yield return new MenuItem(WorldName(w), s["slot.no_pack"], Tag: new Missing(w), Dim: true);
        yield return new MenuItem(s["common.back"]);
    }

    protected override void Choose(MenuItem item, int index)
    {
        switch (item.Tag)
        {
            case "free":
                Stack.Replace(new NewGameScene(free: true, clanId: _clanId));
                return;
            case Missing m:
                AskDeleteWorld(m.World);
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
    /// <summary>遊べない世界を消す(ごみ箱へ移すか、完全に消すかを選ぶ)。</summary>
    private void AskDeleteWorld(SaveStore.WorldSummary w)
    {
        var s = Services.Strings;
        Stack.Push(new ConfirmScene(s.Format("slot.confirm_delete_world", WorldName(w)), [s["slot.to_recycle"], s["slot.delete_forever"], s["title.drop_cancel"]], choice =>
        {
            if (choice == 2) return;
            try
            {
                Trash.Delete(Services.Saves.WorldDir(_clanId, w.Key), toRecycleBin: choice == 0);
                Message = s.Format("slot.world_deleted", WorldName(w));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Message = ex.Message;
            }
            Load();
            Refresh();
            _onChanged?.Invoke();
        }, cancelIndex: 2));
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
                if (_scenarios.FirstOrDefault(x => x.Id == info.Scenario)?.Manifest.Levels is [int wlo, int whi]) y = Line(d, area, y, s.Format("slot.levels", wlo, whi), Theme.Text);
                Line(d, area, y, info.Location == "maze" ? s["slot.in_maze"] : s["slot.in_town"], Theme.Dim);
                break;
            case Unvisited u when _scenarios.FirstOrDefault(x => x.Id == u.ScenarioId) is { } sc:
                y = Line(d, area, y, Services.ScenarioDisplayName(sc.Id), Theme.Title, 26);
                if (sc.Manifest.Description != null) y = Line(d, area, y, Services.Name(sc.Manifest.Description), Theme.Text);
                if (sc.Manifest.Levels is [int lo, int hi]) y = Line(d, area, y, s.Format("slot.levels", lo, hi), Theme.Text);   // 推奨レベル(参考)
                Line(d, area, y + 6, s["slot.about_unvisited"], Theme.Dim, 18);
                break;
            case "free":
                Line(d, area, y, s["slot.about_new_free"], Theme.Text, 19);
                break;
            case Missing m:
                y = Line(d, area, y, WorldName(m.World), Theme.Title, 26);
                Line(d, area, y, s["slot.about_missing"], Theme.Text, 19);
                break;
            default:
                Line(d, area, y, s["slot.about_load"], Theme.Text, 19);
                break;
        }
    }
}

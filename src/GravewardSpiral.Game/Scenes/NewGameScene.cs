using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 新規ゲームの画面。シナリオ・連れていくクラン・シード・階層の数・階の大きさ・難易度を決めて、迷宮を作る。
/// シナリオを選ぶと、そのパックを読み直す(階層の数などの既定も、そのシナリオのものになる)。
/// 難易度は Wizardry 風・5版風・カスタム。カスタムでは、全滅・蘇生・セーブの厳しさを1つずつ選ぶ。
/// 操作: 上下で項目を選び、左右で数を変える(押し続けると早く変わる)。シードは数字キーで入力でき、決定でランダムにする。
/// </summary>
public sealed class NewGameScene : Scene
{
    private enum Row { Scenario, Clan, ClanName, Seed, Floors, SizeMode, Width, Height, MaxWidth, MaxHeight, Difficulty, Bodies, Revive, Save, Create, Back }

    private const float Left = 300f, ValueX = 760f, Top = 112f, Step = 34f;

    private Difficulty _difficulty = new();
    private List<ScenarioInfo> _scenarios = [];
    private int _scenario = -1;   // -1 は本編(標準のパックだけ)
    private List<(string Path, ClanFile Clan)> _clans = [];
    private int _clan = -1;       // -1 は連れていかない
    private string _clanName = "";

    private MazeGenSettings _settings = null!;
    private string _seed = "";
    private int _floors, _width, _height, _maxWidth, _maxHeight;
    private bool _randomSize;
    private int _cursor;
    private string? _message;
    private double _repeatHeld;

    public override void Enter()
    {
        _scenarios = Services.FindScenarios();
        // 手元のセーブのクラン(書き出さなくても選べる)と、書き出したクランファイル
        _clans = [.. ClanFile.FromSaves(Services.Saves, slot => Services.Strings.Format("newgame.clan_slot_unnamed", slot)),
                  .. ClanFile.List(AppPaths.Clans)];
        _scenario = Services.Content.Scenario is { } sc ? _scenarios.FindIndex(x => x.Id == sc.Id) : -1;
        LoadSettings();
    }

    /// <summary>今のパックの生成の設定と、その既定(階層の数・大きさ)。</summary>
    private void LoadSettings()
    {
        _settings = MazeGenSettings.Load(Services.Content);
        _seed = RandomSeed();
        _floors = _settings.DefaultFloors;
        _width = _maxWidth = _settings.DefaultWidth;
        _height = _maxHeight = _settings.DefaultHeight;
    }

    private static string RandomSeed() => (Random.Shared.NextInt64(1, 100_000_000)).ToString();

    private List<Row> Rows()
    {
        var rows = new List<Row> { Row.Scenario };
        if (Services.Content.Scenario?.AllowImport ?? true) rows.Add(Row.Clan);
        rows.Add(Row.ClanName);
        rows.AddRange([Row.Seed, Row.Floors, Row.SizeMode, Row.Width, Row.Height]);
        if (_randomSize) rows.AddRange([Row.MaxWidth, Row.MaxHeight]);
        rows.Add(Row.Difficulty);
        if (_difficulty.Preset == Difficulty.Custom) rows.AddRange([Row.Bodies, Row.Revive, Row.Save]);
        rows.AddRange([Row.Create, Row.Back]);
        return rows;
    }

    private static RectangleF RowRect(int i) => new(Left - 20, Top + i * Step - 6, 760, Step - 4);

    public override void Update(GameTime time, InputState input)
    {
        var rows = Rows();
        int before = _cursor;
        if (input.Repeat(GameAction.Up)) _cursor = (_cursor + rows.Count - 1) % rows.Count;
        if (input.Repeat(GameAction.Down)) _cursor = (_cursor + 1) % rows.Count;
        if (input.Mouse is { } m)
        {
            for (int i = 0; i < rows.Count; i++)
                if (RowRect(i).Contains(m) && input.MouseMoved) _cursor = i;
        }
        if (_cursor != before) Services.Audio.PlaySe(Sounds.Cursor);
        var row = rows[_cursor];

        // 押し続けたら、増え方を大きくする
        bool held = input.Held(GameAction.Left) || input.Held(GameAction.Right);
        _repeatHeld = held ? _repeatHeld + time.ElapsedGameTime.TotalSeconds : 0;
        int step = _repeatHeld > 1.5 ? 5 : 1;
        int delta = (input.Repeat(GameAction.Right) ? step : 0) - (input.Repeat(GameAction.Left) ? step : 0) + input.WheelDelta;
        if (delta != 0) Adjust(row, delta);

        if (row == Row.Seed)
        {
            foreach (char c in input.TypedDigits)
            {
                if (_seed.Length < 18) _seed += c;
            }
            if (input.BackspacePressed)
            {
                if (_seed.Length > 0) _seed = _seed[..^1];
                return;   // Backspace は取消にも割り当てられているので、ここでは取消にしない
            }
        }

        bool clicked = input.LeftClicked && input.Mouse is { } mm && RowRect(_cursor).Contains(mm);
        // 数を変える行では、値の左側(◀)のクリックで減らし、右側(▶)のクリックで増やす
        if (clicked && IsAdjustable(row) && input.Mouse is { } mc && Math.Abs(mc.X - ValueX) > ArrowDeadZone)
        {
            Adjust(row, mc.X < ValueX ? -1 : 1);
            return;
        }
        if (input.Pressed(GameAction.Confirm) || clicked)
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            switch (row)
            {
                case Row.Seed: _seed = RandomSeed(); break;
                case Row.ClanName:
                    Stack.Push(new TextEntryScene(Services.Strings["newgame.clan_name"], _clanName, ClanNameLength, name => _clanName = name));
                    break;
                case Row.SizeMode: _randomSize = !_randomSize; break;
                case Row.Difficulty or Row.Bodies or Row.Revive or Row.Save or Row.Scenario or Row.Clan: Adjust(row, 1); break;
                case Row.Create: Create(); break;
                case Row.Back: Stack.Replace(new TitleScene()); break;
            }
        }
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Replace(new TitleScene());
        }
    }

    /// <summary>値の真ん中から、この幅の内側のクリックは ◀ ▶ のどちらでもない(値そのものの上)。</summary>
    private const float ArrowDeadZone = 12f;

    /// <summary>クランの名前の長さの上限。</summary>
    public const int ClanNameLength = 16;

    /// <summary>値の真ん中から ◀ ▶ までの距離。</summary>
    private const float ArrowSpan = 210f;

    private static bool IsAdjustable(Row row) => row is Row.Floors or Row.SizeMode or Row.Width or Row.Height or Row.MaxWidth or Row.MaxHeight
        or Row.Difficulty or Row.Bodies or Row.Revive or Row.Save or Row.Scenario or Row.Clan;

    private void Adjust(Row row, int delta)
    {
        int min = _settings.MinSize, max = _settings.MaxSize;
        switch (row)
        {
            case Row.Floors: _floors = Math.Clamp(_floors + delta, 1, _settings.MaxFloors); break;
            case Row.SizeMode: _randomSize = !_randomSize; break;
            case Row.Width: _width = Math.Clamp(_width + delta, min, max); _maxWidth = Math.Max(_maxWidth, _width); break;
            case Row.Height: _height = Math.Clamp(_height + delta, min, max); _maxHeight = Math.Max(_maxHeight, _height); break;
            case Row.MaxWidth: _maxWidth = Math.Clamp(_maxWidth + delta, _width, max); break;
            case Row.MaxHeight: _maxHeight = Math.Clamp(_maxHeight + delta, _height, max); break;
            case Row.Difficulty:
            {
                var presets = Difficulty.Presets;
                int i = Array.IndexOf(presets, _difficulty.Preset);
                _difficulty = Difficulty.Of(presets[((i + Math.Sign(delta)) % presets.Length + presets.Length) % presets.Length]);
                break;
            }
            case Row.Scenario:
            {
                if (_scenarios.Count == 0) return;
                int next = (_scenario + 1 + Math.Sign(delta) + _scenarios.Count + 1) % (_scenarios.Count + 1) - 1;
                var problems = Services.UseScenario(next < 0 ? null : _scenarios[next]);
                if (problems.Count > 0)
                {
                    _message = Services.Strings.Format("newgame.scenario_failed", problems[0]);
                    return;
                }
                _scenario = next;
                LoadSettings();
                break;
            }
            case Row.Clan:
                if (_clans.Count == 0) return;
                _clan = (_clan + 1 + Math.Sign(delta) + _clans.Count + 1) % (_clans.Count + 1) - 1;
                if (_clan >= 0) _clanName = _clans[_clan].Clan.Name;   // 連れていくクランの名前を引き継ぐ
                break;
            case Row.Bodies: _difficulty.BodiesRemain = !_difficulty.BodiesRemain; break;
            case Row.Revive: _difficulty.RiskyRevive = !_difficulty.RiskyRevive; break;
            case Row.Save: _difficulty.StrictSave = !_difficulty.StrictSave; break;
            default: return;
        }
        Services.Audio.PlaySe(Sounds.Cursor);
    }

    private void Create()
    {
        _message = null;
        ulong seed = RngStreams.ParseSeed(_seed) ?? (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var size = _randomSize ? new FloorSizeMode(_width, _maxWidth, _height, _maxHeight) : FloorSizeMode.Fixed(_width, _height);
        var request = new GenerationRequest(seed, _floors, size);
        if (Services.Saves.FirstEmptySlot() is int slot) Start(request, slot);
        else Stack.Push(new SlotScene(SlotScene.Purpose.Overwrite, s => Start(request, s)));
    }

    /// <summary>迷宮を作り、その枠で新しいゲームを始めて町へ。</summary>
    private void Start(GenerationRequest request, int slot)
    {
        try
        {
            var dungeon = new MazeGenerator(_settings).Generate(request).Dungeon;
            Console.WriteLine($"dungeon: seed {request.Seed}, {dungeon.Floors.Count} floors, slot {slot}");
            Services.Saves.Delete(slot);
            Services.Session = GameSession.New(slot, request, dungeon);
            Services.Session.Difficulty = _difficulty;
            Services.Session.ClanName = _clanName.Trim().Length > 0 ? _clanName.Trim() : null;
            Services.Session.Scenario = Services.Content.Scenario?.Id;
            Services.Session.ScenarioVersion = Services.Content.Scenario?.Version;
            var enc = Core.Combat.EncounterSettings.Load(Services.Content);
            if (_clan >= 0 && Rows().Contains(Row.Clan))
            {
                _clans[_clan].Clan.ApplyTo(Services.Session);
                // 世界のレベル帯を、連れていくクランの強さに合わせる(周回)
                Services.Session.SetWorldLevel(enc, ClanPower(_clans[_clan].Clan));
            }
            Services.Session.BuildRosters(Services.Data, enc);
            Services.Session.PlaceEvents(Services.Data, _settings, enc);
            Stack.Replace(new TownScene());
        }
        catch (InvalidOperationException ex)
        {
            _message = Services.Strings.Format("newgame.failed", ex.Message);
        }
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.Text(s["newgame.title"], new Vector2(640, 50), 40, Color.White, TextAlign.Center);
        var rows = Rows();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = RowRect(i);
            bool selected = i == _cursor;
            if (selected)
            {
                d.FillRect(r, new Color(40, 60, 80, 180));
                d.Rect(r, new Color(150, 190, 230), 1.5f);
            }
            var color = selected ? Color.White : new Color(160, 170, 180);
            float y = Top + i * Step;
            var (label, value) = Describe(rows[i]);
            d.Text(label, new Vector2(Left, y), 24, color);
            if (value != null)
            {
                bool adjustable = rows[i] is not (Row.Seed or Row.ClanName);
                // ◀ ▶ は値の長さによらず決まった位置に出す
                d.Text(value, new Vector2(ValueX, y), 24, color, TextAlign.Center);
                if (adjustable)
                {
                    d.Text("◀", new Vector2(ValueX - ArrowSpan, y), 24, color, TextAlign.Center);
                    d.Text("▶", new Vector2(ValueX + ArrowSpan, y), 24, color, TextAlign.Center);
                }
            }
        }
        // クランを選んでいれば、クリアした数と世界のレベル帯を出す(周回)
        string hint = rows[_cursor] == Row.Clan && _clan >= 0 ? ClanHint(_clans[_clan].Clan) : s[HintKey(rows[_cursor])];
        d.Text(hint, new Vector2(640, 650), 18, new Color(140, 150, 160), TextAlign.Center);
        if (_message != null) d.Text(_message, new Vector2(640, 680), 18, new Color(255, 170, 150), TextAlign.Center);
    }

    private (string Label, string? Value) Describe(Row row)
    {
        var s = Services.Strings;
        return row switch
        {
            Row.Scenario => (s["newgame.scenario"], _scenario < 0 ? s["newgame.scenario.base"] : ScenarioLabel(_scenarios[_scenario])),
            Row.Clan => (s["newgame.clan"], _clan < 0 ? s["newgame.clan.none"] : ClanLabel(_clans[_clan].Clan)),
            Row.ClanName => (s["newgame.clan_name"], _clanName.Length > 0 ? _clanName : s["newgame.clan_name.none"]),
            Row.Seed => (s["newgame.seed"], _seed.Length == 0 ? s["newgame.seed_random"] : _seed),
            Row.Floors => (s["newgame.floors"], s.Format("newgame.floors_value", _floors)),
            Row.SizeMode => (s["newgame.size_mode"], s[_randomSize ? "newgame.size_random" : "newgame.size_fixed"]),
            Row.Width => (s[_randomSize ? "newgame.min_width" : "newgame.width"], _width.ToString()),
            Row.Height => (s[_randomSize ? "newgame.min_height" : "newgame.height"], _height.ToString()),
            Row.MaxWidth => (s["newgame.max_width"], _maxWidth.ToString()),
            Row.MaxHeight => (s["newgame.max_height"], _maxHeight.ToString()),
            Row.Difficulty => (s["newgame.difficulty"], s["newgame.difficulty." + _difficulty.Preset]),
            Row.Bodies => (s["newgame.bodies"], s[_difficulty.BodiesRemain ? "newgame.bodies.on" : "newgame.bodies.off"]),
            Row.Revive => (s["newgame.revive"], s[_difficulty.RiskyRevive ? "newgame.revive.on" : "newgame.revive.off"]),
            Row.Save => (s["newgame.save"], s[_difficulty.StrictSave ? "newgame.save.on" : "newgame.save.off"]),
            Row.Create => (s["newgame.create"], null),
            _ => (s["newgame.back"], null),
        };
    }

    /// <summary>連れていくクランの強さ(パーティ。いなければ名簿のレベルの高い6人)。</summary>
    private double ClanPower(ClanFile clan)
    {
        var party = clan.Party.Select(id => clan.Characters.FirstOrDefault(c => c.Id == id)).OfType<Core.Characters.Character>().ToList();
        var members = party.Count > 0 ? party : clan.Characters.OrderByDescending(c => c.Level).Take(Core.Characters.Roster.MaxParty).ToList();
        return WorldLevel.ClanPower(Services.Data, members);
    }

    /// <summary>そのクランで始めたときの、世界のレベル帯。</summary>
    private (double First, double Last) WorldBand(ClanFile clan)
    {
        var enc = Core.Combat.EncounterSettings.Load(Services.Content);
        return WorldLevel.Band(enc.FirstLevel, enc.LastLevel, ClanPower(clan));
    }

    /// <summary>「[セーブ 1] 名前(6 人)」。</summary>
    private string ClanLabel(ClanFile clan)
    {
        string label = Services.Strings.Format("newgame.clan_value", clan.Name, clan.Characters.Count);
        string path = _clans[_clan].Path;
        return path.StartsWith("slot:") ? Services.Strings.Format("newgame.clan_from_slot", path["slot:".Length..]) + label : label;
    }

    /// <summary>「クリア 2 回・強さ L8.5。世界は L7〜11 で作る」。</summary>
    private string ClanHint(ClanFile clan)
    {
        var (first, last) = WorldBand(clan);
        return Services.Strings.Format("newgame.clan_hint", clan.Clears, ClanPower(clan).ToString("0.#"), (int)first, (int)last);
    }

    private string ScenarioLabel(ScenarioInfo sc)
    {
        string name = Services.Name(sc.Manifest.Name) is { Length: > 0 } n ? n : sc.Id;
        return sc.Manifest.Levels is [int lo, int hi] ? $"{name} (L{lo}〜{hi})" : name;
    }

    private static string HintKey(Row row) => row switch
    {
        Row.Seed => "newgame.hint_seed",
        Row.Scenario => "newgame.hint_scenario",
        Row.Clan => "newgame.hint_clan",
        Row.ClanName => "newgame.hint_clan_name",
        Row.Difficulty or Row.Bodies or Row.Revive or Row.Save => "newgame.hint_difficulty",
        Row.Create => "newgame.hint_create",
        Row.Back => "newgame.hint_back",
        _ => "newgame.hint_adjust",
    };
}

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Save;
using System.Text.Json.Nodes;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 物語: 序文、階ごとの名前・入ったときの文・書き付け、最下層の主(名前・出会い・倒したとき、敵の組)。
/// 「AI に書かせる」は、ゲームの新しい世界と同じ WorldWriter で書かせ、空の欄だけ埋めるか、全部書き直す。
/// 書き付けは、イベントの手順 world_doc でその階に出る。
/// </summary>
public sealed class StoryTab : IEditorTab
{
    private readonly EditorContext _context;
    private readonly Action<string, bool> _say;
    private readonly ScrollViewer _view = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
    private readonly CheckBox _overwrite = new() { Content = T("書いてある欄も書き直す", "Overwrite filled fields too"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool _busy;

    public StoryTab(EditorContext context, Action<string, bool> say)
    {
        _context = context;
        _say = say;
    }

    public string Key => "story";
    public string Header => T("物語", "Story");
    public UIElement View => _view;

    public void Reload()
    {
        var panel = new StackPanel { Width = 900, HorizontalAlignment = HorizontalAlignment.Left };
        _view.Content = panel;
        if (_context.Pack is not { } pack) return;
        var text = pack.Text;
        while (text.Floors.Count < pack.Floors.Count) text.Floors.Add(new WorldFloorText());
        if (text.Floors.Count > pack.Floors.Count) text.Floors.RemoveRange(pack.Floors.Count, text.Floors.Count - pack.Floors.Count);

        (_overwrite.Parent as Panel)?.Children.Remove(_overwrite);   // 作り直すたびに、前の置き場所から外す
        var ai = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        ai.Children.Add(Ui.Button(T("AI に書かせる", "Write with AI"), Write, T("ゲームの AI 設定と同じ API キー(資格情報マネージャー)で、Claude に書かせる(お金がかかる)", "Has Claude write it, using the same API key as the game's AI settings (Credential Manager). This costs money.")));
        ai.Children.Add(_overwrite);
        // モデルと API キーは、上の帯の「設定」で
        ai.Children.Add(new TextBlock { Text = T($"モデル: {_context.Settings.AiModel}(設定で変える)", $"Model: {_context.Settings.AiModel} (change it in Settings)"), Foreground = Ui.Dim, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(ai);
        panel.Children.Add(Ui.Note(T("空の欄は、ゲームではパックの決まった文(階のテーマの名前など)になる。主の名前は、主の組の最初の敵に付く。", "Empty fields use the pack's default text in the game (such as the floor theme name). The boss name goes to the first enemy of the boss group.")));

        panel.Children.Add(Ui.Heading(T("序文", "Prologue")));
        panel.Children.Add(Ui.Localized(text.Prologue, v => { text.Prologue = v; _context.MarkEdited(); }, multiline: true, height: 110));

        Bosses(panel, pack);
        Rumors(panel, pack);

        for (int i = 0; i < pack.Floors.Count; i++)
        {
            var f = text.Floors[i];
            panel.Children.Add(Ui.Heading($"B{i + 1}"));
            panel.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(f.Name, v => { f.Name = v; _context.MarkEdited(); })));
            panel.Children.Add(Ui.Row(T("入ったとき", "On entering"), Ui.Localized(f.Enter, v => { f.Enter = v; _context.MarkEdited(); }, multiline: true, height: 40)));
            for (int d = 0; d < f.Documents.Count; d++)
            {
                int index = d;
                panel.Children.Add(Ui.Row(T($"書き付け {d + 1}", $"Note {d + 1}"), Ui.Localized(f.Documents[d], v =>
                {
                    if (v != null) f.Documents[index] = v;
                    _context.MarkEdited();
                }, multiline: true, height: 50)));
            }
            var docs = new WrapPanel();
            docs.Children.Add(Ui.Button(T("書き付けを足す", "Add a note"), () => { f.Documents.Add(LocalizedText.Of("", "")); _context.MarkEdited(); Reload(); }));
            if (f.Documents.Count > 0)
                docs.Children.Add(Ui.Button(T("最後の書き付けを消す", "Remove the last note"), () => { f.Documents.RemoveAt(f.Documents.Count - 1); _context.MarkEdited(); Reload(); }));
            panel.Children.Add(Ui.Row("", docs));
        }
    }

    // ---------------- ボス ----------------

    private static string Mark(string id) => "boss:" + id;

    /// <summary>
    /// ボスの欄: 最下層の主(チェックで迷宮に置く・外す)と、足したボス(置く階を選ぶ)。チェックすると、その階の着く場所から
    /// 一番遠い空いたマスに印 boss:&lt;id&gt; を置く(迷宮のタブの「ボス」の道具で動かせる)。
    /// </summary>
    private void Bosses(StackPanel panel, ScenarioPack pack)
    {
        var text = pack.Text;
        int last = pack.Floors.Count - 1;
        panel.Children.Add(Ui.Heading(T("最下層の主", "The final boss")));
        var finalAt = MazeEdit.FindMark(pack.Floors, Mark(GameSession.FinalBossId));
        panel.Children.Add(PlaceRow(pack, GameSession.FinalBossId, finalAt, fixedFloor: last));
        if (finalAt != null)
        {
            text.Boss ??= new WorldBossText();
            var boss = text.Boss;
            panel.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(boss.Name, v => { boss.Name = v; _context.MarkEdited(); })));
            panel.Children.Add(Ui.Row(T("出会ったとき", "On meeting"), Ui.Localized(boss.Intro, v => { boss.Intro = v; _context.MarkEdited(); }, multiline: true, height: 44)));
            panel.Children.Add(Ui.Row(T("倒したとき", "On defeat"), Ui.Localized(boss.Victory, v => { boss.Victory = v; _context.MarkEdited(); }, multiline: true, height: 44)));
            var finalGroups = pack.BossGroups ?? [];
            panel.Children.Add(Ui.Row(T("敵の組", "Enemies"), GroupsEditor.Create(_context, finalGroups, () =>
            {
                pack.BossGroups = finalGroups.Count == 0 ? null : finalGroups;
                _context.MarkEdited();
            }, MonsterFilter.AroundLevel(pack.LastLevel))));
            panel.Children.Add(Ui.Note(T("最初の行が主。2行目からは従う者で、名前(日本語・English)を付けられる(空なら元の敵の名前)。組が無ければ、新しいゲームのたびに世界のレベル帯から選ぶ。",
                "The first row is the boss. Later rows are attendants and can be named (Japanese, English; empty: the monster's own name). With no groups, one is picked from the world's level band in each new game.")));
        }

        foreach (var o in pack.Bosses.ToList()) ExtraBoss(panel, pack, o);
        var add = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        add.Children.Add(Ui.Button(T("ボスを足す", "Add a boss"), () => AddBoss(pack), T("最下層の主のほかに、途中の階に置くボス", "A boss for a floor on the way, besides the final boss")));
        panel.Children.Add(add);
    }

    /// <summary>AI に頼む噂の数(噂が1つも無いとき、か全部書き直すとき)。</summary>
    private const int AiRumors = 6;

    /// <summary>
    /// 酒場の噂: 文(英日)、条件(全部満たすときだけ出る)、出やすさ。酒場で「噂を聞く」と、聞くたびに1つ選んで出す。
    /// </summary>
    private void Rumors(StackPanel panel, ScenarioPack pack)
    {
        panel.Children.Add(Ui.Heading(T("酒場の噂", "Tavern rumors")));
        panel.Children.Add(Ui.Note(T("酒場の「噂を聞く」で、聞くたびに1つ出す(条件に合う噂と、名のある敵・伝説の噂から、出やすさの重みで選ぶ)。物語が進んだら変わるように、フラグや済んだイベントで出し分けられる。文はかぎ括弧なしで書く(酒場で付ける)。",
            "Each time you \"Hear rumors\" in the tavern, one is told (picked by weight from rumors whose conditions hold, rumors of named monsters and legends). Use flags or finished events to change them as the story goes on. Write them without quotation marks (the tavern adds them).")));
        for (int i = 0; i < pack.Rumors.Count; i++)
        {
            var rumor = pack.Rumors[i];
            var box = new Border
            {
                BorderBrush = Theme.Brush(Theme.Palette.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 4),
            };
            var inner = new StackPanel();
            inner.Children.Add(Ui.Row(T($"噂 {i + 1}", $"Rumor {i + 1}"), Ui.Localized(rumor.Text, v => { rumor.Text = v ?? LocalizedText.Of(""); _context.MarkEdited(); }, multiline: true, height: 40)));
            inner.Children.Add(Ui.Row(T("条件", "Conditions"), ConditionsEditor.Create(rumor.When, _context.MarkEdited)));
            var weight = Ui.Text(rumor.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture), v =>
            {
                if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) && w >= 0)
                {
                    rumor.Weight = w;
                    _context.MarkEdited();
                }
            });
            weight.Width = 70;
            weight.HorizontalAlignment = HorizontalAlignment.Left;
            inner.Children.Add(Ui.Row(T("出やすさ", "Weight"), weight));
            var remove = new WrapPanel();
            remove.Children.Add(Ui.Button(T("この噂を消す", "Remove this rumor"), () =>
            {
                pack.Rumors.Remove(rumor);
                _context.MarkEdited();
                Reload();
            }));
            inner.Children.Add(remove);
            box.Child = inner;
            panel.Children.Add(box);
        }
        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        add.Children.Add(Ui.Button(T("噂を足す", "Add a rumor"), () =>
        {
            int n = pack.Rumors.Count + 1;
            while (pack.Rumors.Any(x => x.Id == $"rumor{n}")) n++;
            pack.Rumors.Add(new RumorDef { Id = $"rumor{n}", Text = LocalizedText.Of("", "") });
            _context.MarkEdited();
            Reload();
        }));
        panel.Children.Add(add);
    }

    /// <summary>
    /// AI が書いた噂を入れる。全部書き直すときは、条件の無い噂を AI の噂に替える(条件を付けた噂は手で書いたものとして残す)。
    /// そうでなければ、噂が1つも無いときだけ入れる。
    /// </summary>
    private static void MergeRumors(ScenarioPack pack, WorldText from, bool overwrite)
    {
        if (from.Rumors.Count == 0) return;
        if (overwrite) pack.Rumors.RemoveAll(x => x.When.Count == 0);
        else if (pack.Rumors.Count > 0) return;
        int n = 1;
        foreach (var text in from.Rumors)
        {
            while (pack.Rumors.Any(x => x.Id == $"rumor{n}")) n++;
            pack.Rumors.Add(new RumorDef { Id = $"rumor{n}", Text = text });
        }
    }

    /// <summary>「迷宮に置く」のチェックと、置く階(fixedFloor なら最下層に決まり)。</summary>
    private WrapPanel PlaceRow(ScenarioPack pack, string id, (int Floor, GridPos Cell)? at, int? fixedFloor = null)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        var box = new CheckBox { Content = T("迷宮に置く", "Place in the maze"), IsChecked = at != null, VerticalAlignment = VerticalAlignment.Center };
        var floors = new ComboBox { ItemsSource = Enumerable.Range(1, pack.Floors.Count).Select(i => $"B{i}").ToList(), Width = 70, Margin = new Thickness(12, 0, 0, 0) };
        floors.SelectedIndex = at?.Floor ?? fixedFloor ?? Math.Max(0, pack.Floors.Count - 2);
        floors.IsEnabled = fixedFloor == null;
        void Place(int floor)
        {
            MazeEdit.RemoveMark(pack.Floors, Mark(id));
            if (MazeEdit.PlaceMarkFar(pack.Floors[floor], Mark(id)) == null) _say(T($"B{floor + 1} に空いたマスがありません", $"B{floor + 1} has no free cell"), true);
            _context.MarkEdited();
            Reload();
        }
        box.Click += (_, _) =>
        {
            if (box.IsChecked == true) Place(floors.SelectedIndex);
            else
            {
                MazeEdit.RemoveMark(pack.Floors, Mark(id));
                _context.MarkEdited();
                Reload();
            }
        };
        floors.SelectionChanged += (_, _) => { if (box.IsChecked == true && floors.SelectedIndex >= 0 && floors.SelectedIndex != at?.Floor) Place(floors.SelectedIndex); };
        row.Children.Add(box);
        row.Children.Add(floors);
        row.Children.Add(new TextBlock
        {
            Text = at is { } a ? T($"({a.Cell.X}, {a.Cell.Y}) に置いてある。迷宮のタブの「ボス」の道具で動かせる", $"at ({a.Cell.X}, {a.Cell.Y}). Move it with the Boss tool on the Maze tab")
                : T("置いていない(このボスとは戦わない)", "not placed (this boss is never fought)"),
            Foreground = Ui.Dim, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    /// <summary>足したボス1体の欄。</summary>
    private void ExtraBoss(StackPanel panel, ScenarioPack pack, JsonObject o)
    {
        string id = (string?)o["id"] ?? "";
        panel.Children.Add(Ui.Heading(T($"ボス: {id}", $"Boss: {id}")));
        panel.Children.Add(PlaceRow(pack, id, MazeEdit.FindMark(pack.Floors, Mark(id))));
        panel.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(NameOf(o), v =>
        {
            o["name"] = v == null ? null : new JsonObject { ["en"] = v.Texts.GetValueOrDefault("en") ?? "", ["ja"] = v.Texts.GetValueOrDefault("ja") ?? "" };
            SyncName(o);
            _context.MarkEdited();
        })));
        foreach (var (key, label, labelEn) in new[] { ("intro", "出会ったとき", "On meeting"), ("victory", "倒したとき", "On defeat") })
        {
            string textKey = $"boss.{id}.{key}";
            panel.Children.Add(Ui.Row(T(label, labelEn), Ui.Localized(TextOf(pack, textKey), v =>
            {
                SetText(pack, textKey, v);
                o[key] = v == null ? null : textKey;
                _context.MarkEdited();
            }, multiline: true, height: 44)));
        }
        var groups = o["groups"]?.Deserialize<List<BossDef.BossGroup>>(ContentLibrary.JsonOptions) ?? [];
        int bossFloor = MazeEdit.FindMark(pack.Floors, Mark(id))?.Floor ?? Math.Max(0, pack.Floors.Count - 2);
        double bossLevel = pack.Floors.Count <= 1 ? pack.LastLevel : pack.FirstLevel + (pack.LastLevel - pack.FirstLevel) * bossFloor / (pack.Floors.Count - 1);
        panel.Children.Add(Ui.Row(T("敵の組", "Enemies"), GroupsEditor.Create(_context, groups, () =>
        {
            o["groups"] = JsonSerializer.SerializeToNode(groups, ContentLibrary.JsonOptions);
            SyncName(o);
            _context.MarkEdited();
        }, MonsterFilter.AroundLevel(bossLevel))));
        panel.Children.Add(Ui.Row(T("HP の倍率", "HP multiplier"), Ui.Text(((double?)o["hp_mult"] ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture), v =>
        {
            if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double m) && m > 0)
            {
                o["hp_mult"] = m;
                _context.MarkEdited();
            }
        })));
        panel.Children.Add(Ui.Row(T("倒したら立てるフラグ", "Flag set on defeat"), Ui.Text((string?)o["reward_flag"], v => { o["reward_flag"] = v.Trim().Length == 0 ? null : v.Trim(); _context.MarkEdited(); })));
        panel.Children.Add(Ui.Note(T("名前は、組の最初の敵(主)に付く。フラグはイベントの条件で使える(倒したあとに開く扉など)。",
            "The name goes on the first enemy of the group. Events can check the flag (a door that opens after the fight, etc.).")));
        var remove = new WrapPanel();
        remove.Children.Add(Ui.Button(T("このボスを消す", "Remove this boss"), () =>
        {
            if (MessageBox.Show(T($"ボス {id} を消しますか？(迷宮の印も消す)", $"Remove boss {id}? (Its marks in the maze go too)"), T("物語", "Story"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            pack.Bosses.Remove(o);
            MazeEdit.RemoveMark(pack.Floors, Mark(id));
            SetText(pack, $"boss.{id}.intro", null);
            SetText(pack, $"boss.{id}.victory", null);
            _context.MarkEdited();
            Reload();
        }));
        panel.Children.Add(remove);
    }

    private void AddBoss(ScenarioPack pack)
    {
        bool Taken(string id) => id == GameSession.FinalBossId || pack.Bosses.Any(b => (string?)b["id"] == id) || _context.BaseData.Bosses.ContainsKey(id);
        int n = 1;
        while (Taken($"boss{n}")) n++;
        var dialog = new InputWindow(T("ボスを足す", "Add a boss"), "id", $"boss{n}", T("迷宮の印 boss:<id> と、文言のキーに使う。", "Used by the maze mark boss:<id> and the text keys."),
            id => id.Length == 0 || !id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') ? T("id は小文字の英数字と _ だけにしてください", "Use lowercase letters, digits and _ only")
                : Taken(id) ? T("その id はもうあります", "That id is already used") : null) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        // はじめは、シナリオの最後のレベル帯に合う敵1体(あとで組を直す)
        var monster = _context.BaseData.Monsters.Values.Where(m => m.Usable && m.Xp > 0 && m.Cr >= pack.LastLevel - 1)
            .OrderBy(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault()?.Id ?? "ogre";
        var o = new JsonObject
        {
            ["id"] = dialog.Value,
            ["name"] = new JsonObject { ["en"] = dialog.Value, ["ja"] = dialog.Value },
            ["groups"] = new JsonArray(new JsonObject { ["monster"] = monster, ["count"] = 1 }),
            ["hp_mult"] = 1.5,
        };
        SyncName(o);
        pack.Bosses.Add(o);
        _context.MarkEdited();
        Reload();
    }

    private static LocalizedText? NameOf(JsonObject o) =>
        o["name"] is JsonObject n ? LocalizedText.Of((string?)n["en"] ?? "", (string?)n["ja"]) : o["name"] is JsonValue v && (string?)v is { } s ? LocalizedText.Of(s) : null;

    /// <summary>ボスの名前を、組の最初の敵の名前にも付ける(戦闘で出す名前)。</summary>
    private static void SyncName(JsonObject o)
    {
        if (o["groups"] is JsonArray { Count: > 0 } g && g[0] is JsonObject first) first["name"] = o["name"]?.DeepClone();
    }

    private static LocalizedText? TextOf(ScenarioPack pack, string key)
    {
        string? ja = pack.Texts.GetValueOrDefault("ja")?.GetValueOrDefault(key), en = pack.Texts.GetValueOrDefault("en")?.GetValueOrDefault(key);
        return ja == null && en == null ? null : LocalizedText.Of(en ?? "", ja ?? "");
    }

    private static void SetText(ScenarioPack pack, string key, LocalizedText? value)
    {
        foreach (var lang in new[] { "ja", "en" })
        {
            string? t = value?.Texts.GetValueOrDefault(lang);
            if (!pack.Texts.TryGetValue(lang, out var texts)) pack.Texts[lang] = texts = [];
            if (string.IsNullOrEmpty(t)) texts.Remove(key);
            else texts[key] = t;
        }
    }

    /// <summary>AI に書かせている途中か。</summary>
    public bool Busy => _busy;

    /// <summary>「AI に書かせる」を押したことにする(--ai-write)。</summary>
    public void WriteNow() => Write();

    /// <summary>AI に書かせる。主の組が空なら、ここで世界のレベル帯から選んで決まった組にする(名前と敵を合わせるため)。</summary>
    private async void Write()
    {
        if (_busy || _context.Pack is not { } pack) return;
        if (_context.Ai(out var noKey) is not { } client)
        {
            _say(noKey!, true);
            return;
        }
        var data = _context.BaseData;
        var dungeon = new Dungeon(pack.Id, pack.Floors);
        var session = GameSession.New(0, DungeonLoader.RequestFor(dungeon, 1), dungeon);
        session.World = pack.Text;
        var enc = EncounterSettings.Load(_context.BaseContent);
        enc.FirstLevel = pack.FirstLevel;
        enc.LastLevel = pack.LastLevel;
        if (pack.BossGroups is not { Count: > 0 })
        {
            session.BuildRosters(data, enc);
            pack.BossGroups = session.BossPicks.GetValueOrDefault(GameSession.FinalBossId);
        }
        var bossInfo = pack.BossGroups is { } bg ? WorldWriter.MonsterDefInfo.Of(data, bg) : null;
        var others = new List<WorldWriter.BossBrief>();
        foreach (var o in pack.Bosses)
            if ((string?)o["id"] is { } id && MazeEdit.FindMark(pack.Floors, Mark(id)) is { } at
                && o["groups"]?.Deserialize<List<BossDef.BossGroup>>(ContentLibrary.JsonOptions) is { } g && WorldWriter.MonsterDefInfo.Of(data, g) is { } info)
                others.Add(new WorldWriter.BossBrief(id, at.Floor, info));
        var prompt = WorldWriter.Prompt(_context.BaseContent)!;
        var dropped = new List<string>();   // AI が書いたが、長すぎるなどで捨てた欄
        int rumors = _overwrite.IsChecked == true || pack.Rumors.Count == 0 ? AiRumors : 0;
        _busy = true;
        _say(T($"{client.Model} に書かせています…", $"Asking {client.Model} to write…"), false);
        var (world, error) = await WorldWriter.WriteAsync(client, data, prompt, pack.Floors.Count, pack.Manifest.Author, CancellationToken.None,
            (ulong)pack.Id.GetHashCode(), session, bossInfo, dropped, others, rumors);
        _busy = false;
        if (world == null)
        {
            _say(T("書けませんでした: ", "Could not write: ") + error, true);
            return;
        }
        Merge(pack.Text, world, _overwrite.IsChecked == true);
        MergeBosses(pack, world, _overwrite.IsChecked == true);
        MergeRumors(pack, world, _overwrite.IsChecked == true);
        // 最下層の主の従う者の名前は、組の名前に入れる(エディタの欄で直せるように)
        if (world.Boss is { } wb && pack.BossGroups is { } fg)
            for (int i = 1; i < fg.Count && i - 1 < wb.Escorts.Count; i++)
                if (wb.Escorts[i - 1] is { } escort && (_overwrite.IsChecked == true || fg[i].Name == null)) fg[i].Name = escort;
        if (pack.Text.Boss != null) pack.Text.Boss.Escorts = [];
        _context.MarkEdited();
        Reload();
        _say(dropped.Count == 0 ? T($"{client.Model} が書きました(保存するまでファイルには書かない)", $"{client.Model} wrote it (nothing is written to files until you save)")
            : T($"{client.Model} が書きました。捨てた欄 {dropped.Count} 個: {string.Join(" / ", dropped)}(もう一度書かせると埋まることがある)", $"{client.Model} wrote it. Dropped fields: {dropped.Count}: {string.Join(" / ", dropped)} (writing again may fill them)"), dropped.Count > 0);
    }

    /// <summary>
    /// AI が書いた途中の階のボスの名前と文を、パックのボスに入れる(overwrite でなければ、空の欄だけ。名前は id のままなら空とみなす)。
    /// </summary>
    private static void MergeBosses(ScenarioPack pack, WorldText from, bool overwrite)
    {
        foreach (var o in pack.Bosses)
        {
            if ((string?)o["id"] is not { } id || !from.OtherBosses.TryGetValue(id, out var got)) continue;
            var name = NameOf(o);
            bool unnamed = name == null || name.Texts.Values.All(t => t.Length == 0 || t == id);
            if (got.Name != null && (overwrite || unnamed))
            {
                o["name"] = new JsonObject { ["en"] = got.Name.Get("en"), ["ja"] = got.Name.Get("ja") };
                SyncName(o);
            }
            if (o["groups"] is JsonArray groups)
                for (int i = 1; i < groups.Count && i - 1 < got.Escorts.Count; i++)
                    if (got.Escorts[i - 1] is { } escort && groups[i] is JsonObject g && (overwrite || g["name"] == null))
                        g["name"] = new JsonObject { ["en"] = escort.Get("en"), ["ja"] = escort.Get("ja") };
            foreach (var (key, text) in new[] { ("intro", got.Intro), ("victory", got.Victory) })
            {
                string textKey = $"boss.{id}.{key}";
                if (text == null || !overwrite && TextOf(pack, textKey) != null) continue;
                SetText(pack, textKey, text);
                o[key] = textKey;
            }
        }
    }

    /// <summary>AI の文を入れる(overwrite でなければ、空の欄だけ)。</summary>
    private static void Merge(WorldText into, WorldText from, bool overwrite)
    {
        T? Pick<T>(T? have, T? got) where T : class => overwrite ? got ?? have : have ?? got;
        into.Model = from.Model;
        into.Prologue = Pick(into.Prologue, from.Prologue);
        if (from.Boss != null)
        {
            into.Boss ??= new WorldBossText();
            into.Boss.Name = Pick(into.Boss.Name, from.Boss.Name);
            into.Boss.Intro = Pick(into.Boss.Intro, from.Boss.Intro);
            into.Boss.Victory = Pick(into.Boss.Victory, from.Boss.Victory);
        }
        for (int i = 0; i < into.Floors.Count && i < from.Floors.Count; i++)
        {
            var a = into.Floors[i];
            var b = from.Floors[i];
            a.Name = Pick(a.Name, b.Name);
            a.Enter = Pick(a.Enter, b.Enter);
            if (overwrite || a.Documents.Count == 0) a.Documents = b.Documents.Count > 0 ? b.Documents : a.Documents;
        }
    }
}

/// <summary>確かめ用の偽の AI: 決まった形の文を返す(--fake-ai)。</summary>
public sealed class FakeAiClient : IAiClient
{
    public string Model => "fake";

    public Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel)
    {
        if (request.System.StartsWith("You write pack data", StringComparison.Ordinal)) return Task.FromResult(Draft(request.User));
        int floors = System.Text.RegularExpressions.Regex.Match(request.User, @"exactly (\d+) entries").Groups[1].Value is { Length: > 0 } n ? int.Parse(n) : 1;
        object T(string en, string ja) => new { en, ja };
        var reply = new
        {
            prologue = T("Under the old mill, a stair goes down that no miller ever built.", "古い粉ひき小屋の下に、どの粉屋も造らなかった階段が続いている。"),
            floors = Enumerable.Range(1, floors).Select(i => new
            {
                name = T($"Floor of Flour {i}", $"粉の階 {i}"),
                enter = T("White dust hangs in the air.", "白い粉が宙に浮いている。"),
                documents = new[] { T("The wheel turns by itself at night.", "夜になると、水車がひとりでに回る。"), T("Do not eat the bread.", "パンを食べるな。") },
            }).ToArray(),
            rumors = Enumerable.Range(1, System.Text.RegularExpressions.Regex.Match(request.User, "exactly (\\d+) things townsfolk say") is { Success: true } rm ? int.Parse(rm.Groups[1].Value) : 0)
                .Select(i => T($"They say the mill wheel turned {i} times last night.", $"ゆうべ、水車が {i} 回ひとりでに回ったそうだ。")).ToArray(),
            bosses = System.Text.RegularExpressions.Regex.Matches(request.User, "- id \"(\\w+)\" on B(\\d+)").Select(m => new
            {
                id = m.Groups[1].Value,
                name = T($"Keeper of B{m.Groups[2].Value}", $"B{m.Groups[2].Value} の番人"),
                intro = T("It blocks the stair.", "それが階段をふさいでいる。"),
                victory = T("The stair is free.", "階段が空いた。"),
                escorts = new[] { T("Stair Rats", "階段の鼠") },
            }).ToArray(),
            boss = new
            {
                name = T("The Miller Below", "下の粉屋"),
                intro = T("Something grinds in the dark.", "闇の中で、何かが挽いている。"),
                victory = T("The grinding stops.", "挽く音が止んだ。"),
                escorts = new[] { T("Flour Ghosts", "粉の亡霊") },
            },
        };
        return Task.FromResult(new AiJsonReply(JsonSerializer.SerializeToElement(reply), null, 0, 0));
    }

    /// <summary>
    /// 下書き(DraftWriter)の偽の返事: 元があれば写して名前か覚え書きを変え、無ければ短い文のイベント。
    /// 希望に「わざと間違える」があれば、最初は知らない手順を混ぜる(直させる流れの確かめ)。
    /// </summary>
    private static AiJsonReply Draft(string user)
    {
        user = user.Replace("\r", "");
        string Line(string label) => System.Text.RegularExpressions.Regex.Match(user, $"^{label}: (.+)$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value.Trim();
        string id = Line("Use this id"), extend = Line("Extend from");
        int at = user.IndexOf("# Base\n", StringComparison.Ordinal), end = user.IndexOf("\n# Request", StringComparison.Ordinal);
        string baseText = at >= 0 && end > at ? user[(at + 7)..end].Trim().Replace("\r", "") : "";
        JsonObject draft;
        if (extend.Length > 0 && !extend.StartsWith('('))
            draft = new JsonObject { ["id"] = id, ["extends"] = extend, ["name"] = new JsonObject { ["en"] = "Fake " + extend, ["ja"] = "偽の" + extend } };
        else if (baseText.StartsWith('{') && JsonNode.Parse(baseText) is JsonObject b)
        {
            draft = b;
            draft["id"] = id;
            if (draft.ContainsKey("steps")) draft["note"] = "偽の AI が写した";
            else draft["name"] = new JsonObject { ["en"] = "Fake copy", ["ja"] = "偽の写し" };
        }
        else draft = new JsonObject
        {
            ["id"] = id, ["place"] = "maze", ["once"] = "game",
            ["steps"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = new JsonObject { ["en"] = "A fake voice echoes.", ["ja"] = "偽の声が響く。" } }),
        };
        if (user.Contains("わざと間違える") && !user.Contains("# Your previous draft") && draft["steps"] is JsonArray steps)
            steps.Add(new JsonObject { ["type"] = "dance" });
        // 「先に動く」は作れない頼み事の見本: 希望と違うところを返す
        string[] differences = user.Contains("先に動") ? ["先制を上げる部品が無いので、逃走 +3 に変えました(偽の AI)。"] : [];
        // 「持ち帰」はゲームに無い品が要る頼み事の見本: 新しい品を一緒に書き、イベントで取り上げる
        string[] items = [];
        if (user.Contains("持ち帰") && draft["steps"] is JsonArray st)
        {
            string itemId = id + "_hammer_head";
            items = [new JsonObject { ["id"] = itemId, ["kind"] = "key", ["name"] = new JsonObject { ["en"] = "Broken Hammer Head", ["ja"] = "折れた槌の頭" }, ["no_random"] = true }.ToJsonString()];
            st.Add(new JsonObject { ["type"] = "take_item", ["id"] = itemId, ["count"] = 1 });
            st.Add(new JsonObject { ["type"] = "gold", ["value"] = 100 });
        }
        var reply = new { json = draft.ToJsonString(), items, note = "偽の AI の下書き(確かめ用)。", differences };
        return new AiJsonReply(JsonSerializer.SerializeToElement(reply), null, 0, 0);
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes;
using GravewardSpiral.Game.Text;

namespace GravewardSpiral.Game;

/// <summary>Scene から使う、ゲーム全体の部品。</summary>
public sealed class GameServices
{
    /// <summary>
    /// 画面の言語(text/&lt;言語&gt;.json の名前)。設定で選ぶ。データの名前は、その言語が無ければ英語を出す。
    /// </summary>
    public static string Language { get; private set; } = "ja";

    /// <summary>選べる言語(パックの text/ にある言語)。名前は各言語の language.name。</summary>
    public IReadOnlyList<string> Languages => Content.Languages;

    /// <summary>言語を変える(画面の文言を読み直す)。</summary>
    public void SetLanguage(string language)
    {
        Language = Content.Languages.Contains(language) ? language : Content.Languages.Contains("en") ? "en" : Content.Languages.FirstOrDefault() ?? "ja";
        Strings = MakeStrings(Content);
    }

    /// <summary>今の言語の文言(足りないキーは英語、日本語の順に探す)。</summary>
    public static Strings MakeStrings(ContentLibrary content) => new(content.Texts(Language), content.Texts("en"), content.Texts("ja"));

    /// <summary>設定の言語が空のときの言語(Windows が日本語なら日本語、それ以外は英語)。</summary>
    public static string DefaultLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";

    /// <summary>起動のときに、使う言語を決める(画面の文言を作る前)。</summary>
    public static void UseLanguage(ContentLibrary content, string? wanted)
    {
        string lang = wanted ?? DefaultLanguage();
        Language = content.Languages.Contains(lang) ? lang : content.Languages.Contains("en") ? "en" : content.Languages.FirstOrDefault() ?? "ja";
    }

    /// <summary>読んでいるパック(標準のパックと、選んだシナリオ)。シナリオを選ぶと読み直す。</summary>
    public required ContentLibrary Content { get; set; }
    public required GameData Data { get; set; }
    public required Strings Strings
    {
        get => _strings;
        set
        {
            _strings = value;
            Rendering.AutoMapRenderer.Strings = value;   // 地図の印の文字も、今の言語で
        }
    }
    private Strings _strings = null!;
    public required TextureCache Textures { get; init; }
    public required AudioPlayer Audio { get; init; }
    public required SaveStore Saves { get; init; }
    public required Action Exit { get; init; }

    /// <summary>遊び方の設定(音量・ログの速さ・線画の色)。</summary>
    public required GameOptions Options { get; init; }

    /// <summary>音量を設定で変えない(起動の引数 --volume で決めたとき)。</summary>
    public bool KeepVolume { get; init; }

    /// <summary>設定を保存するか(確認用の起動 --screenshot では残さない)。</summary>
    public bool PersistOptions { get; init; } = true;

    public void ApplyOptions() => Options.Apply(Audio, KeepVolume);

    public void SaveOptions()
    {
        if (!PersistOptions) return;
        try { Options.Save(AppPaths.UserFile(GameOptions.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"設定を保存できませんでした: {ex.Message}"); }
    }
    public SceneStack Scenes { get; }

    /// <summary>遊んでいるゲーム(タイトル画面と試しの迷宮では null)。</summary>
    public GameSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            AttachItems();
        }
    }

    private GameSession? _session;

    /// <summary>
    /// 持っている品を、いまのデータで使える形にする(よそのシナリオのオリジナルの品は写しから、中身の無い品は「失われた品」で)。
    /// ゲームを入れ替えたときと、シナリオを読み直したときと、名簿を入れ替えたときに呼ぶ。
    /// </summary>
    public void AttachItems()
    {
        if (_session != null) Core.Save.OriginalItems.Attach(Data, Core.Save.OriginalItems.All(_session));
    }

    /// <summary>前の戦闘のログ(キャンプで読み返す。セーブには書かない)。</summary>
    public List<(string Text, Microsoft.Xna.Framework.Color? Color)> LastCombatLog { get; set; } = [];

    /// <summary>戦闘のメニューで、キャラ(id)と画面ごとに前に選んだもの(ゲームを閉じるまで覚える。セーブには書かない)。</summary>
    public Dictionary<(string CharacterId, string Phase), string> CombatCursor { get; } = [];

    /// <summary>combat.log に残す戦闘の数(古いものから消す)。</summary>
    private const int CombatLogKeep = 20;

    /// <summary>
    /// 戦闘のログを %LOCALAPPDATA%/GravewardSpiral/combat.log に足す(「呪文が効かない」などを後で調べるため)。
    /// 戦闘は "=== " で始まる見出しで区切り、新しい CombatLogKeep 戦だけを残す。確かめの起動(設定を残さない)では書かない。
    /// </summary>
    public void AppendCombatLog(string header, IEnumerable<string> lines)
    {
        if (!PersistOptions) return;
        try
        {
            string path = AppPaths.UserFile("combat.log");
            var all = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
            all.Add("=== " + header);
            all.AddRange(lines);
            all.Add("");
            var starts = all.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("=== ")).Select(x => x.i).ToList();
            if (starts.Count > CombatLogKeep) all = all.Skip(starts[^CombatLogKeep]).ToList();
            File.WriteAllLines(path, all);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>デバッグ用の操作を使えるか(起動の引数 --debug)。</summary>
    public bool Debug { get; init; }

    public GameServices() => Scenes = new SceneStack(this);

    /// <summary>ユーザーのシナリオを置く場所(ふだんはドキュメント\GravewardSpiral\Scenarios。自由な迷宮から作ったシナリオもここに書く)。</summary>
    public string UserScenarios { get; init; } = AppPaths.Scenarios;

    /// <summary>置かれているシナリオ(ゲームに入れて配るものと、ユーザーが置いたもの)。</summary>
    public List<ScenarioInfo> FindScenarios(List<string>? problems = null) =>
        ScenarioCatalog.Find([AppPaths.BundledScenarios, UserScenarios], AppPaths.ScenarioCache, problems);

    /// <summary>新しく作った自由な迷宮の、まだ書き出していないシナリオ(id と、イベントを置く前のマップ)。</summary>
    public (string Id, List<string> Maps)? PendingExport { get; set; }

    /// <summary>
    /// 新しい世界を始めて町へ入る前に呼ぶ。自由な迷宮なら、世界をシナリオのパックに書き出し、そのシナリオを遊んでいる形にする
    /// (世界の文が決まってから書くので、AI が書くときは書き終わったあと)。書けなければ、今までどおりシナリオの無い自由な迷宮のまま。
    /// </summary>
    public void FinishNewWorld()
    {
        if (PendingExport is not { } pending || Session is not { } session) return;
        PendingExport = null;
        if (session.Scenario != null) return;
        try
        {
            // マップは今の迷宮(散らしたイベントの印も入れて、出来上がった世界のまま残す)
            Core.Save.ScenarioExport.Write(Path.Combine(UserScenarios, pending.Id), pending.Id, session, Core.Save.ScenarioExport.MapTexts(session.Dungeon), Encounters(), Content, Data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"scenario export failed: {ex.Message}");
            return;
        }
        _scenarioNames = null;
        if (FindScenarios().FirstOrDefault(x => x.Id == pending.Id) is not { } info || UseScenario(info).Count > 0) return;
        session.Scenario = pending.Id;
        session.ScenarioVersion = Core.Save.ScenarioExport.Version;
        Console.WriteLine($"scenario exported: {pending.Id}");
    }

    /// <summary>
    /// シナリオを選ぶ(null なら標準のパックだけ)。パックを読み直し、データと文言を入れ替える。
    /// 読めなければ入れ替えず、問題の一覧を返す。
    /// </summary>
    public List<string> UseScenario(ScenarioInfo? scenario)
    {
        if (Content.Scenario?.Id == scenario?.Id) return [];
        var content = scenario == null ? ContentLibrary.Load(AppPaths.Content) : ContentLibrary.Load(AppPaths.Content, scenario.Directory);
        Core.Maze.DungeonLoader.ValidateAll(content);
        var data = GameData.Load(content);
        if (!content.IsValid) return content.Problems;
        Content = content;
        Data = data;
        Strings = MakeStrings(content);
        Console.WriteLine($"packs: {string.Join(", ", content.Packs.Select(p => p.Id))}");
        AttachItems();
        return [];
    }

    /// <summary>
    /// その階の曲。パックに「名前_b階.mp3」(例: audio/bgm/maze_b3.mp3、battle_b3.mp3)があればそれを、無ければ元の曲を使う。
    /// 曲はパックの audio/bgm に置く(同じ名前の曲を置けば、標準の曲も差し替わる)。
    /// </summary>
    public string BgmFor(string path, int? floor)
    {
        if (floor is not int f || !path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return path;
        string perFloor = $"{path[..^4]}_b{f + 1}.mp3";
        return Audio.Has(perFloor) ? perFloor : path;
    }

    /// <summary>遭遇の設定(今のゲームの世界のレベル帯を当てたもの)。</summary>
    public Core.Combat.EncounterSettings Encounters()
    {
        var s = Core.Combat.EncounterSettings.Load(Content);
        return Session?.WorldSettings(s) ?? s;
    }

    /// <summary>データの名前を、今の言語で。</summary>
    public string Name(LocalizedText? text) => text?.Get(Language) ?? "";

    /// <summary>クランの名前。無ければ、クランの id で決まる名前(clan_names.json)を付ける。</summary>
    public string ClanDisplayName(string? name, string clanId) =>
        name is { Length: > 0 } ? name : Core.Save.ClanNameParts.Load(Content).Pick(clanId, Language) ?? clanId;

    /// <summary>
    /// シナリオに決まった序文があり、世界にまだ序文が無ければ付ける。新しく始めるときと、序文の無いころに始めた世界を続きから読むときに使う。
    /// 決まった文は、パックの worlds.json の文(序文と最下層の主)。古い形の、文言の scenario.prologue も読む。
    /// worlds.json の文は、世界に無い欄だけを埋める(第1部を前の形で始めたセーブにも、主の名前と文が付く)。
    /// </summary>
    public void GiveScenarioPrologue(Core.Save.GameSession session)
    {
        if (Core.Save.WorldPackDef.Of(Content)?.Text is { } fixedText)
        {
            session.World ??= new Core.Ai.WorldText { Model = fixedText.Model };
            session.World.Prologue ??= fixedText.Prologue;
            if (fixedText.Boss is { } boss)
            {
                session.World.Boss ??= new Core.Ai.WorldBossText();
                session.World.Boss.Name ??= boss.Name;
                session.World.Boss.Intro ??= boss.Intro;
                session.World.Boss.Victory ??= boss.Victory;
            }
            return;
        }
        if (session.World != null || !Content.Texts("en").TryGetValue("scenario.prologue", out var en)) return;
        session.World = new Core.Ai.WorldText
        {
            Model = "scenario",
            Prologue = LocalizedText.Of(en, Content.Texts("ja").GetValueOrDefault("scenario.prologue")),
        };
    }

    /// <summary>クランの名前の入力の「おまかせ」(押すたびに clan_names.json から1つ選ぶ)。</summary>
    public Func<string?> ClanNameSuggester() => () => Core.Save.ClanNameParts.Load(Content).Pick(Guid.NewGuid().ToString("N"), Language);

    /// <summary>シナリオの名前(自由な迷宮は「自由な迷宮」、見つからないシナリオは id)。</summary>
    public string ScenarioDisplayName(string? scenarioId)
    {
        if (scenarioId == null) return Strings["slot.free_world"];
        _scenarioNames ??= FindScenarios().ToDictionary(x => x.Id, x => x.Manifest.Name);
        return _scenarioNames.TryGetValue(scenarioId, out var n) && n != null ? Name(n) : scenarioId;
    }

    private Dictionary<string, LocalizedText?>? _scenarioNames;

    /// <summary>AI の窓口。AI の手伝いを切っているか、キーが無ければ null。</summary>
    public Core.Ai.IAiClient? AiClient()
    {
        if (!Options.AiEnabled) return null;
        string? key = Ai.ApiKeyStore.Load(Ai.ApiKeyStore.Claude);
        return key == null ? null : new Core.Ai.ClaudeClient(key, Options.AiModel);
    }

    /// <summary>モンスターの画像(後から読んだパックを優先。art/monsters/&lt;id&gt;.png)。無ければ null。</summary>
    public Microsoft.Xna.Framework.Graphics.Texture2D? MonsterArt(Core.Monsters.MonsterDef def, int depth = 0) =>
        Art("monsters", def.Id)
        ?? (depth < 4 && def.ArtFrom != null && Data.Monsters.GetValueOrDefault(def.ArtFrom) is { } from && from != def ? MonsterArt(from, depth + 1) : null);   // 借りる先をたどる(輪にならないよう4つまで)

    /// <summary>呪文のアイコン(効果の種類ごと。art/spells/&lt;icon&gt;.png)。無ければ null。</summary>
    public Microsoft.Xna.Framework.Graphics.Texture2D? SpellIcon(Core.Magic.SpellDef spell) => Art("spells", spell.Icon ?? "buff");

    /// <summary>クラスのアイコン(art/classes/&lt;id&gt;.png)。無ければ null。</summary>
    public Microsoft.Xna.Framework.Graphics.Texture2D? ClassIcon(string classId) => Art("classes", classId);

    /// <summary>状態のアイコン(art/conditions/&lt;id&gt;.png。poisoned、dead など)。無ければ null。</summary>
    public Microsoft.Xna.Framework.Graphics.Texture2D? ConditionIcon(string id) => Art("conditions", id);

    /// <summary>パックの art/&lt;folder&gt;/&lt;id&gt;.png(後から読んだパックを優先)。無ければ null。</summary>
    private Microsoft.Xna.Framework.Graphics.Texture2D? Art(string folder, string id)
    {
        for (int i = Content.Packs.Count - 1; i >= 0; i--)
        {
            string full = Path.Combine(Content.Packs[i].Directory, "art", folder, id + ".png");
            if (File.Exists(full)) return Textures.GetKeyed(full);
        }
        return null;
    }

    /// <summary>今のゲームをセーブする。失敗したら false(画面で知らせる)。</summary>
    public bool SaveSession()
    {
        if (Session == null || Session.Slot == 0) return false;   // 枠 0 は確認用の起動(セーブしない)
        try
        {
            Core.Save.OriginalItems.Snapshot(Data, Core.Save.OriginalItems.All(Session));   // オリジナルの品は中身も書く
            Saves.Save(Session, Content);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"セーブできませんでした: {ex.Message}");
            return false;
        }
    }
}

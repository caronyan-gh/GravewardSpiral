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
    public GameSession? Session { get; set; }

    /// <summary>デバッグ用の操作を使えるか(起動の引数 --debug)。</summary>
    public bool Debug { get; init; }

    public GameServices() => Scenes = new SceneStack(this);

    /// <summary>置かれているシナリオ(ゲームに入れて配るものと、ユーザーが置いたもの)。</summary>
    public List<ScenarioInfo> FindScenarios(List<string>? problems = null) =>
        ScenarioCatalog.Find([AppPaths.BundledScenarios, AppPaths.Scenarios], AppPaths.ScenarioCache, problems);

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
        return [];
    }

    /// <summary>遭遇の設定(今のゲームの世界のレベル帯を当てたもの)。</summary>
    public Core.Combat.EncounterSettings Encounters()
    {
        var s = Core.Combat.EncounterSettings.Load(Content);
        return Session?.WorldSettings(s) ?? s;
    }

    /// <summary>データの名前を、今の言語で。</summary>
    public string Name(LocalizedText? text) => text?.Get(Language) ?? "";

    /// <summary>モンスターの画像(後から読んだパックを優先。art/monsters/&lt;id&gt;.png)。無ければ null。</summary>
    public Microsoft.Xna.Framework.Graphics.Texture2D? MonsterArt(Core.Monsters.MonsterDef def) => Art("monsters", def.Id);

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

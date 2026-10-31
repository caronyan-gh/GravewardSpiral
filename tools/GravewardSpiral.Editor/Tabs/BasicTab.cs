using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>基本: 名前・説明・作者・版・推奨レベル帯・クランの持ち込み・タイトル画面の絵。</summary>
public sealed class BasicTab(EditorContext context, Action<string, bool> say) : IEditorTab
{
    private readonly ScrollViewer _view = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };

    public string Key => "basic";
    public string Header => T("基本", "Basic");
    public UIElement View => _view;

    public void Reload()
    {
        var panel = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Width = 900 };
        _view.Content = panel;
        if (context.Pack is not { } pack)
        {
            panel.Children.Add(Ui.Heading(T("シナリオを開いていません", "No scenario open")));
            panel.Children.Add(Ui.Note(T("上の「新しく作る」か「開く」(フォルダか zip)から始めます。開くまで、ほかのタブは使えません。シナリオは、ゲームのパックのフォルダ(manifest.json・dungeons.json と maps・events.json など)です。", "Start with \"New\" or \"Open\" (a folder or a zip) above. The other tabs are available once a scenario is open. A scenario is a game pack folder (manifest.json, dungeons.json, maps, events.json, etc.).")));
            return;
        }
        var m = pack.Manifest;
        panel.Children.Add(Ui.Heading(T("シナリオ", "Scenario")));
        panel.Children.Add(Ui.Row("id", new TextBlock { Text = m.Id }));
        panel.Children.Add(Ui.Row(T("フォルダ", "Folder"), new TextBlock { Text = pack.Directory, TextWrapping = TextWrapping.Wrap }));
        panel.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(m.Name, v => { m.Name = v; context.MarkEdited(); })));
        panel.Children.Add(Ui.Row(T("説明", "Description"), Ui.Localized(m.Description, v => { m.Description = v; context.MarkEdited(); }, multiline: true, height: 60)));
        panel.Children.Add(Ui.Row(T("作者", "Author"), Ui.Text(m.Author, v => { m.Author = v.Length == 0 ? null : v; context.MarkEdited(); })));
        panel.Children.Add(Ui.Row(T("版", "Version"), Ui.Text(m.Version, v => { m.Version = v; context.MarkEdited(); })));
        var import = new CheckBox { Content = T("クランを連れて入れる", "Allow bringing a clan in"), IsChecked = m.AllowImport };
        import.Click += (_, _) => { m.AllowImport = import.IsChecked == true; context.MarkEdited(); };
        panel.Children.Add(Ui.Row(T("持ち込み", "Import"), import));
        panel.Children.Add(Ui.Row(T("シリーズ", "Series"), Ui.Text(m.Series, v => { m.Series = v.Trim().Length == 0 ? null : v.Trim(); context.MarkEdited(); })));
        panel.Children.Add(Ui.Row("", Ui.Note(T("同じシリーズの名前(例 wiz_classic)を書いたシナリオどうしは、「クランを連れて入れる」を外していても、そのシリーズのシナリオだけで遊んだクランを連れて入れる。シナリオでクラスを足したシリーズで、I → II → III と同じ人で進むときに使う。連れていく人のクラスや呪文が入る先に無ければ、新しいゲームの画面で止める。",
            "Scenarios with the same series name (e.g. wiz_classic) accept clans that have played only scenarios of that series, even with \"Allow bringing a clan in\" off. Use it for a series that adds its own classes, to go I → II → III with the same characters. If a character's class or spells are missing in the next scenario, the new game screen stops."))));
        panel.Children.Add(Ui.Row(T("始める条件", "Required clears"), RequiredClears(m)));

        panel.Children.Add(Ui.Heading(T("レベル帯", "Level range")));
        panel.Children.Add(Ui.Note(T("B1 の目安のレベルと、最下層の目安のレベル。階ごとの敵の強さと宝はここから決まる(敵と宝のタブで見られる)。強いクランで入ると、帯はクランに合わせて上がる。", "The target level at B1 and at the bottom floor. Enemy strength and treasure per floor follow from these (see the Enemies & loot tab). If a strong clan enters, the range rises to match.")));
        panel.Children.Add(Ui.Row(T("B1 の目安", "B1 target"), Number(pack.FirstLevel, v => pack.FirstLevel = v)));
        panel.Children.Add(Ui.Row(T("最下層の目安", "Bottom floor target"), Number(pack.LastLevel, v => pack.LastLevel = v)));

        TitleArt(panel, pack.Directory);
        Fills(panel, pack);

        panel.Children.Add(Ui.Heading(T("中身", "Contents")));
        panel.Children.Add(Ui.Note(T($"迷宮 {pack.Floors.Count} 階、イベント {pack.Events.Count} 個、", $"{pack.Floors.Count} floor(s), {pack.Events.Count} event(s), ") +
            T($"最下層の主 {(pack.BossGroups is { Count: > 0 } g ? string.Join("・", g.Select(x => $"{x.Monster}×{x.Count}")) : "世界のレベル帯から選ぶ")}",
              $"bottom boss: {(pack.BossGroups is { Count: > 0 } g2 ? string.Join(", ", g2.Select(x => $"{x.Monster}×{x.Count}")) : "chosen from the world's level range")}")));
    }

    /// <summary>
    /// タイトル画面の絵: このシナリオを最後に遊んだ人のタイトル画面に出る、ロゴ(art/title/logo.png)と後ろの絵(art/title/background.png)。
    /// 選ぶと PNG をシナリオのフォルダに写す(保存は要らない)。外すとごみ箱へ。曲は曲のタブの「タイトル」。
    /// </summary>
    private void TitleArt(StackPanel panel, string dir)
    {
        panel.Children.Add(Ui.Heading(T("タイトル画面", "Title screen")));
        panel.Children.Add(Ui.Note(T("このシナリオを最後に遊んだ人のタイトル画面を、ここで選んだ絵にする(無ければ標準のまま)。ロゴは標準のロゴの場所(横 880 × 縦 360 ほど)に、後ろの絵は画面いっぱい(16:9)に、縦横の比を保って出る。メニューのあたりは暗くする。曲は曲のタブの「タイトル」で選ぶ。",
            "Shown on the title screen of anyone who last played this scenario (otherwise the standard one). The logo goes where the standard logo is (about 880 × 360) and the background fills the screen (16:9), both keeping their proportions. The menu area is darkened. Choose the music under \"Title\" on the Music tab.")));
        UIElement Line(string file, string ja, string en)
        {
            string path = Path.Combine(dir, "art", "title", file);
            var row = new WrapPanel();
            bool here = File.Exists(path);
            if (here)
            {
                // 読み込んだら手放す(ファイルを開いたままにしないので、選び直しや外すができる)
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path);
                image.DecodePixelHeight = 90;
                image.EndInit();
                row.Children.Add(new Image { Source = image, Height = 90, MaxWidth = 220, Margin = new Thickness(0, 0, 12, 0) });
            }
            else row.Children.Add(new TextBlock { Text = T("無し(標準のまま)", "None (standard)"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, MinWidth = 160, Margin = new Thickness(0, 0, 12, 0) });
            row.Children.Add(Ui.Button(T("選ぶ", "Choose"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG|*.png" };
                if (dialog.ShowDialog() != true) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(dialog.FileName, path, overwrite: true);
                say(T($"{ja}を入れました", $"Set the {en}"), false);
                Reload();
            }));
            if (here) row.Children.Add(Ui.Button(T("外す", "Remove"), () =>
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                say(T($"{ja}を外しました(ごみ箱へ)", $"Removed the {en} (to the Recycle Bin)"), false);
                Reload();
            }));
            foreach (var button in row.Children.OfType<Button>()) button.VerticalAlignment = VerticalAlignment.Center;   // 絵の高さまで伸ばさない
            return row;
        }
        panel.Children.Add(Ui.Row(T("ロゴ", "Logo"), Line("logo.png", "ロゴ", "logo")));
        panel.Children.Add(Ui.Row(T("後ろの絵", "Background"), Line("background.png", "後ろの絵", "background")));
    }

    /// <summary>
    /// 中身を自動で決める: 出る敵・名のある敵・散らすイベントを、ゲームの新しい世界と同じ選び方で1回選んで、シナリオに書き込む。
    /// 書き込んだものは、どの新しいゲームでもそのまま出る(気に入らないところは、敵と宝・迷宮・イベントのタブで直す)。
    /// </summary>
    private void Fills(StackPanel panel, Core.Save.ScenarioPack pack)
    {
        panel.Children.Add(Ui.Heading(T("中身を自動で決める", "Fill in contents automatically")));
        panel.Children.Add(Ui.Note(T("ゲームの新しい世界と同じ選び方で選んで、シナリオに書き込む(先に保存する)。書き込んだものは、誰が何度遊んでも同じ。気に入らないところは、敵と宝・迷宮・イベントのタブで直す。決めていないものは、遊ぶたびに選ぶ。",
            "Picks these the same way a new game does and writes them into the scenario (saves first). What is written is the same every time anyone plays. Fix what you don't like on the Enemies & loot, Maze and Events tabs. Anything left undecided is picked on each new game.")));
        void Run(Core.Save.ScenarioFill.Parts parts, bool redo)
        {
            var (done, problem) = context.Fill(parts, redo);
            if (problem != null) say(T("シナリオを読めないので埋められません: ", "Cannot fill because the scenario does not load: ") + problem, true);
            else say(done == Core.Save.ScenarioFill.Parts.None ? T("埋めるところはありませんでした(もう決めてある)", "Nothing to fill (already decided)") : T("自動で決めて書き込みました(保存済み)", "Filled in and saved"), false);
        }
        UIElement Line(string state, params Button[] buttons)
        {
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 260 });
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }
        int floors = pack.Floors.Count, decided = Enumerable.Range(0, floors).Count(i => i < pack.Rosters.Count && pack.Rosters[i].Count > 0);
        panel.Children.Add(Ui.Row(T("出る敵", "Monsters"), Line(
            decided == 0 ? T("決めていない(遊ぶたびに選ぶ)", "Not decided (picked each game)") : T($"{decided} / {floors} 階で決めてある", $"Decided on {decided} of {floors} floors"),
            Ui.Button(T("決めていない階を埋める", "Fill undecided floors"), () => Run(Core.Save.ScenarioFill.Parts.Rosters, false)),
            Ui.Button(T("全部選び直す", "Re-pick all"), () => Run(Core.Save.ScenarioFill.Parts.Rosters, true)))));
        panel.Children.Add(Ui.Row(T("名のある敵", "Named monsters"), Line(
            pack.Named == null ? T("決めていない(遊ぶたびに選ぶ)", "Not decided (picked each game)")
                : pack.Named.Count == 0 ? T("いない(決めてある)", "None (decided)") : T($"{pack.Named.Count} 体(決めてある)", $"{pack.Named.Count} (decided)"),
            Ui.Button(pack.Named == null ? T("選んで入れる", "Pick") : T("選び直す", "Re-pick"), () => Run(Core.Save.ScenarioFill.Parts.Named, true)))));
        int marks = pack.Floors.Sum(f => f.AllCells().Count(p => f[p].Mark?.StartsWith(Core.Events.EventPlacer.MarkPrefix) == true));
        panel.Children.Add(Ui.Row(T("散らすイベント", "Scattered events"), Line(
            pack.EventsPlaced ? T($"置いてある(イベントの印 {marks} 個)", $"Placed ({marks} event marks)") : T("置いていない(遊ぶたびに散らす)", "Not placed (scattered each game)"),
            Ui.Button(pack.EventsPlaced ? T("散らし直す", "Re-scatter") : T("散らして置く", "Scatter now"), () => Run(Core.Save.ScenarioFill.Parts.Events, true)),
            Ui.Button(T("散らしたものを消す", "Remove scattered"), () =>
            {
                int n = context.ClearScattered();
                say(T($"散らしたイベントの印を {n} 個消しました(保存済み)", $"Removed {n} scattered event marks (saved)"), false);
            }))));
        var standard = new CheckBox { Content = T("散らすときに、標準のパックのイベントも使う", "Also use the standard pack's events when scattering"), IsChecked = pack.Manifest.StandardEvents, Margin = new Thickness(0, 2, 0, 0) };
        standard.Click += (_, _) => { pack.Manifest.StandardEvents = standard.IsChecked == true; context.MarkEdited(); };
        panel.Children.Add(Ui.Row("", standard));
        panel.Children.Add(Ui.Row("", Ui.Note(T("標準のパックの散らすイベントは 500 ほど(泉・罠・書き付け・連動する仕掛けなど)。外すと、このシナリオの散らすイベントだけを使う。",
            "The standard pack has about 500 scattered events (fountains, traps, notes, linked puzzles…). Uncheck to use only this scenario's scattered events."))));
        var all = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fillAll = Ui.Button(T("決めていないところを全部埋める", "Fill everything undecided"), () => Run(Core.Save.ScenarioFill.Parts.All, false));
        fillAll.Style = (Style)Application.Current.FindResource("AccentButton");
        all.Children.Add(fillAll);
        panel.Children.Add(Ui.Row("", all));
    }

    /// <summary>始める条件: ここに並べたシナリオを全部クリアしたクランを連れていくときだけ、このシナリオを始められる。</summary>
    private StackPanel RequiredClears(Core.Content.PackManifest m)
    {
        var box = new StackPanel();
        var others = context.OtherScenarios();
        string NameOf(Core.Save.RequiredClear r) =>
            Name(r.Name) ?? (others.FirstOrDefault(s => r.Guid != null && s.Manifest.Guid == r.Guid || s.Id == r.Id) is { } sc ? Name(sc.Manifest.Name) : null) ?? r.Id;
        if (m.RequiresClear.Count == 0)
            box.Children.Add(Ui.Note(T("無し(どのクランでも、新しいクランでも始められる)", "None (any clan, or a new one, can start)")));
        foreach (var r in m.RequiresClear.ToList())
        {
            var line = new WrapPanel();
            line.Children.Add(new TextBlock { Text = T($"「{NameOf(r)}」をクリアしたクラン", $"A clan that has cleared \"{NameOf(r)}\""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            line.Children.Add(Ui.Button(T("外す", "Remove"), () => { m.RequiresClear.Remove(r); context.MarkEdited(); Reload(); }));
            box.Children.Add(line);
        }
        var choices = others.Where(s => !m.RequiresClear.Any(r => r.Guid != null && r.Guid == s.Manifest.Guid || r.Id == s.Id)).ToList();
        if (choices.Count > 0)
        {
            var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var pick = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var s in choices) pick.Items.Add(new ComboBoxItem { Content = Name(s.Manifest.Name) ?? s.Id, Tag = s });
            pick.SelectedIndex = 0;
            add.Children.Add(pick);
            add.Children.Add(Ui.Button(T("をクリアしたクランだけにする", "must be cleared first"), () =>
            {
                if ((pick.SelectedItem as ComboBoxItem)?.Tag is not Core.Content.ScenarioInfo s) return;
                m.RequiresClear.Add(new Core.Save.RequiredClear { Guid = s.Manifest.Guid, Id = s.Id, Name = s.Manifest.Name });
                context.MarkEdited();
                Reload();
            }));
            box.Children.Add(add);
        }
        box.Children.Add(Ui.Note(T("クリアは、クランがそのシナリオのエンディングを見たときに付く。条件のあるシナリオは、条件を満たすクランを連れていかないと始められない(新しいクランでは入れない)。",
            "A clear is recorded when a clan sees that scenario's ending. A scenario with conditions can only be started with a clan that meets them (not with a new clan).")));
        if (m.RequiresClear.Count > 0 && !m.AllowImport && m.Series is not { Length: > 0 })
            box.Children.Add(new TextBlock { Text = T("⚠ クランを連れて入れないので、誰も始められません(「クランを連れて入れる」を入れる)", "⚠ Clans cannot be brought in, so nobody can start this (check \"Allow bringing a clan in\")"), Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap });
        return box;
    }

    private TextBox Number(double value, Action<double> set) =>
        Ui.Text(value.ToString(CultureInfo.InvariantCulture), v =>
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 1 && d <= 30)
            {
                set(d);
                context.MarkEdited();
            }
        });
}

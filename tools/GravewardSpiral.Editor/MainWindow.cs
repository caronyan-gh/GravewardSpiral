using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Editor.Tabs;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>エディタの窓。上にシナリオの操作、真ん中にタブ、下に知らせ。</summary>
public sealed class MainWindow : Window
{
    private readonly EditorContext _context;
    private readonly EditorOptions _options;
    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Brush(Theme.Palette.Text) };
    private readonly System.Windows.Shapes.Ellipse _statusDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), Fill = Theme.Brush(Theme.Palette.Good) };
    private readonly TextBlock _packTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _dirty = new()
    {
        CornerRadius = new CornerRadius(9), Background = Theme.Brush("#33FFFFFF"), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = T("● 保存していない直しあり", "● Unsaved changes"), Foreground = Brushes.White, FontSize = 11 },
    };

    /// <summary>タブのアイコン(Segoe Fluent Icons の字)。</summary>
    private static readonly Dictionary<string, string> TabIcons = new()
    {
        ["basic"] = "\uE946", ["maze"] = "\uE707", ["advice"] = "\uEA80", ["events"] = "\uE8BD", ["data"] = "\uE8F1",
        ["music"] = "\uE8D6", ["text"] = "\uE8D2", ["story"] = "\uE736", ["check"] = "\uE73E",
    };
    private readonly List<IEditorTab> _tabList;

    public MainWindow(EditorContext context, EditorOptions options)
    {
        _context = context;
        _options = options;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Icon.ico"));
        Width = 1280;
        Height = 820;
        FontSize = 13;
        Background = Theme.Brush(Theme.Palette.Background);
        _tabList =
        [
            new BasicTab(context, Say),
            new MazeTab(context, options.Floor - 1),
            new AdviceTab(context),
            new EventsTab(context),
            new DataTab(context),
            new MusicTab(context),
            new TextTab(context),
            new StoryTab(context, Say),
            new CheckTab(context, Say, Save),
        ];
        // タブ: アイコンと名前。中身は白いカードに載せる
        foreach (var t in _tabList)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = TabIcons.GetValueOrDefault(t.Key, "\uE8A5"), FontFamily = Theme.Icons, FontSize = 14, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = t.Header, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            var card = new Border
            {
                Child = t.View, Background = Theme.Brush(Theme.Palette.Surface), CornerRadius = new CornerRadius(10), Margin = new Thickness(14, 12, 14, 12),
                BorderBrush = Theme.Brush(Theme.Palette.Border), BorderThickness = new Thickness(1), Padding = new Thickness(4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 1, Opacity = 0.08 },
            };
            System.Windows.Documents.TextElement.SetForeground(card, Theme.Brush(Theme.Palette.Text));   // 選んだタブの青い字を、中身に移さない
            _tabs.Items.Add(new TabItem { Header = header, Content = card, Tag = t.Key });
        }

        // 上の帯: アプリの名前と、開いているシナリオ、操作のボタン
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "\uE8F1", FontFamily = Theme.Icons, FontSize = 22, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = T("GRAVEWARD SPIRAL  シナリオエディタ", "GRAVEWARD SPIRAL  Scenario Editor"), FontSize = 11, Foreground = Theme.Brush("#C9D3F5") });
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(_packTitle);
        nameRow.Children.Add(_dirty);
        names.Children.Add(nameRow);
        title.Children.Add(names);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        commands.Children.Add(BarButton("\uE710", T("新しく作る", "New"), NewScenario));
        commands.Children.Add(BarButton("\uE8B7", T("開く", "Open"), OpenFolder, T("シナリオのフォルダを開く", "Open a scenario folder")));
        commands.Children.Add(BarButton("\uE8C8", T("zip を開く", "Open zip"), OpenZip));
        commands.Children.Add(BarButton("\uEDE1", T("zip に書き出す", "Export zip"), ExportZip));
        commands.Children.Add(BarButton("", T("置き場所", "Folder"), OpenScenariosFolder, T($"シナリオの置き場所({_context.ScenariosDir})をエクスプローラーで開く", $"Open the scenario folder ({_context.ScenariosDir}) in Explorer")));
        commands.Children.Add(BarButton("\uE792", T("名前を付けて保存", "Save As"), SaveAs, T("フォルダを丸ごと写して、新しい id の別のシナリオにする(第1部を元に改造するときなど)", "Copy the whole folder as a new scenario with a new id (e.g. to modify Part 1)")));
        commands.Children.Add(BarButton("\uE74E", T("保存", "Save"), () => Save(), primary: true));
        commands.Children.Add(BarButton("\uE768", T("試し遊び", "Test play"), TestPlay, T("保存してから、ゲームをこのシナリオのおまかせの6人で始める(セーブしない)", "Saves, then starts the game on this scenario with the default party of 6 (nothing is saved)"), primary: true));
        commands.Children.Add(BarButton("", T("設定", "Settings"), OpenSettings, T("画面の言語、AI のモデル、API キー", "Language, AI model, API key")));
        var headerGrid = new Grid();
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(commands);
        var bar = new Border
        {
            Child = headerGrid, Padding = new Thickness(18, 12, 14, 12),
            Background = new LinearGradientBrush(((SolidColorBrush)Theme.Brush(Theme.Palette.HeaderTop)).Color, ((SolidColorBrush)Theme.Brush(Theme.Palette.HeaderBottom)).Color, 0),
        };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_status);
        var statusBar = new Border
        {
            Child = statusRow, Padding = new Thickness(16, 7, 16, 7), Background = Theme.Brush(Theme.Palette.Surface),
            BorderBrush = Theme.Brush(Theme.Palette.Border), BorderThickness = new Thickness(0, 1, 0, 0),
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);
        // タブの帯は白地に下線
        // タブの帯の右端に ? (今のタブの説明)
        var tabsGrid = new Grid();
        tabsGrid.Children.Add(_tabs);
        var help = Help.Button(() => (_tabs.SelectedItem as TabItem)?.Tag as string ?? "");
        help.Margin = new Thickness(0, 6, 16, 0);
        tabsGrid.Children.Add(help);
        var tabsHost = new Border { Child = tabsGrid, Background = Theme.Brush(Theme.Palette.Background) };
        root.Children.Add(tabsHost);
        Content = root;

        context.PackChanged += () =>
        {
            foreach (var t in _tabList) t.Reload();
            UpdateTitle();
        };
        context.Edited += UpdateTitle;
        if (context.Pack != null) foreach (var t in _tabList) t.Reload();   // 作り直した窓(言語を変えたとき)は、開いているシナリオをそのまま見せる
        else _tabList[0].Reload();   // 基本のタブに、始め方を出す
        UpdateTitle();
        Loaded += (_, _) => Start();
        Closing += (_, e) =>
        {
            if (_rebuilding) return;
            if (_options.Screenshot == null && _context.Dirty
                && MessageBox.Show(this, T("保存していない直しがあります。閉じますか？", "You have unsaved changes. Close anyway?"), T("シナリオエディタ", "Scenario Editor"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    private bool _rebuilding;

    /// <summary>設定の窓。言語を変えたら、同じシナリオを開いたまま、窓をその言語で作り直す。</summary>
    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_context.Settings) { Owner = this };
        dialog.ShowDialog();
        if (!dialog.LanguageChanged) return;
        Loc.Lang = _context.Settings.Language;
        _context.DetachViews();
        string? tab = (_tabs.SelectedItem as TabItem)?.Tag as string;
        var next = new MainWindow(_context, _options with { Open = null, NewId = null, Tab = tab, Floor = _tabList.OfType<MazeTab>().First().Floor + 1 })
        {
            Left = Left, Top = Top, Width = Width, Height = Height, WindowState = WindowState,
        };
        Application.Current.MainWindow = next;
        next.Show();
        _rebuilding = true;
        Close();
    }

    /// <summary>起動の引数どおりに開き、画面の保存なら保存して閉じる。</summary>
    private void Start()
    {
        try
        {
            if (_options.NewId is { } id)
            {
                var pack = _context.NewPack(id, _options.Seed == 0 ? (ulong)Random.Shared.NextInt64(1, long.MaxValue) : _options.Seed,
                    _options.Floors > 0 ? _options.Floors : 4, 16, 16);
                _context.Open(pack);
                Save();
                _context.Fill(Core.Save.ScenarioFill.Parts.All, false);   // 出る敵・名のある敵・散らすイベントも、作るときに1回決める
            }
            else if (_options.Open is { } open) OpenPath(open);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            Say(T("開けません: ", "Cannot open: ") + ex.Message, warn: true);
        }
        if (_options.Tab is { } tab)
            _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Tag == tab && t.IsEnabled) ?? _tabs.SelectedItem;
        if (EditorOptions.Select is { Length: >= 2 } sel)
            _tabList.OfType<MazeTab>().First().Select(new Core.Maze.GridPos(sel[0], sel[1]), sel.Length >= 4 ? new Core.Maze.GridPos(sel[2], sel[3]) : new Core.Maze.GridPos(sel[0], sel[1]));
        // 開き直し(名前を付けて保存や、別のシナリオを開いたときと同じく、全部のタブを作り直す)を何度か(--reopen。確かめ用)
        for (int i = 0; i < EditorOptions.Reopen && _context.Pack is { } again; i++) _context.Open(again);
        if (EditorOptions.AiWrite && _context.Pack != null) _tabList.OfType<StoryTab>().First().WriteNow();
        // 画面の保存をしない起動では、AI に頼む窓を希望を入れて開くだけ(書かせない。チャット用のコピーなどを人が試す)
        if (_options.Screenshot == null && EditorOptions.AiDraft is { } askOnly && _context.Pack != null)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => { if (OpenAiDraft(askOnly) is ({ } w, var wish)) w.SetWish(wish); });
        // 画面の保存で検査のタブを開くときは、検査を流してから撮る
        if (_options.Screenshot != null && _options.Tab == "check" && _context.Pack != null) _tabList.OfType<CheckTab>().First().RunChecks();
        if (_options.Screenshot is { } shot)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
            {
                while (_tabList.OfType<StoryTab>().First().Busy) await Task.Delay(100);
                if (EditorOptions.AiDraft is { } ask)
                {
                    // AI に下書きを頼む窓(--ai-draft event:希望 / item@元:希望 / monster@元:希望。確かめ用)
                    var (window, wish) = OpenAiDraft(ask);
                    if (window != null)
                    {
                        window.ShowActivated = false;
                        if (EditorOptions.AiPaste is { } answer) window.PasteNow(wish, File.ReadAllText(answer));
                        else await window.WriteNow(wish);
                        if (window.Kind == Core.Ai.DraftWriter.Kind.Monster) await window.TestStrength();   // モンスターは強さも試して撮る
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        SaveScreenshot(shot, window);
                        File.WriteAllText(Path.ChangeExtension(shot, ".txt"), window.Report());
                        window.CloseQuietly();
                    }
                    Close();
                    return;
                }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (EditorOptions.DataNode is { } data)
                {
                    var parts = data.Split('/');
                    _tabList.OfType<DataTab>().First().SelectForCheck(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 0);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    if (EditorOptions.Scroll > 0)
                    {
                        _tabList.OfType<DataTab>().First().ScrollProps(EditorOptions.Scroll);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    }
                }
                if (EditorOptions.EventNode is { } node)
                {
                    var parts = node.Split('/');
                    _tabList.OfType<EventsTab>().First().SelectForCheck(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 0);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                if (EditorOptions.Scroll > 0 && (_tabs.SelectedItem as TabItem)?.Content is Border { Child: ScrollViewer view })
                {
                    view.ScrollToVerticalOffset(EditorOptions.Scroll);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                if (EditorOptions.Picker is { } kind && kind.StartsWith("data:"))
                {
                    // データの写す元を選ぶ窓(--picker data:items など。確かめ用)
                    var picker = new DataPickerWindow(kind["data:".Length..], _context.BaseData, _context.BaseContent) { Owner = this, ShowActivated = false };
                    picker.Show();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    SaveScreenshot(shot, picker);
                    picker.Close();
                }
                else if (EditorOptions.Picker is { } fields && fields.StartsWith("fields:"))
                {
                    // データのタブの項目の一覧(--picker fields:items など。確かめ用)
                    var window = new FieldListWindow(fields["fields:".Length..], _context.BaseContent, new TextBox()) { Owner = this, ShowActivated = false };
                    window.Show();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    SaveScreenshot(shot, window);
                    window.Close();
                }
                else if (EditorOptions.Picker is { } search)
                {
                    // 標準のイベントから写す窓を、探す言葉を入れて開いて撮る(確かめ用)
                    var picker = new EventPickerWindow(_context.BaseData.Events.Values, search) { Owner = this, ShowActivated = false };
                    picker.Show();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    SaveScreenshot(shot, picker);
                    picker.Close();
                }
                else SaveScreenshot(shot, this);
                Close();
            });
    }

    private void UpdateTitle()
    {
        Title = _context.Pack is { } p
            ? $"{(Loc.Name(p.Manifest.Name) ?? p.Id)}{(_context.Dirty ? " *" : "")} - {T("シナリオエディタ", "Scenario Editor")}"
            : T("シナリオエディタ", "Scenario Editor") + " - GRAVEWARD SPIRAL";
        _packTitle.Text = _context.Pack is { } pack ? $"{Loc.Name(pack.Manifest.Name) ?? pack.Id}" : T("シナリオを開いていません", "No scenario open");
        _dirty.Visibility = _context.Dirty ? Visibility.Visible : Visibility.Collapsed;
        // シナリオを開いていないときは、基本のタブ(開く・新しく作る)だけ
        foreach (var item in _tabs.Items.OfType<TabItem>()) item.IsEnabled = _context.Pack != null || (string)item.Tag == "basic";
        if (_tabs.SelectedItem is TabItem { IsEnabled: false }) _tabs.SelectedIndex = 0;
    }

    /// <summary>上の帯のボタン(アイコンと名前)。primary は白地に青い字で目立たせる。</summary>
    /// <summary>シナリオの置き場所を開く(無ければ作る)。</summary>
    private void OpenScenariosFolder()
    {
        try
        {
            Directory.CreateDirectory(_context.ScenariosDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _context.ScenariosDir, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, T("シナリオエディタ", "Scenario Editor"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Button BarButton(string icon, string label, Action onClick, string? tip = null, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = icon, FontFamily = Theme.Icons, FontSize = 14, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, ToolTip = tip, Margin = new Thickness(6, 0, 0, 0), Style = (Style)Application.Current.FindResource("BarButton") };
        if (primary)
        {
            b.Background = Brushes.White;
            b.Foreground = Theme.Brush(Theme.Palette.Accent);
            b.FontWeight = FontWeights.SemiBold;
        }
        b.Click += (_, _) => onClick();
        return b;
    }

    public void Say(string text, bool warn = false)
    {
        _status.Text = text;
        _statusDot.Fill = Theme.Brush(warn ? Theme.Palette.Warn : Theme.Palette.Good);
    }

    private void NewScenario()
    {
        var dialog = new NewScenarioWindow(_context) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } pack) return;
        _context.Open(pack);
        Save();
        // 出る敵・名のある敵・散らすイベントも、作るときに1回決めて書き込む(気に入らないところは直す)
        var (_, problem) = dialog.Fill == 0 ? (Core.Save.ScenarioFill.Parts.None, null) : _context.Fill(dialog.Fill, false);
        Say(problem == null ? T($"新しいシナリオを作りました(中身も自動で決めた): {pack.Directory}", $"Created a new scenario (contents filled in): {pack.Directory}")
            : T($"新しいシナリオを作りました(中身は埋められない: {problem}): {pack.Directory}", $"Created a new scenario (could not fill: {problem}): {pack.Directory}"), problem != null);
    }

    private void OpenFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("シナリオのフォルダ(manifest.json のあるところ)", "Scenario folder (the one containing manifest.json)"), InitialDirectory = _context.ScenariosDir };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FolderName);
    }

    private void OpenZip()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("シナリオの zip|*.zip", "Scenario zip|*.zip"), InitialDirectory = _context.ScenariosDir };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }

    /// <summary>フォルダか zip を開く。zip は、シナリオの置き場所に同じ名前のフォルダとして展開してから開く。</summary>
    private void OpenPath(string path)
    {
        if (!ConfirmDiscard()) return;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string target = Path.Combine(_context.ScenariosDir, Path.GetFileNameWithoutExtension(path));
            if (Directory.Exists(target))
            {
                Say(T($"展開する先 {target} がもうあります。そのフォルダを開くか、名前を変えてください", $"The destination {target} already exists. Open that folder, or rename it."), warn: true);
                return;
            }
            ZipFile.ExtractToDirectory(path, target);
            // 1つのフォルダに包まれた zip なら、その中を開く
            var inner = Directory.EnumerateDirectories(target).ToList();
            if (!File.Exists(Path.Combine(target, Core.Content.ContentLibrary.ManifestFile)) && inner.Count == 1) path = inner[0];
            else path = target;
        }
        var problems = new List<string>();
        var pack = ScenarioPack.Load(path, problems);
        _context.Open(pack);
        Say(problems.Count == 0 ? T($"開きました: {path}", $"Opened: {path}") : T($"開きました(読めないところ {problems.Count} 件): {string.Join(" / ", problems)}", $"Opened ({problems.Count} unreadable item(s)): {string.Join(" / ", problems)}"), problems.Count > 0);
    }

    private bool ConfirmDiscard() => !_context.Dirty
        || MessageBox.Show(this, T("保存していない直しがあります。捨てて開きますか？", "You have unsaved changes. Discard them and open?"), T("シナリオエディタ", "Scenario Editor"), MessageBoxButton.YesNo) == MessageBoxResult.Yes;

    /// <summary>保存する。できたら true。</summary>
    public bool Save()
    {
        if (_context.Pack is not { } pack) return false;
        try
        {
            pack.Save(_context.BaseContent);
            _context.Dirty = false;
            UpdateTitle();
            Say(T($"保存しました: {pack.Directory}", $"Saved: {pack.Directory}"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say(T("保存できません: ", "Cannot save: ") + ex.Message, warn: true);
            return false;
        }
    }

    /// <summary>名前を付けて保存: 新しい id を聞いて、シナリオの置き場所に別のシナリオとして写す。</summary>
    private void SaveAs()
    {
        if (_context.Pack is not { } pack) return;
        string Dir(string id) => Path.Combine(_context.ScenariosDir, id);
        var dialog = new InputWindow(T("名前を付けて保存", "Save As"), T("新しい id", "New id"), pack.Id + "_copy",
            T($"置き場所: {_context.ScenariosDir}\\<id>。エディタが触らないファイル(文言・はめ込む部屋・絵・曲)も写す。", $"Location: {_context.ScenariosDir}\\<id>. Files the editor does not touch (texts, prefab rooms, art, music) are copied too."),
            id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? T("id は英数字と _ - だけにしてください", "The id may only contain letters, digits, _ and -")
                : Directory.Exists(Dir(id)) ? T("その id のフォルダがもうあります", "A folder with that id already exists") : null) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            pack.SaveAs(Dir(dialog.Value), dialog.Value, _context.BaseContent);
            _context.Open(pack);   // 写した先を開き直す(タブを作り直す)
            Say(T($"名前を付けて保存しました: {pack.Directory}", $"Saved as: {pack.Directory}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say(T("保存できません: ", "Cannot save: ") + ex.Message, warn: true);
        }
    }

    private void ExportZip()
    {
        if (_context.Pack is not { } pack || !Save()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = T("シナリオの zip|*.zip", "Scenario zip|*.zip"), FileName = pack.Id + ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        pack.ExportZip(dialog.FileName);
        Say(T($"zip に書き出しました: {dialog.FileName}(ゲームのタイトル画面に落とすと、ゲームで選べます)", $"Exported zip: {dialog.FileName} (drop it on the game's title screen to pick it in the game)"));
    }

    /// <summary>保存してから、ゲームをこのシナリオで始める(迷宮のタブで選んでいる階から)。</summary>
    private void TestPlay()
    {
        if (_context.Pack is not { } pack || !Save()) return;
        if (FindGame() is not { } exe)
        {
            Say(T("ゲーム(GravewardSpiral.exe)が見つかりません。先にゲームをビルドしてください", "Cannot find the game (GravewardSpiral.exe). Build the game first."), warn: true);
            return;
        }
        int floor = _tabList.OfType<MazeTab>().First().Floor + 1;
        // 試し遊びは使い捨てのセーブの置き場所で起動する(本物のセーブを読んだり、古い形を移し替えたりしない)
        string saves = Path.Combine(Path.GetTempPath(), "GravewardSpiralEditor", "playtest-saves");
        Directory.CreateDirectory(saves);
        Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "--start", $"scenario:{pack.Directory}:{floor}", "--saves", saves }, UseShellExecute = false });
        Say(T($"試し遊び: B{floor} から(セーブしません)", $"Test play: from B{floor} (nothing is saved)"));
    }

    /// <summary>ゲームの exe(エディタの横か、リポジトリのビルド)。</summary>
    private string? FindGame()
    {
        string here = Path.Combine(AppContext.BaseDirectory, "GravewardSpiral.exe");
        if (File.Exists(here)) return here;
        string repo = Path.GetDirectoryName(_context.ContentRoot)!;
        return new[] { "Debug", "Release" }
            .Select(c => Path.Combine(repo, "src", "GravewardSpiral.Game", "bin", c, "net10.0", "GravewardSpiral.exe"))
            .Where(File.Exists).OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    /// <summary>
    /// --ai-draft の「種類[@元]:希望」で、AI に頼む窓を開く(イベントのタブか、データのタブから)。窓と希望を返す。
    /// </summary>
    private (AiDraftWindow? Window, string Wish) OpenAiDraft(string ask)
    {
        int colon = ask.IndexOf(':');
        string[] head = (colon > 0 ? ask[..colon] : ask).Split('@');
        string wish = colon > 0 ? ask[(colon + 1)..] : "";
        var window = head[0] == "event" ? _tabList.OfType<EventsTab>().First().AskAi(head.Length > 1 ? head[1] : null)
            : _tabList.OfType<DataTab>().First().AskAi(fromStandard: head.Length > 1, head.Length > 1 ? head[1] : null, head[0] == "monster" ? "monsters" : "items");
        return (window, wish);
    }

    /// <summary>窓の中身を PNG に書く(確かめ用)。</summary>
    private static void SaveScreenshot(string path, Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

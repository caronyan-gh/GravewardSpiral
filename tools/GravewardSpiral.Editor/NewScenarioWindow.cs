using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>新しいシナリオ: id と、迷宮を自動生成するときのシード・階の数・大きさ(あとで手で直せる)。</summary>
public sealed class NewScenarioWindow : Window
{
    private readonly EditorContext _context;
    private readonly TextBox _id = new() { Text = "my_scenario" };
    private readonly TextBox _seed = new() { Text = Random.Shared.Next(1, 99999999).ToString() };
    private readonly TextBox _floors = new() { Text = "5" };
    private readonly TextBox _width = new() { Text = "16" };
    private readonly TextBox _height = new() { Text = "16" };
    private readonly CheckBox _blank = new() { Content = T("空の階で始める(手で描く)", "Start with blank floors (draw by hand)") };
    private readonly TextBlock _error = new() { Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _rosters = new() { Content = T("階ごとの出る敵を決める", "Decide each floor's monsters"), IsChecked = true };
    private readonly CheckBox _named = new() { Content = T("名のある敵を決める", "Decide named monsters"), IsChecked = true };
    private readonly CheckBox _events = new() { Content = T("イベントを散らして置く", "Scatter events in the maze"), IsChecked = true };
    private readonly CheckBox _standard = new() { Content = T("標準のパックのイベントも使う(泉・罠・書き付けなど 500 ほど)", "Also use the standard pack's events (about 500: fountains, traps, notes…)"), IsChecked = true, Margin = new Thickness(20, 0, 0, 0) };

    /// <summary>作ったあとに自動で決める中身(ScenarioFill)。</summary>
    public ScenarioFill.Parts Fill =>
        (_rosters.IsChecked == true ? ScenarioFill.Parts.Rosters : 0) | (_named.IsChecked == true ? ScenarioFill.Parts.Named : 0) | (_events.IsChecked == true ? ScenarioFill.Parts.Events : 0);

    public ScenarioPack? Result { get; private set; }

    public NewScenarioWindow(EditorContext context)
    {
        _context = context;
        Title = T("新しいシナリオ", "New Scenario");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Ui.Row(T("id(英数字と _)", "id (letters, digits, _)"), _id));
        panel.Children.Add(Ui.Row(T("シード", "Seed"), _seed));
        panel.Children.Add(Ui.Row(T("階の数", "Floors"), _floors));
        panel.Children.Add(Ui.Row(T("幅", "Width"), _width));
        panel.Children.Add(Ui.Row(T("高さ", "Height"), _height));
        panel.Children.Add(_blank);
        panel.Children.Add(Ui.Heading(T("中身も自動で決める", "Also fill in contents")));
        panel.Children.Add(_rosters);
        panel.Children.Add(_named);
        panel.Children.Add(_events);
        panel.Children.Add(_standard);
        _events.Click += (_, _) => _standard.IsEnabled = _events.IsChecked == true;
        panel.Children.Add(Ui.Note(T("決めたものはシナリオに書き込み、誰が遊んでも同じになる(あとで直せる。外したものは遊ぶたびに選ぶ)。", "What is decided is written into the scenario and is the same for everyone (you can change it later; unchecked parts are picked each game).")));
        panel.Children.Add(Ui.Note(T($"置き場所: {context.ScenariosDir}\\<id>", $"Location: {context.ScenariosDir}\\<id>")));
        panel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Ui.Button(T("作る", "Create"), Create));
        buttons.Children.Add(Ui.Button(T("やめる", "Cancel"), () => DialogResult = false));
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void Create()
    {
        string id = _id.Text.Trim();
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
        {
            _error.Text = T("id は英数字と _ - だけにしてください", "The id may only contain letters, digits, _ and -");
            return;
        }
        if (Directory.Exists(Path.Combine(_context.ScenariosDir, id)))
        {
            _error.Text = T("その id のフォルダがもうあります", "A folder with that id already exists");
            return;
        }
        if (!int.TryParse(_floors.Text, out int floors) || floors < 1 || !int.TryParse(_width.Text, out int w) || w < 4
            || !int.TryParse(_height.Text, out int h) || h < 4)
        {
            _error.Text = T("階の数は 1 以上、幅と高さは 4 以上にしてください", "Floors must be at least 1; width and height at least 4");
            return;
        }
        try
        {
            ulong seed = Core.Rng.RngStreams.ParseSeed(_seed.Text) ?? 1;
            Result = _blank.IsChecked == true
                ? ScenarioPack.New(Path.Combine(_context.ScenariosDir, id), id,
                    Enumerable.Range(0, floors).Select(i => Core.Maze.MazeEdit.Blank($"b{i + 1}", w, h)), Environment.UserName)
                : _context.NewPack(id, seed, floors, w, h);
            Result.Manifest.StandardEvents = _standard.IsChecked == true;
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            _error.Text = T("迷宮を作れません: ", "Cannot generate the maze: ") + ex.Message;
        }
    }
}

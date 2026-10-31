using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 検査と書き出し: 保存してから、ゲームと同じ検査をまとめて流す(読み込み、迷宮の辻褄、町から行けるか、ボットが歩けるか、イベント、印)。
/// ボットの通し(CampaignSim)は時間がかかるので、別のボタン。
/// </summary>
public sealed class CheckTab : IEditorTab
{
    /// <summary>ボットの編成(名前、クラス)。</summary>
    private static readonly (string Ja, string En, string[] Classes)[] Parties =
    [
        ("前衛と後衛の6人", "Balanced 6", ["fighter", "fighter", "paladin", "rogue", "cleric", "wizard"]),
        ("前衛の多い6人", "Front-heavy 6", ["fighter", "barbarian", "paladin", "ranger", "monk", "fighter"]),
        ("術者だけの6人", "Casters only 6", ["cleric", "druid", "wizard", "sorcerer", "warlock", "bard"]),
        ("3人", "Party of 3", ["fighter", "cleric", "wizard"]),
    ];

    private readonly ComboBox _party = new() { Width = 170, Margin = new Thickness(6, 0, 0, 0) };

    private readonly EditorContext _context;
    private readonly Action<string, bool> _say;
    private readonly Func<bool> _save;
    private readonly DockPanel _view = new() { Margin = new Thickness(12) };
    private readonly ListBox _results = new();
    private readonly TextBox _runs = new() { Text = "5", Width = 50 };
    private bool _busy;

    public CheckTab(EditorContext context, Action<string, bool> say, Func<bool> save)
    {
        _context = context;
        _say = say;
        _save = save;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Ui.Button(T("保存して検査する", "Save and check"), () => RunChecks()));
        bar.Children.Add(Ui.Button(T("ボットに通しで遊ばせる", "Play through with bots"), RunBots, T("選んだ編成のボットに、このシナリオを最初から最後まで遊ばせる(何分かかかる)。クリアできるか、どこで詰まるかが分かる", "Has a bot party play this scenario from start to finish (takes a few minutes), to see whether it can clear and where it gets stuck")));
        bar.Children.Add(new TextBlock { Text = T("回数", "Runs"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        bar.Children.Add(_runs);
        _party.ItemsSource = Parties.Select(x => T(x.Ja, x.En)).ToList();
        _party.SelectedIndex = 0;
        bar.Children.Add(new TextBlock { Text = T("編成", "Party"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        bar.Children.Add(_party);
        DockPanel.SetDock(bar, Dock.Top);
        _view.Children.Add(bar);
        var note = Ui.Note(T("書き出し: 上の「ファイル」の「zip に書き出す」で zip にする。シナリオの置き場所(「ファイル」の「置き場所を開く」で開く)に置くか、ゲームのタイトル画面に落とすと、ゲームの「新しく始める」のシナリオに出る(エディタで作ったフォルダは、もうそこにある)。",
            "Export: use File > \"Export zip\" above to make a zip. Put it in the scenario folder (File > \"Open scenario folder\" opens it) or drop it on the game's title screen, and it appears in the New Game scenario list (folders made in the editor are already there)."));
        DockPanel.SetDock(note, Dock.Bottom);
        _view.Children.Add(note);
        _view.Children.Add(_results);
    }

    public string Key => "check";
    public string Header => T("検査と書き出し", "Check & Export");
    public UIElement View => _view;

    public void Reload() => _results.Items.Clear();

    /// <summary>注意の色(問題ではないが、意図したものか確かめたいもの)。</summary>
    private static readonly Brush Caution = Theme.Brush("#B7791F");

    /// <summary>結果の1行。頭の「○ / × / ！ / ・」は、通った・問題・注意・知らせのアイコンにする。</summary>
    private void Line(string text, Brush? color = null)
    {
        (string? icon, string body) = text switch
        {
            _ when text.StartsWith("○ ") => ("", text[2..]),
            _ when text.StartsWith("× ") => ("", text[2..]),
            _ when text.StartsWith("！ ") => ("\uE7BA", text[2..]),
            _ when text.StartsWith("・ ") => ("", text[2..]),
            _ => (null, text),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 2, 3) };
        if (icon != null)
            row.Children.Add(new TextBlock { Text = icon, FontFamily = Theme.Icons, FontSize = 13, Foreground = color ?? Ui.Dim, Margin = new Thickness(0, 2, 10, 0) });
        row.Children.Add(new TextBlock
        {
            Text = body, Foreground = color ?? Theme.Brush(Theme.Palette.Text), TextWrapping = TextWrapping.Wrap, MaxWidth = 1100,
            FontWeight = icon == null ? FontWeights.SemiBold : FontWeights.Normal,
        });
        _results.Items.Add(row);
    }

    /// <summary>まとめて検査する。問題の数を返す(画面にも出す)。</summary>
    public int RunChecks()
    {
        _results.Items.Clear();
        if (_context.Pack is not { } pack || !_save()) return -1;
        // ゲームと同じ検査(Core の ScenarioCheck。配布する検査の道具とも同じ)
        var (content, data) = _context.LoadScenario();
        var lines = ScenarioCheck.Run(pack, content, data, _context.BaseContent, T, Lang, EventList.Name);
        foreach (var l in lines)
        {
            switch (l.Level)
            {
                case CheckLevel.Pass: Line("○ " + l.Text, Ui.Good); break;
                case CheckLevel.Problem: Line("× " + l.Text, Ui.Warn); break;
                case CheckLevel.Caution: Line("！ " + l.Text, Caution); break;
                default: Line("・ " + l.Text, Ui.Dim); break;
            }
        }
        if (data == null) return lines.Count(l => l.Level == CheckLevel.Problem);
        int problems = lines.Count(l => l.Level == CheckLevel.Problem);
        int cautions = lines.Count(l => l.Level == CheckLevel.Caution);
        string caution = cautions == 0 ? "" : T($"(注意 {cautions} 件。わざとなら、そのままでよい)", $" ({cautions} caution(s); fine if intended)");
        Line((problems == 0 ? T("検査に通りました", "Check passed") : T($"問題 {problems} 件", $"{problems} problem(s)")) + caution, problems == 0 ? Ui.Good : Ui.Warn);
        _say((problems == 0 ? T("検査に通りました", "Check passed") : T($"検査: 問題 {problems} 件", $"Check: {problems} problem(s)")) + caution, problems > 0);
        return problems;
    }

    /// <summary>ボットに通しで遊ばせる(別のスレッドで。終わったら結果を出す)。</summary>
    private async void RunBots()
    {
        if (_busy || _context.Pack is null || !_save()) return;
        int runs = int.TryParse(_runs.Text, out int r) ? Math.Clamp(r, 1, 50) : 5;
        var (content, data) = _context.LoadScenario();
        if (data == null || !content.IsValid)
        {
            Line(T("× 読み込みの検査を通らないので、ボットは遊べない", "× The loading check failed, so the bots cannot play"), Ui.Warn);
            return;
        }
        _busy = true;
        Line(T($"ボットが {runs} 回遊んでいます…", $"Bots are playing {runs} time(s)…"), Ui.Dim);
        var party = Parties[Math.Max(0, _party.SelectedIndex)];
        var results = await Task.Run(() => Enumerable.Range(1, runs).Select(i => CampaignSim.Run(data, content, party.Classes, (ulong)i)).ToList());
        _busy = false;
        int cleared = results.Count(x => x.Cleared);
        Line(T($"ボット({party.Ja}): {runs} 回中 {cleared} 回クリア({100 * cleared / runs}%)、平均 Lv {results.Average(x => x.AverageLevel):0.0}、全滅 {results.Average(x => x.Wipes):0.0} 回、" +
            $"一番深い階 B{results.Max(x => x.DeepestFloor)}", $"Bots ({party.En}): cleared {cleared} of {runs} runs ({100 * cleared / runs}%), average Lv {results.Average(x => x.AverageLevel):0.0}, wipes {results.Average(x => x.Wipes):0.0} per run, " +
            $"deepest floor B{results.Max(x => x.DeepestFloor)}"), cleared * 2 >= runs ? Ui.Good : Ui.Warn);
        // 理由は括弧の前でまとめ、括弧の中(どの階か、誰が動けないかなど)は1回ずつ出す
        foreach (var why in results.Where(x => !x.Cleared).GroupBy(x => (x.FailReason ?? "?").Split('(')[0]))
        {
            Line(T($"  クリアできなかった理由: {why.Key} ×{why.Count()}", $"  Reason for not clearing: {why.Key} ×{why.Count()}"), Ui.Dim);
            foreach (var f in why.Where(x => x.FailReason?.Contains('(') == true))
                Line("    " + f.FailReason![f.FailReason.IndexOf('(')..], Ui.Dim);
        }
    }
}

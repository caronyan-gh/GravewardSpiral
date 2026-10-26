using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 曲: パックの audio/bgm に置く曲。標準と同じ名前で置くと差し替わり、階ごとの曲(maze_b3.mp3 など)はその階だけで流れる。
/// ほかの名前の曲は、イベントの bgm で流せる。曲はパックの中でだけ使う(持ち出さない)。
/// </summary>
public sealed class MusicTab(EditorContext context) : IEditorTab
{
    /// <summary>標準の曲の名前と、流れるところ。</summary>
    private static readonly (string Name, string Where)[] Standard =
    [
        ("title", "タイトル"), ("town", "町"), ("maze", "迷宮"), ("battle", "ふつうの戦闘"), ("boss", "ボス戦"), ("story", "物語の場面(イベントの bgm で使う)"),
        ("ending", "エンディング"),
    ];

    private readonly ScrollViewer _view = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
    private readonly MediaPlayer _player = new();

    public string Key => "music";
    public string Header => T("曲", "Music");
    public UIElement View => _view;

    private string Dir => Path.Combine(context.Pack!.Directory, "audio", "bgm");

    public void Reload()
    {
        var panel = new StackPanel { Width = 900, HorizontalAlignment = HorizontalAlignment.Left };
        _view.Content = panel;
        if (context.Pack is not { } pack) return;
        panel.Children.Add(Ui.Note(T("曲は mp3。選ぶとパックの audio/bgm に写す。曲はこのシナリオの中でだけ使う(ほかのシナリオへは持ち出さない)。", "Music is mp3. Choosing a file copies it into the pack's audio/bgm. Tracks are used only inside this scenario (they are not carried over to other scenarios).")));
        var stop = Ui.Button(T("止める", "Stop"), () => _player.Stop());
        panel.Children.Add(stop);

        panel.Children.Add(Ui.Heading(T("標準の曲の差し替え", "Replace the standard tracks")));
        foreach (var (name, where) in Standard) panel.Children.Add(Slot(name, WhereOf(where), standard: true));

        panel.Children.Add(Ui.Heading(T("階ごとの曲(無い階は、上の曲)", "Per-floor tracks (floors without one use the track above)")));
        for (int i = 0; i < pack.Floors.Count; i++)
        {
            panel.Children.Add(Slot($"maze_b{i + 1}", T($"B{i + 1} の迷宮", $"B{i + 1} maze"), standard: false));
            panel.Children.Add(Slot($"battle_b{i + 1}", T($"B{i + 1} のふつうの戦闘", $"B{i + 1} normal battle"), standard: false));
        }

        panel.Children.Add(Ui.Heading(T("イベントの曲(ほかの名前の曲。イベントのタブの「曲」で選ぶ)", "Event tracks (other names; pick them with \"Music\" in the Events tab)")));
        var reserved = Standard.Select(s => s.Name).ToHashSet();
        var others = Directory.Exists(Dir)
            ? Directory.EnumerateFiles(Dir, "*.mp3").Select(f => Path.GetFileNameWithoutExtension(f)!)
                .Where(n => !reserved.Contains(n) && !System.Text.RegularExpressions.Regex.IsMatch(n, @"^(maze|battle)_b\d+$")).Order().ToList()
            : [];
        foreach (var name in others) panel.Children.Add(Slot(name, T("イベントの曲", "Event track"), standard: false));
        panel.Children.Add(Ui.Button(T("曲を足す(名前はファイルの名前)", "Add a track (named after the file)"), () =>
        {
            if (Pick() is not { } file) return;
            Directory.CreateDirectory(Dir);
            File.Copy(file, Path.Combine(Dir, Path.GetFileName(file)), overwrite: true);
            Reload();
        }));
    }

    /// <summary>1つの曲の行: 名前、どこで流れるか、今の状態、選ぶ・聞く・消す。</summary>
    private UIElement Slot(string name, string where, bool standard)
    {
        string file = Path.Combine(Dir, name + ".mp3");
        string baseFile = Path.Combine(context.ContentRoot, "srd", "audio", "bgm", name + ".mp3");
        bool here = File.Exists(file);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = name, Width = 120, FontWeight = FontWeights.Bold });
        row.Children.Add(new TextBlock { Text = where, Width = 220 });
        row.Children.Add(new TextBlock
        {
            Text = here ? T("このシナリオの曲", "Scenario track") : standard && File.Exists(baseFile) ? T("標準の曲", "Standard track") : T("元の曲のまま", "Original track"), Width = 140,
            Foreground = here ? Ui.Good : Ui.Dim,
        });
        row.Children.Add(Ui.Button(T("選ぶ", "Choose"), () =>
        {
            if (Pick() is not { } src) return;
            Directory.CreateDirectory(Dir);
            File.Copy(src, file, overwrite: true);
            Reload();
        }));
        string? play = here ? file : standard && File.Exists(baseFile) ? baseFile : null;
        if (play != null) row.Children.Add(Ui.Button(T("聞く", "Play"), () => { _player.Open(new Uri(play)); _player.Play(); }));
        if (here) row.Children.Add(Ui.Button(T("消す", "Delete"), () => { _player.Close(); File.Delete(file); Reload(); }));
        return row;
    }

    /// <summary>標準の曲の「流れるところ」を今の言語で。</summary>
    private static string WhereOf(string ja) => ja switch
    {
        "タイトル" => T(ja, "Title"), "町" => T(ja, "Town"), "迷宮" => T(ja, "Dungeon"), "ふつうの戦闘" => T(ja, "Normal battle"), "ボス戦" => T(ja, "Boss battle"),
        "物語の場面(イベントの bgm で使う)" => T(ja, "Story scenes (used by an event's bgm)"), "エンディング" => T(ja, "Ending"), _ => ja,
    };

    private static string? Pick()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "mp3|*.mp3" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

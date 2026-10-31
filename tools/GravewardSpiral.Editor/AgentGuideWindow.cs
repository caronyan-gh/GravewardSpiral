using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 「AI エージェントで作る」の案内(上の帯のボタン)。エージェント(Claude Code・Codex など)にシナリオを作らせるときの頼み方と、
/// 手引き scenario-kit の置き場所を出し、エクスプローラーで開ける。エディタ自身は AI を呼ばない(頼んだ AI サービスの利用枠を使い、料金がかかることがある。画面でも知らせる)。
/// </summary>
public sealed class AgentGuideWindow : Window
{
    private readonly string _kit;

    /// <param name="kit">scenario-kit のフォルダ(インストール先ではゲームと同じ所、開発ではリポジトリの根)。</param>
    public AgentGuideWindow(string kit)
    {
        _kit = kit;
        Title = T("AI エージェントで作る", "Create with an AI agent");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        var panel = new StackPanel();
        panel.Children.Add(Ui.Heading(T("AI エージェントでシナリオを作る", "Create a scenario with an AI agent")));
        panel.Children.Add(Para(T(
            "Claude Code や Codex などのエージェントに、シナリオを丸ごと作ってもらえます。エージェントは下のフォルダの手引き(README.md)を読み、ゲームと同じ検査の道具(ScenarioCheck.exe)で確かめながら作ります。",
            "An agent such as Claude Code or Codex can build a whole scenario. It reads the guide (README.md) in the folder below and checks its work with ScenarioCheck.exe, the same checks the game uses.")));
        // 料金の注意(「エディタの API キーは使わない」が、無料と読まれないように、頼み方の前に目立たせる)
        var caution = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        caution.Children.Add(new TextBlock { Text = "\uE7BA", FontFamily = Theme.Icons, FontSize = 15, Foreground = Theme.Brush("#B7791F"), Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        caution.Children.Add(new TextBlock
        {
            Text = T("エージェントを利用したシナリオ作成では、お使いの AI サービスの利用枠を消費します。\n契約や使い方によっては料金がかかります。\nエージェントでのシナリオ作成では、登録された API キーは使用しません。",
                "Creating a scenario with an agent uses your AI service's usage allowance.\nDepending on your plan and usage, it may cost money.\nThe API key registered in this editor is not used when an agent creates a scenario."),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Foreground = Theme.Brush(Theme.Palette.Text),
        });
        panel.Children.Add(caution);
        panel.Children.Add(Para(T("頼み方の例(写して使えます):", "Example request (copy and adapt):"), bold: true));
        var request = new TextBox
        {
            Text = T(
                $"{Path.Combine(kit, "README.md")} を読んでから、作業フォルダに新しいシナリオを作って。テーマは〇〇、階は 5 つ、推奨レベルは 1〜5。できたら ScenarioCheck で検査して、問題が無くなるまで直して。",
                $"Read {Path.Combine(kit, "README.md")}, then create a new scenario in a working folder. Theme: ..., 5 floors, recommended levels 1-5. When done, run ScenarioCheck and fix it until there are no problems."),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8, 8, 36, 8),
        };
        // 枠の右下に、よくあるコピーのアイコン(押すとクリップボードへ写し、少しのあいだチェックの印にする)
        var icon = new TextBlock { Text = "\uE8C8", FontFamily = Theme.Icons, FontSize = 14, Foreground = Theme.Brush(Theme.Palette.TextDim) };
        var copy = new Button
        {
            Content = icon, ToolTip = T("コピー", "Copy"), Width = 28, Height = 26, Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4),
        };
        var reset = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        reset.Tick += (_, _) =>
        {
            icon.Text = "\uE8C8";
            reset.Stop();
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(request.Text);
                icon.Text = "\uE73E";   // チェックの印
                copy.ToolTip = T("コピーしました", "Copied");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                copy.ToolTip = T("コピーできませんでした", "Could not copy");
            }
            reset.Start();
        };
        var box = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        box.Children.Add(request);
        box.Children.Add(copy);
        panel.Children.Add(box);
        panel.Children.Add(Para(T(
            "・作業用のフォルダで作らせ、本物のセーブやシナリオの置き場所には触らせないでください(手引きにも書いてあります)。\n・できあがったら、このエディタの「開く」で開いて、中身と検査の結果を確かめてください。\n・エディタで手直しをして、完成度を高めることもできます。\n・シナリオの置き場所(「ファイル」→「置き場所を開く」)に置くと、ゲームで遊べます。",
            "- Have it work in its own folder and keep it away from your real saves and scenario folder (the guide says so too).\n- When it is done, open it here with Open and review the contents and the check results.\n- You can also polish it further in the editor.\n- Put it in the scenario folder (File > Open scenario folder) to play it.")));
        panel.Children.Add(Ui.Note(T($"手引きの置き場所: {kit}", $"Guide folder: {kit}")));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var open = Ui.Button(T("エクスプローラーで開く", "Open in Explorer"), OpenKit);
        open.Style = (Style)Application.Current.FindResource("AccentButton");
        buttons.Children.Add(open);
        buttons.Children.Add(Ui.Button(T("閉じる", "Close"), Close));
        panel.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(20), Child = panel };   // 外側の余白は Margin でなく Padding(画面の保存がずれない)
    }

    private static TextBlock Para(string text, bool bold = false) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Theme.Brush(Theme.Palette.Text),
    };

    /// <summary>手引きのフォルダをエクスプローラーで開く。</summary>
    private void OpenKit()
    {
        if (!Directory.Exists(_kit))
        {
            MessageBox.Show(this, T($"手引きのフォルダが見つかりません: {_kit}", $"Guide folder not found: {_kit}"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _kit, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

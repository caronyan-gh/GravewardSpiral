using System.Windows;
using System.Windows.Controls;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>1行を聞く小さな窓(名前を付けて保存の id など)。</summary>
public sealed class InputWindow : Window
{
    private readonly TextBox _box;
    private readonly TextBlock _error = new() { Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap };
    private readonly Func<string, string?> _check;

    public string Value => _box.Text.Trim();

    /// <param name="check">入れた値の問題(無ければ null)。</param>
    public InputWindow(string title, string label, string value, string? note, Func<string, string?> check)
    {
        _check = check;
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        _box = new TextBox { Text = value };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Ui.Row(label, _box, 110));
        if (note != null) panel.Children.Add(Ui.Note(note));
        panel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = Ui.Button(T("決める", "OK"), Ok);
        ok.Style = (Style)Application.Current.FindResource("AccentButton");
        ok.IsDefault = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(Ui.Button(T("やめる", "Cancel"), () => DialogResult = false));
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    private void Ok()
    {
        if (_check(Value) is { } problem)
        {
            _error.Text = problem;
            return;
        }
        DialogResult = true;
    }
}

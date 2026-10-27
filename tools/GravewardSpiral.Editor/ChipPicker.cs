using System.Windows;
using System.Windows.Controls;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// いくつでも選べる欄: 左にコンボと「足す」、右に選んだものの札(× で外す)。選べるもの(choices)を渡せば、どこでも使える。
/// 足す・外すたびに changed に今の並びを渡し、自分の中だけ描き直す(まわりの欄は作り直さない)。
/// 選べるものに無い値が選んであれば、id のまま札にする(古いデータや、ほかで書いたもの)。
/// </summary>
public sealed class ChipPicker : Grid
{
    private readonly IReadOnlyList<Choice> _choices;
    private readonly List<string> _chosen;
    private readonly Action<List<string>> _changed;
    private readonly ComboBox _pick = new() { IsTextSearchEnabled = true };
    private readonly Button _add;
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Top };

    public ChipPicker(IEnumerable<Choice> choices, IEnumerable<string> chosen, Action<List<string>> changed, double leftWidth = 250)
    {
        _choices = [.. choices];
        _chosen = [.. chosen];
        _changed = changed;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _pick.SetValue(TextSearch.TextPathProperty, nameof(Choice.Display));
        _add = Ui.Button(T("＋ 足す", "+ Add"), Add);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(_add, Dock.Right);
        left.Children.Add(_add);
        left.Children.Add(_pick);
        SetColumn(_chips, 1);
        Children.Add(left);
        Children.Add(_chips);
        Draw();
    }

    private string Name(string key) => _choices.FirstOrDefault(c => c.Key == key) is { Label.Length: > 0 } c ? c.Label : key;

    private void Add()
    {
        if (_pick.SelectedItem is not Choice c || _chosen.Contains(c.Key)) return;
        _chosen.Add(c.Key);
        _changed([.. _chosen]);
        Draw();
    }

    private void Draw()
    {
        var rest = _choices.Where(c => !_chosen.Contains(c.Key)).ToList();
        int keep = Math.Max(0, Math.Min(_pick.SelectedIndex, rest.Count - 1));
        _pick.ItemsSource = rest;
        _pick.SelectedIndex = rest.Count > 0 ? keep : -1;
        _pick.IsEnabled = _add.IsEnabled = rest.Count > 0;
        _chips.Children.Clear();
        foreach (var key in _chosen.ToList())
            _chips.Children.Add(Ui.Chip(Name(key), () =>
            {
                _chosen.Remove(key);
                _changed([.. _chosen]);
                Draw();
            }));
        if (_chosen.Count == 0) _chips.Children.Add(new TextBlock { Text = T("なし", "none"), Foreground = Ui.Dim, Margin = new Thickness(0, 4, 0, 0) });
    }
}

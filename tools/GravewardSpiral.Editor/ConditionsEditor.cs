using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Events;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 条件の欄(噂など): 1行に1つ(種類・でない・id・比べ方・数・消す ×)、下に「＋」で足す。全部を満たすときだけ合う。
/// 数の欄は、item では個数(count)、そのほかは value。使わない欄は灰色にする。
/// </summary>
public static class ConditionsEditor
{
    private static readonly HashSet<string> UsesId = ["flag", "done", "item", "class", "race", "counter", "facing", "entered", "equipped", "status", "zone", "cast", "timer"];
    private static readonly HashSet<string> UsesNumber = ["item", "gold", "level", "counter", "chance", "alive", "depth", "slots", "hp", "timer"];

    public static StackPanel Create(List<EventCondition> conditions, Action changed)
    {
        var panel = new StackPanel();
        void Build()
        {
            panel.Children.Clear();
            if (conditions.Count == 0) panel.Children.Add(new TextBlock { Text = T("条件なし(いつでも)", "No conditions (always)"), Foreground = Ui.Dim, Margin = new Thickness(0, 2, 0, 2) });
            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                var row = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) };
                var types = PartNames.Choices(EventCondition.KnownTypes, PartNames.Conditions);
                var type = new ComboBox { ItemsSource = types, SelectedItem = types.FirstOrDefault(t => t.Key == c.Type), Width = 200 };
                var not = new CheckBox { Content = T("でない", "not"), IsChecked = c.Not, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var id = new TextBox { Text = c.Id ?? "", Width = 150, ToolTip = T("フラグ・イベント・品・クラス・種族・数の id", "Id of the flag, event, item, class, race or counter") };
                var op = new ComboBox { ItemsSource = new[] { ">=", "<=", "==", "!=", ">", "<" }, SelectedItem = c.Op, Width = 56, Margin = new Thickness(4, 0, 0, 0) };
                var number = new TextBox { Text = (c.Type == "item" ? c.Count : c.Value).ToString(), Width = 56, Margin = new Thickness(4, 0, 0, 0),
                    ToolTip = T("数(品は個数、確率と深さは %)", "Number (count for items; % for chance and depth)") };
                void Enable()
                {
                    id.IsEnabled = UsesId.Contains(c.Type);
                    op.IsEnabled = c.Type is "counter" or "hp" or "timer";
                    number.IsEnabled = UsesNumber.Contains(c.Type);
                }
                type.SelectionChanged += (_, _) =>
                {
                    if (type.SelectedItem is not Choice t || t.Key == c.Type) return;
                    c.Type = t.Key;
                    Enable();
                    changed();
                };
                not.Click += (_, _) => { c.Not = not.IsChecked == true; changed(); };
                id.TextChanged += (_, _) => { c.Id = id.Text.Trim().Length == 0 ? null : id.Text.Trim(); changed(); };
                op.SelectionChanged += (_, _) => { if (op.SelectedItem is string o) { c.Op = o; changed(); } };
                number.TextChanged += (_, _) =>
                {
                    if (!int.TryParse(number.Text, out int n)) return;
                    if (c.Type == "item") c.Count = n;
                    else c.Value = n;
                    changed();
                };
                Enable();
                var remove = new Button { Content = "×", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("この条件を消す", "Remove this condition") };
                int index = i;
                remove.Click += (_, _) =>
                {
                    conditions.RemoveAt(index);
                    changed();
                    Build();
                };
                row.Children.Add(type);
                row.Children.Add(not);
                row.Children.Add(id);
                row.Children.Add(op);
                row.Children.Add(number);
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var add = new Button { Content = T("＋ 条件", "+ Condition"), Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) =>
            {
                conditions.Add(new EventCondition { Type = "flag" });
                changed();
                Build();
            };
            panel.Children.Add(add);
        }
        Build();
        return panel;
    }
}

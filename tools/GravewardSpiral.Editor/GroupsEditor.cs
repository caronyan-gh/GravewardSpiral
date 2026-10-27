using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Monsters;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 敵の組の欄: 1行に1組(敵を選ぶ欄・数・名前・消す ×)、下に「＋」で行を足す。最初の行が主(主の名前は、上の名前の欄)。
/// 2行目からの名前は、戦闘で出す従う者の名前(空なら元の敵の名前)。
/// 敵は、標準のパックとこのシナリオのモンスターから選ぶ(上の絞り込みで種類と脅威度を絞り、名前か id を打って探せる)。
/// 組の名前(BossGroup.Name)などは行の敵を替えても残す。boss でなければ(イベントの戦闘)、主の印も名前の欄も無い。
/// </summary>
public static class GroupsEditor
{
    /// <param name="groups">直す組(この並びを直接書き換える)。</param>
    /// <param name="changed">行を足した・消した・敵か数を替えたとき。</param>
    /// <param name="cr">絞り込みの脅威度の初めの範囲(省くと全部)。</param>
    public static StackPanel Create(EditorContext context, List<BossDef.BossGroup> groups, Action changed, (double, double)? cr = null, bool boss = true)
    {
        var filter = new MonsterFilter(context, cr);
        var outer = new StackPanel();
        outer.Children.Add(filter.Bar);
        var panel = new StackPanel();
        outer.Children.Add(panel);
        filter.Changed += () => Build();
        void Build()
        {
            panel.Children.Clear();
            var monsters = filter.Choices([.. groups.Select(g => g.Monster)]);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                // 従う者の名前の欄(ボスの組の2行目から)。ふつうの戦闘では幅を取らない(細い欄で敵を選ぶ欄が潰れないように)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(boss ? 150 : 0) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(boss ? 150 : 0) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var monster = new ComboBox
                {
                    ItemsSource = monsters, IsEditable = true, IsTextSearchEnabled = true,
                    SelectedItem = monsters.FirstOrDefault(m => m.Key == g.Monster),
                    ToolTip = !boss ? null : i == 0 ? T("主(名前はこの敵に付く)", "The boss (the name goes on this enemy)") : T("従う者", "Attendant"),
                };
                if (monster.SelectedItem == null) monster.Text = g.Monster;
                monster.SelectionChanged += (_, _) =>
                {
                    if (monster.SelectedItem is not Choice c || c.Key == g.Monster) return;
                    g.Monster = c.Key;
                    changed();
                };
                // 打った文字は、id か名前で探す(合わなければ元に戻す)
                monster.LostFocus += (_, _) =>
                {
                    string text = monster.Text.Trim();
                    var hit = monsters.FirstOrDefault(m => m.ToString() == text || m.Key == text || m.Label == text);
                    if (hit == null) monster.SelectedItem = monsters.FirstOrDefault(m => m.Key == g.Monster);
                    else if (hit.Key != g.Monster)
                    {
                        g.Monster = hit.Key;
                        monster.SelectedItem = hit;
                        changed();
                    }
                };
                var times = new TextBlock { Text = "×", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var count = new TextBox { Text = g.Count.ToString(), VerticalContentAlignment = VerticalAlignment.Center };
                count.TextChanged += (_, _) =>
                {
                    if (!int.TryParse(count.Text, out int n) || n < 1 || n == g.Count) return;
                    g.Count = Math.Min(n, 9);
                    changed();
                };
                var remove = new Button { Content = "×", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("この組を消す", "Remove this group") };
                int index = i;
                remove.Click += (_, _) =>
                {
                    groups.RemoveAt(index);
                    changed();
                    Build();
                };
                Grid.SetColumn(times, 1);
                Grid.SetColumn(count, 2);
                Grid.SetColumn(remove, 5);
                if (!boss) { }
                else if (i == 0)
                {
                    var note = new TextBlock { Text = T("主(名前は上の欄)", "Boss (name above)"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    Grid.SetColumn(note, 3);
                    Grid.SetColumnSpan(note, 2);
                    row.Children.Add(note);
                }
                else
                {
                    // 従う者の名前(日本語と英語。どちらも空なら元の敵の名前)
                    TextBox? ja = null, en = null;
                    void NameChanged()
                    {
                        g.Name = ja!.Text.Trim().Length == 0 && en!.Text.Trim().Length == 0 ? null : Core.Content.LocalizedText.Of(en!.Text.Trim(), ja.Text.Trim());
                        changed();
                    }
                    ja = new TextBox { Text = g.Name?.Texts.GetValueOrDefault("ja") ?? "", Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("名前(日本語。空なら元の敵の名前)", "Name in Japanese (empty: the monster's own name)") };
                    en = new TextBox { Text = g.Name?.Texts.GetValueOrDefault("en") ?? "", Margin = new Thickness(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("名前(English。空なら元の敵の名前)", "Name in English (empty: the monster's own name)") };
                    ja.TextChanged += (_, _) => NameChanged();
                    en.TextChanged += (_, _) => NameChanged();
                    Grid.SetColumn(ja, 3);
                    Grid.SetColumn(en, 4);
                    row.Children.Add(ja);
                    row.Children.Add(en);
                }
                row.Children.Add(monster);
                row.Children.Add(times);
                row.Children.Add(count);
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var add = new Button { Content = "＋", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = T("組を足す", "Add a group") };
            add.Click += (_, _) =>
            {
                groups.Add(new BossDef.BossGroup { Monster = filter.Choices().FirstOrDefault()?.Key ?? (groups.Count > 0 ? groups[^1].Monster : "goblin"), Count = 1 });
                changed();
                Build();
            };
            panel.Children.Add(add);
        }
        Build();
        return outer;
    }
}

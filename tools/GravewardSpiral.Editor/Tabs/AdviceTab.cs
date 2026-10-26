using System.Windows;
using System.Windows.Controls;
using static GravewardSpiral.Editor.Loc;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Items;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 敵と宝: 階ごとに、さまよう敵をどう選ぶか(自動か、決めた敵だけか)と、宝の等級の割合。
/// 「テーマのまま」は、新しいゲームのたびに、階のテーマ(種類の多さ)でその階に合う敵から選ぶので、テーマの種類を見せる。
/// 「自分で決める」は、種類から何種(新しいゲームのたびに選ぶ)と、必ず出る敵を並べる(Combat.RosterSpec)。レベル帯は基本のタブで変える。
/// </summary>
public sealed class AdviceTab(EditorContext context) : IEditorTab
{
    private const int PartySize = 6, MonstersShown = 8;
    private readonly ScrollViewer _view = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };

    /// <summary>「決める」にしたが、まだ敵を入れていない階(敵が無いあいだは、データの上では自動のまま)。</summary>
    private readonly HashSet<int> _choosing = [];

    public string Key => "advice";
    public string Header => T("敵と宝", "Enemies & loot");
    public UIElement View => _view;

    public void Reload()
    {
        var panel = new StackPanel { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
        _view.Content = panel;
        if (context.Pack is not { } pack) return;
        var data = context.BaseData;
        var enc = EncounterSettings.Load(context.BaseContent);
        enc.FirstLevel = pack.FirstLevel;
        enc.LastLevel = pack.LastLevel;
        var treasure = TreasureSettings.Load(context.BaseContent);
        panel.Children.Add(Ui.Note(T($"迷宮をさまよう敵と、宝の出方を階ごとに見る。目安のレベルは、基本のタブのレベル帯(L{pack.FirstLevel:0.#}〜{pack.LastLevel:0.#})から。",
            $"Wandering monsters and treasure per floor. Target levels come from the level range on the Basic tab (L{pack.FirstLevel:0.#}-{pack.LastLevel:0.#}).")));
        int n = pack.Floors.Count;
        for (int i = 0; i < n; i++)
        {
            double depth = n <= 1 ? 1 : (double)i / (n - 1);
            var theme = FloorAdvisor.ThemeFor(data, depth);
            var card = new StackPanel();

            // 見出し: 階と名前、右に目安のレベル
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var level = new TextBlock { Text = T($"目安 Lv{enc.TargetLevel(depth):0.0}", $"Target Lv{enc.TargetLevel(depth):0.0}"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(level, Dock.Right);
            head.Children.Add(level);
            string name = Loc.Name(pack.Text.Floor(i)?.Name) ?? pack.Floors[i].NameJa ?? Loc.Name(theme?.Name) ?? "";
            head.Children.Add(new TextBlock { Text = $"B{i + 1}  {name}", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            card.Children.Add(head);

            card.Children.Add(Ui.Row(T("出る敵", "Monsters"), Enemies(pack, i, depth, enc, theme), 90));
            card.Children.Add(Ui.Row(T("名のある敵", "Named"), Named(pack, i, depth), 90));
            var rarities = FloorAdvisor.ItemRarities(treasure, depth);
            card.Children.Add(Ui.Row(T("宝", "Treasure"), new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                Text = string.Join("  ・  ", rarities.OrderByDescending(r => r.Value)
                    .Select(r => $"{context.BaseContent.Texts(Lang).GetValueOrDefault("rarity." + r.Key) ?? (r.Key == "mundane" ? T("魔法でない品", "Mundane") : r.Key)} {100 * r.Value:0}%")),
            }, 90));
            panel.Children.Add(new Border
            {
                Child = card, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = Theme.Brush(Theme.Palette.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            });
        }
    }

    /// <summary>出る敵の欄:「テーマのまま」(新しいゲームのたびにテーマから選ぶ)か「自分で決める」(種類から何種と、必ず出る敵)。</summary>
    private StackPanel Enemies(Core.Save.ScenarioPack pack, int floor, double depth, EncounterSettings enc, FloorTheme? theme)
    {
        var roster = pack.RosterOf(floor);
        int size = enc.RosterSize;
        bool choosing = roster.Count > 0 || _choosing.Contains(floor);
        var box = new StackPanel();
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        string group = $"roster{floor}";
        var auto = new RadioButton { Content = T("決めない(遊ぶたびに選ぶ)", "Undecided (picked each game)"), GroupName = group, IsChecked = !choosing, Style = Palette };
        var chosen = new RadioButton { Content = T("決める", "Decide"), GroupName = group, IsChecked = choosing, Style = Palette };
        auto.Checked += (_, _) =>
        {
            _choosing.Remove(floor);
            if (roster.Count > 0)
            {
                roster.Clear();
                context.MarkEdited();
            }
            Reload();
        };
        chosen.Checked += (_, _) =>
        {
            // はじめは、テーマから今選んだ敵を入れておき、そこから直す(ゲームの新しい世界と同じ選び方)
            if (roster.Count == 0)
            {
                roster.AddRange(DrawFromTheme(enc, depth, theme));
                context.MarkEdited();
            }
            _choosing.Add(floor);
            Reload();
        };
        modes.Children.Add(auto);
        modes.Children.Add(chosen);
        box.Children.Add(modes);

        var fits = RosterSpec.FitCounts(context.BaseData, enc, depth, PartySize);
        if (!choosing)
        {
            // テーマ: 種類ごとの多さを言葉で
            string themeName = Loc.Name(theme?.Name) ?? T("テーマなし", "no theme");
            box.Children.Add(Ui.Note(T($"新しいゲームのたびに、テーマ「{themeName}」の種類の多さで、この階に合う敵から {size} 種を選ぶ。",
                $"Each new game picks {size} monsters that fit this floor, weighted by the kinds in the theme \"{themeName}\".")));
            var chips = new WrapPanel();
            if (theme != null)
            {
                var kinds = theme.Types.Where(t => t.Key != "*" && t.Value > 0 && fits.ContainsKey(t.Key)).OrderByDescending(t => t.Value).ToList();
                double total = theme.Types.Where(t => t.Value > 0 && (t.Key == "*" || fits.ContainsKey(t.Key))).Sum(t => t.Value);
                foreach (var (type, w) in kinds)
                    chips.Children.Add(Chip($"{PartNames.Name(PartNames.CreatureTypes, type)}  {Much(w / total)}", null, T($"この階に合う {fits[type]} 種", $"{fits[type]} fit this floor")));
                if (theme.Types.GetValueOrDefault("*") > 0)
                    chips.Children.Add(new TextBlock { Text = T("ほかの種類も少し", "and a few of other kinds"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            }
            else chips.Children.Add(new TextBlock { Text = T("どの種類も同じくらい", "all kinds about equally"), Foreground = Ui.Dim });
            box.Children.Add(chips);
            // 出るかもしれない敵: テーマの重い種類から順に、種類ごとに(重み 0 の種類は出ない)
            var order = (theme?.Types ?? []).Where(t => t.Key != "*" && t.Value > 0).OrderByDescending(t => t.Value).Select(t => t.Key).ToList();
            bool others = theme == null || theme.Types.GetValueOrDefault("*", 1) > 0;
            if (others) order.AddRange(fits.Keys.Where(k => !order.Contains(k)).OrderByDescending(k => fits[k]));
            box.Children.Add(Candidates(enc, depth, order, T($"出るかもしれない敵({order.Sum(k => fits.GetValueOrDefault(k))} 種)", $"Monsters that may appear ({order.Sum(k => fits.GetValueOrDefault(k))})")));
            return box;
        }

        // 決める: 並べた敵(と、種類から何種の行)
        int fixedCount = roster.Count(e => !RosterSpec.IsType(e, out _, out _));
        int drawn = roster.Sum(e => RosterSpec.IsType(e, out _, out int c) ? c : 0);
        box.Children.Add(Ui.Note(drawn == 0 ? T($"この階には、ここに並べた {fixedCount} 種だけが出る(誰が何度遊んでも同じ)。", $"Only these {fixedCount} monsters appear on this floor (the same in every game).")
            : T($"この階に出るのは: 並べた敵 {fixedCount} 種 + 新しいゲームのたびに種類から選ぶ {drawn} 種。", $"This floor gets: {fixedCount} listed monsters + {drawn} picked from kinds in each new game.")));
        var redraw = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        redraw.Children.Add(Ui.Button(T("テーマから選び直す", "Re-pick from the theme"), () =>
        {
            roster.Clear();
            roster.AddRange(DrawFromTheme(enc, depth, theme));
            context.MarkEdited();
            Reload();
        }, T("この階の敵を、ゲームの新しい世界と同じ選び方で選び直す", "Re-pick this floor's monsters the same way a new game does")));
        box.Children.Add(redraw);
        var types = PartNames.Choices(fits.Keys, PartNames.CreatureTypes)
            .Select(c => new Choice(c.Key, T($"{c.Label}(合う {fits[c.Key]} 種)", $"{c.Label} ({fits[c.Key]} fit)"))).ToList();
        // 敵は札を折り返して並べ(× で外す)、種類から何種の行は1行ずつ
        var monsterChips = new WrapPanel();
        foreach (var entry in roster.Where(e => !RosterSpec.IsType(e, out _, out _)).ToList())
            monsterChips.Children.Add(Chip(Label(pack, entry, out bool known), () =>
            {
                roster.Remove(entry);
                if (roster.Count == 0) _choosing.Add(floor);   // 全部外しても「決める」のまま
                context.MarkEdited();
                Reload();
            }, entry, warn: !known));
        box.Children.Add(monsterChips);
        for (int i = 0; i < roster.Count; i++)
        {
            int index = i;
            string entry = roster[i];
            if (!RosterSpec.IsType(entry, out _, out _)) continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            if (RosterSpec.IsType(entry, out string type, out int count))
            {
                var kind = new ComboBox { ItemsSource = types, Width = 220, SelectedItem = types.FirstOrDefault(t => t.Key == type) };
                if (kind.SelectedItem == null) kind.Text = type;
                var num = new TextBox { Text = count.ToString(), Width = 44, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
                kind.SelectionChanged += (_, _) =>
                {
                    if (kind.SelectedItem is not Choice c) return;
                    roster[index] = RosterSpec.Type(c.Key, int.TryParse(num.Text, out int n) ? n : count);
                    context.MarkEdited();
                };
                num.TextChanged += (_, _) =>
                {
                    if (!int.TryParse(num.Text, out int n) || n < 1) return;
                    RosterSpec.IsType(roster[index], out string t, out _);
                    roster[index] = RosterSpec.Type(t, Math.Min(n, 30));
                    context.MarkEdited();
                };
                num.LostFocus += (_, _) => Reload();   // 上の数の合計を直す
                row.Children.Add(kind);
                row.Children.Add(new TextBlock { Text = T("から", "pick"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
                row.Children.Add(num);
                row.Children.Add(new TextBlock { Text = T("種を、毎回選ぶ", "each new game"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
                if (!fits.ContainsKey(type))
                    row.Children.Add(new TextBlock { Text = T("(この階に合う敵がいない)", "(none fit this floor)"), Foreground = Ui.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            }
            var remove = new Button { Content = "×", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("外す", "Remove") };
            remove.Click += (_, _) =>
            {
                roster.RemoveAt(index);
                if (roster.Count == 0) _choosing.Add(floor);   // 全部外しても「自分で決める」のまま
                context.MarkEdited();
                Reload();
            };
            row.Children.Add(remove);
            box.Children.Add(row);
        }
        if (roster.Count == 0) box.Children.Add(new TextBlock { Text = T("まだ何も無い(無いあいだは、遊ぶたびに選ぶ)", "Nothing yet (picked each game until you add something)"), Foreground = Ui.Dim });
        var usedKinds = roster.Select(e => RosterSpec.IsType(e, out string t, out _) ? t : null).OfType<string>().Distinct().ToList();
        if (usedKinds.Count > 0)
            box.Children.Add(Candidates(enc, depth, usedKinds, T($"種類の行から選ばれるかもしれない敵({usedKinds.Sum(k => fits.GetValueOrDefault(k))} 種)", $"Monsters the kind rows may pick ({usedKinds.Sum(k => fits.GetValueOrDefault(k))})")));

        // 足す: 種類から(まだ入れていない、合う敵の多い種類)と、必ず出る敵
        var add = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        add.Children.Add(Ui.Button(T("＋ 種類から選ぶ", "+ Pick from a kind"), () =>
        {
            var used = roster.Select(e => RosterSpec.IsType(e, out string t, out _) ? t : null).OfType<string>().ToHashSet();
            string type = fits.OrderByDescending(f => f.Value).Select(f => f.Key).FirstOrDefault(t => !used.Contains(t)) ?? "beast";
            roster.Add(RosterSpec.Type(type, 2));
            context.MarkEdited();
            Reload();
        }));
        // 必ず出る敵の候補: 絞り込み(初めは、この階に合う敵の脅威度の範囲)。★ はこの階に合う敵
        var fitIds = FloorAdvisor.Monsters(context.BaseData, enc, depth, PartySize, theme);
        var fitSet = fitIds.Select(s => s.Monster.Id).ToHashSet();
        var filter = new MonsterFilter(context, fitIds.Count > 0 ? (fitIds.Min(s => s.Monster.Cr), fitIds.Max(s => s.Monster.Cr)) : null);
        List<Choice> monsters = [];
        var pick = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Width = 300, Margin = new Thickness(0, 2, 0, 2),
            ToolTip = T("★ はこの階に合う敵。名前か id を打って探せる", "★ fits this floor. Type a name or id to search") };
        void Fill()
        {
            monsters = filter.Choices().Where(c => !roster.Contains(c.Key))
                .Select(c => fitSet.Contains(c.Key) ? new Choice(c.Key, "★ " + c.Label) : c)
                .OrderBy(c => fitSet.Contains(c.Key) ? 0 : 1).ToList();
            pick.ItemsSource = monsters;
        }
        filter.Changed += Fill;
        Fill();
        box.Children.Add(add);
        add = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        box.Children.Add(filter.Bar);
        add.Children.Add(pick);
        add.Children.Add(Ui.Button(T("＋ 敵を足す", "+ Add monster"), () =>
        {
            string text = pick.Text.Trim();
            var hit = pick.SelectedItem as Choice ?? monsters.FirstOrDefault(c => c.ToString() == text || c.Key == text || c.Label == text);
            if (hit == null) return;
            roster.Add(hit.Key);
            context.MarkEdited();
            Reload();
        }));
        box.Children.Add(add);
        return box;
    }

    /// <summary>
    /// 種類ごとに、この階に合う敵を並べた開け閉めできる欄(はじめは閉じている)。敵は脅威度の順で、名前と脅威度。
    /// </summary>
    private Expander Candidates(EncounterSettings enc, double depth, IReadOnlyList<string> kinds, string header)
    {
        var fit = FloorAdvisor.Monsters(context.BaseData, enc, depth, PartySize);
        var list = new StackPanel { Margin = new Thickness(4, 4, 0, 0) };
        foreach (var kind in kinds)
        {
            var ms = fit.Where(x => x.Monster.CreatureType == kind).Select(x => x.Monster).OrderBy(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            if (ms.Count == 0) continue;
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            line.Inlines.Add(new System.Windows.Documents.Run($"{PartNames.Name(PartNames.CreatureTypes, kind)}({ms.Count}): ") { FontWeight = FontWeights.SemiBold });
            line.Inlines.Add(new System.Windows.Documents.Run(string.Join("、", ms.Select(m => $"{Loc.Name(m.Name)} {m.Cr:0.##}"))));
            list.Children.Add(line);
        }
        if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = T("合う敵がいない", "No suitable monsters"), Foreground = Ui.Warn });
        list.Children.Add(new TextBlock { Text = T("数は脅威度。", "Numbers are CR."), Foreground = Ui.Dim, FontSize = 11.5 });
        System.Windows.Documents.TextElement.SetForeground(list, Theme.Brush(Theme.Palette.Text));
        return new Expander { Header = header, Content = list, IsExpanded = false, Margin = new Thickness(0, 4, 0, 0), Foreground = Theme.Brush(Theme.Palette.Accent) };
    }

    /// <summary>テーマから今選んだ顔ぶれ(ゲームの新しい世界と同じ Encounters.Roster)。</summary>
    private List<string> DrawFromTheme(EncounterSettings enc, double depth, FloorTheme? theme) =>
        Encounters.Roster(context.BaseData, enc, depth, PartySize, new Core.Rng.Pcg32((ulong)Random.Shared.NextInt64(1, long.MaxValue), 3), theme);

    /// <summary>
    /// 名のある敵の欄: この階にいる名のある敵(ねぐらのマスか、さまようか。噂に乗るか)を札で並べ、× で外し、下の欄から足す。
    /// 名のある敵を決めていないシナリオ(遊ぶたびに抽選)は、決めるボタンだけ。
    /// </summary>
    private StackPanel Named(Core.Save.ScenarioPack pack, int floor, double depth)
    {
        var box = new StackPanel();
        if (pack.Named is not { } named)
        {
            box.Children.Add(Ui.Note(T("決めていない(遊ぶたびに 0〜2 体を抽選)。", "Undecided (0-2 are drawn each game).")));
            var decide = new WrapPanel();
            decide.Children.Add(Ui.Button(T("名のある敵を決める(全部の階)", "Decide named monsters (all floors)"), () =>
            {
                context.Fill(Core.Save.ScenarioFill.Parts.Named, true);
            }, T("保存してから、ゲームの新しい世界と同じ選び方で選んで書き込む", "Saves, then picks them the same way a new game does")));
            decide.Children.Add(Ui.Button(T("いないことにする", "None"), () =>
            {
                pack.Named = [];
                context.MarkEdited();
                Reload();
            }));
            box.Children.Add(decide);
            return box;
        }
        var chips = new WrapPanel();
        foreach (var n in named.Where(n => n.Floor == floor).ToList())
        {
            string name = context.BaseData.NamedMonsters.TryGetValue(n.Id, out var def) ? Loc.Name(def.Name) ?? n.Id : T($"{n.Id}(無い)", $"{n.Id} (missing)");
            string where = n.Lair ? T($"ねぐら ({n.X}, {n.Y})", $"lair ({n.X}, {n.Y})") : T("さまよう", "wanders");
            string rumor = n.Rumored ? T("・噂あり", ", rumored") : "";
            chips.Children.Add(Chip($"{name}  {where}{rumor}", () =>
            {
                named.Remove(n);
                context.MarkEdited();
                Reload();
            }, n.Id, warn: def == null));
        }
        if (chips.Children.Count == 0) chips.Children.Add(new TextBlock { Text = T("いない", "None"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(chips);
        // 足す: この深さに合う名のある敵(合わないものも後ろに)
        var defs = context.BaseData.NamedMonsters.Values
            .OrderBy(d => depth >= d.MinDepth - 1e-9 && depth <= d.MaxDepth + 1e-9 ? 0 : 1).ThenBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => new Choice(d.Id, (depth >= d.MinDepth - 1e-9 && depth <= d.MaxDepth + 1e-9 ? "★ " : "") + (Loc.Name(d.Name) ?? d.Id))).ToList();
        var pick = new ComboBox { ItemsSource = defs, Width = 240, ToolTip = T("★ はこの深さに合う名のある敵", "★ fits this depth") };
        var rumored = new CheckBox { Content = T("噂に乗せる", "Rumored"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        void Add(bool lair)
        {
            if (pick.SelectedItem is not Choice c) return;
            var spawn = new Core.Monsters.NamedSpawn { Id = c.Key, Floor = floor, Lair = lair, Rumored = rumored.IsChecked == true };
            if (lair)
            {
                var cells = Core.Save.GameSession.LairCells(pack.Floors[floor]).Where(p => !named.Any(x => x.Lair && x.Floor == floor && x.X == p.X && x.Y == p.Y)).ToList();
                if (cells.Count == 0) return;
                var at = cells[Random.Shared.Next(cells.Count)];
                (spawn.X, spawn.Y) = (at.X, at.Y);
            }
            named.Add(spawn);
            context.MarkEdited();
            Reload();
        }
        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        add.Children.Add(pick);
        add.Children.Add(rumored);
        add.Children.Add(Ui.Button(T("＋ ねぐらに置く", "+ In a lair"), () => Add(true), T("部屋か行き止まりの空いたマス(迷宮のタブの地図に「名」で出る)", "A free room or dead-end cell (shown as 名 on the Maze tab map)")));
        add.Children.Add(Ui.Button(T("＋ さまよわせる", "+ Wandering"), () => Add(false)));
        box.Children.Add(add);
        return box;
    }

    /// <summary>テーマの種類の重みで、size 種を種類ごとに割り振った項目(この階に合う敵のいる種類だけ。「ほか」は使わない)。</summary>
    private static List<string> FromTheme(FloorTheme? theme, int size, Dictionary<string, int> fits)
    {
        var kinds = (theme?.Types ?? []).Where(t => t.Key != "*" && t.Value > 0 && fits.ContainsKey(t.Key)).OrderByDescending(t => t.Value).ToList();
        if (kinds.Count == 0) kinds = fits.OrderByDescending(f => f.Value).Take(3).Select(f => new KeyValuePair<string, double>(f.Key, 1)).ToList();
        double total = kinds.Sum(k => k.Value);
        var counts = kinds.Select(k => (k.Key, Count: Math.Max(1, (int)Math.Round(size * k.Value / total)))).ToList();
        return counts.Select(c => RosterSpec.Type(c.Key, Math.Min(c.Count, fits.GetValueOrDefault(c.Key, c.Count)))).ToList();
    }

    /// <summary>テーマの中の割合を言葉で。</summary>
    private static string Much(double share) => share >= 0.35 ? T("とても多い", "very common") : share >= 0.2 ? T("多い", "common") : share >= 0.1 ? T("ときどき", "sometimes") : T("少し", "rare");

    private static Style Palette => (Style)Application.Current.FindResource("PaletteButton");

    private string ToolTipOf(string id, double cr) => T($"{id}  脅威度 {cr:0.##}", $"{id}  CR {cr:0.##}");

    /// <summary>敵の名前(標準のパックかシナリオのもの。どちらにも無ければ id と「無い」)。</summary>
    private string Label(Core.Save.ScenarioPack pack, string id, out bool known)
    {
        known = true;
        if (context.BaseData.Monsters.TryGetValue(id, out var m)) return $"{Loc.Name(m.Name)}  CR{m.Cr:0.##}";
        if (pack.Monsters.FirstOrDefault(o => (string?)o["id"] == id) is { } mine)
        {
            var names = mine["name"] as System.Text.Json.Nodes.JsonObject;
            return T($"{(string?)names?[Lang] ?? (string?)names?["ja"] ?? id}(シナリオ)", $"{(string?)names?[Lang] ?? (string?)names?["en"] ?? id} (scenario)");
        }
        known = false;
        return T($"{id}(無い)", $"{id} (missing)");
    }

    /// <summary>敵の札(角の丸い枠。remove があれば × で外せる)。</summary>
    private static Border Chip(string text, Action? remove, string? tip = null, bool warn = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = warn ? Ui.Warn : Theme.Brush(Theme.Palette.Text) });
        if (remove != null)
        {
            var x = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, ToolTip = T("外す", "Remove") };
            x.Click += (_, _) => remove();
            content.Children.Add(x);
        }
        return new Border
        {
            Child = content, Background = Theme.Brush(Theme.Palette.AccentSoft), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, remove != null ? 2 : 10, 3), Margin = new Thickness(0, 2, 6, 2), ToolTip = tip,
        };
    }
}

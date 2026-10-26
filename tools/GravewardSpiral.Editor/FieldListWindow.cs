using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Content;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// データのタブの「項目の一覧」: 品やモンスターの JSON に書ける項目と、その説明(ゲームのコードの説明書き)と、
/// 標準のパックにある書き方の例。中の部品(効果など)も、それぞれの節に出す。ダブルクリックで JSON の欄に書き込む。
/// </summary>
public sealed class FieldListWindow : Window
{
    /// <summary>一覧の1行: 節(どの部品の項目か)、項目の名前、型、説明、例。</summary>
    private sealed record Field(string Section, string Name, string Type, string Doc, string? Example);

    private readonly ListView _list = new() { Height = 480 };
    private readonly List<Field> _all;

    public FieldListWindow(string collection, ContentLibrary content, TextBox target)
    {
        Title = T("項目の一覧", "Field list");
        Width = 980;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        var root = collection == "monsters" ? typeof(Core.Monsters.MonsterDef) : typeof(Core.Items.ItemDef);
        var examples = Examples(content.All(collection).Select(i => i.Data));
        _all = Fields(root, examples);

        var search = new TextBox { Width = 260, ToolTip = T("項目の名前か説明で探す", "Search by field name or description") };
        var section = new ComboBox { Width = 200, Margin = new Thickness(8, 0, 0, 0) };
        var sections = new List<string> { T("すべての部品", "All parts") };
        sections.AddRange(_all.Select(f => f.Section).Distinct());
        section.ItemsSource = sections;
        section.SelectedIndex = 0;
        var count = new TextBlock { Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        var grid = new GridView();
        GridViewColumn Col(string header, string path, double width) => new()
        {
            Header = header, Width = width,
            CellTemplate = CellTemplate(path),
        };
        grid.Columns.Add(Col(T("部品", "Part"), nameof(Field.Section), 110));
        grid.Columns.Add(Col(T("項目", "Field"), nameof(Field.Name), 150));
        grid.Columns.Add(Col(T("型", "Type"), nameof(Field.Type), 110));
        grid.Columns.Add(Col(T("説明", "Description"), nameof(Field.Doc), 380));
        grid.Columns.Add(Col(T("例", "Example"), nameof(Field.Example), 190));
        _list.View = grid;

        void Fill()
        {
            string q = search.Text.Trim();
            string sec = section.SelectedIndex > 0 ? (string)section.SelectedItem : "";
            _list.ItemsSource = _all.Where(f => (sec.Length == 0 || f.Section == sec)
                && (q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Doc.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            count.Text = T($"{_list.Items.Count} 件", $"{_list.Items.Count}");
        }
        search.TextChanged += (_, _) => Fill();
        section.SelectionChanged += (_, _) => Fill();
        string rootSection = root.Name.Replace("Def", "");
        _list.MouseDoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is not Field f) return;
            if (f.Section == rootSection && Insert(target, f)) return;
            // 中の部品の項目(どの効果に入れるかは決められない)と、読めない JSON は、カーソルの位置に書き込む
            string text = $"\"{f.Name}\": {f.Example ?? "null"}";
            int at = Math.Clamp(target.CaretIndex, 0, target.Text.Length);
            if (target.Text[at..].TrimStart().StartsWith('"')) text += ", ";
            target.Text = target.Text.Insert(at, text);
            target.CaretIndex = at + text.Length;
            target.Focus();
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Ui.Note(T("JSON に書ける項目。ダブルクリックで JSON に入れる(一番上の項目は JSON の中に足して、値を選んだ形にする。もうあれば、その値を選ぶ。中の部品(効果・攻撃など)の項目は、どこに入れるか決められないので、カーソルの位置に書き込む)。説明は日本語だけ",
            "Fields you can write in the JSON. Double-click to add one (top-level fields are added to the JSON with the value selected, or just selected if already there; fields of inner parts such as effects or attacks are inserted at the cursor). Descriptions are in Japanese only")));
        var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(search);
        bar.Children.Add(section);
        bar.Children.Add(count);
        panel.Children.Add(bar);
        panel.Children.Add(_list);
        var close = Ui.Button(T("閉じる", "Close"), Close);
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(close);
        Content = panel;
        Fill();
    }

    /// <summary>
    /// 一番上の項目を、JSON の中に入れる(もうあれば、その値を選ぶだけ)。書き直した JSON は整えて、入れた値を選んだ形にする。
    /// JSON が読めなければ false。
    /// </summary>
    private static bool Insert(TextBox target, Field f)
    {
        System.Text.Json.Nodes.JsonObject obj;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(target.Text) is not System.Text.Json.Nodes.JsonObject o) return false;
            obj = o;
        }
        catch (JsonException) { return false; }
        if (!obj.ContainsKey(f.Name))
        {
            System.Text.Json.Nodes.JsonNode? value = null;
            try { if (f.Example != null) value = System.Text.Json.Nodes.JsonNode.Parse(f.Example); }
            catch (JsonException) { }
            obj[f.Name] = value;
            target.Text = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        // その項目の値を選ぶ(すぐ打ち替えられるように)
        int key = target.Text.IndexOf($"\"{f.Name}\":", StringComparison.Ordinal);
        if (key >= 0)
        {
            int start = key + f.Name.Length + 3;
            while (start < target.Text.Length && target.Text[start] == ' ') start++;
            target.Select(start, ValueEnd(target.Text, start) - start);
        }
        target.Focus();
        return true;
    }

    /// <summary>JSON の値の終わり(start から始まる値の、次の位置)。括弧の中と文字列の中の , や } は数えない。</summary>
    private static int ValueEnd(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '[' or '{': depth++; break;
                case ']' or '}' when depth > 0:
                    if (--depth == 0) return i + 1;
                    break;
                case ',' or '}' or ']' or '\r' or '\n' when depth == 0:
                    return i;
            }
        }
        return text.Length;
    }

    /// <summary>折り返す文字の列。</summary>
    private static DataTemplate CellTemplate(string path)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        return new DataTemplate { VisualTree = text };
    }

    /// <summary>型の項目を、中の部品の型もたどって並べる(部品ごとに1つの節)。</summary>
    private static List<Field> Fields(Type root, (Dictionary<string, string> Top, Dictionary<string, string> Inner) examples) =>
        FieldDocs.Walk(root, FieldDocs.Load())
            .Select(f => new Field(f.Owner.Name.Replace("Def", ""), f.Name, TypeName(f.Type), f.Doc, (f.Owner == root ? examples.Top : examples.Inner).GetValueOrDefault(f.Name)))
            .ToList();

    /// <summary>型の、読む人向けの名前(列挙は書ける値の一覧)。</summary>
    private static string TypeName(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u.IsEnum) return string.Join(" / ", Enum.GetNames(u).Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
        if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(List<>)) return T("リスト: ", "list: ") + TypeName(u.GetGenericArguments()[0]);
        if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return T("名前と値の組", "map");
        return u == typeof(string) ? T("文字", "text") : u == typeof(int) ? T("整数", "integer") : u == typeof(double) ? T("数", "number")
            : u == typeof(bool) ? "true / false" : u == typeof(LocalizedText) ? T("英日の文", "en/ja text") : u.Name.Replace("Def", "");
    }

    /// <summary>標準のパックの JSON から、項目ごとに最初に見つかった短い値(書き方の例)。</summary>
    /// <remarks>一番上の項目(品やモンスターそのもの)と、中の部品の項目は、同じ名前でも別に集める。</remarks>
    private static (Dictionary<string, string> Top, Dictionary<string, string> Inner) Examples(IEnumerable<JsonElement> items)
    {
        var top = new Dictionary<string, string>(StringComparer.Ordinal);
        var inner = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonElement e, bool isTop)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var found = isTop ? top : inner;
                foreach (var p in e.EnumerateObject())
                {
                    string raw = p.Value.GetRawText();
                    bool trivial = p.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False || raw is "[]" or "{}" or "0";
                    if (!trivial && raw.Length <= 80 && !found.ContainsKey(p.Name)) found[p.Name] = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");
                    Walk(p.Value, false);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x, false);
        }
        foreach (var i in items) Walk(i, true);
        return (top, inner);
    }
}

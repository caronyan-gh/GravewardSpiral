using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Editor;

/// <summary>画面の部品を作る小さな道具(XAML を使わず、コードで組む)。</summary>
public static class Ui
{
    public static readonly Brush Dim = Theme.Brush(Theme.Palette.TextDim);
    public static readonly Brush Warn = Theme.Brush(Theme.Palette.Warn);
    public static readonly Brush Good = Theme.Brush(Theme.Palette.Good);

    /// <summary>節の見出し(左に青い線)。</summary>
    public static Border Heading(string text) => new()
    {
        BorderBrush = Theme.Brush(Theme.Palette.Accent), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 1, 0, 1),
        Margin = new Thickness(0, 14, 0, 8),
        Child = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush(Theme.Palette.Text) },
    };

    public static TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
    };

    public static Button Button(string text, Action onClick, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>名前の欄と入力の部品を横に並べた行。</summary>
    public static Grid Row(string label, UIElement field, double labelWidth = 150)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(l);
        Grid.SetColumn((FrameworkElement)field, 1);
        g.Children.Add(field);
        return g;
    }

    /// <summary>1行の入力。直すたびに set を呼ぶ。</summary>
    public static TextBox Text(string? value, Action<string> set, bool multiline = false, double height = 0)
    {
        var t = new TextBox { Text = value ?? "", AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (multiline) t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        if (height > 0) t.Height = height;
        t.TextChanged += (_, _) => set(t.Text);
        return t;
    }

    /// <summary>
    /// 英日の組の入力(上が日本語、下が英語)。どちらも空なら null を渡す。
    /// </summary>
    public static StackPanel Localized(LocalizedText? value, Action<LocalizedText?> set, bool multiline = false, double height = 0)
    {
        // 言語ごとの元の文を見せる(Get は無い言語を英語で埋めるので使わない)
        string ja = value?.Texts.GetValueOrDefault("ja") ?? "", en = value?.Texts.GetValueOrDefault("en") ?? "";
        var panel = new StackPanel();
        TextBox? jaBox = null, enBox = null;
        void Changed() => set(jaBox!.Text.Length == 0 && enBox!.Text.Length == 0 ? null : LocalizedText.Of(enBox!.Text, jaBox.Text));
        jaBox = Text(ja, _ => Changed(), multiline, height);
        enBox = Text(en, _ => Changed(), multiline, height);
        panel.Children.Add(Row("日本語", jaBox, 60));
        panel.Children.Add(Row("English", enBox, 60));
        return panel;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12),
    };
}

/// <summary>エディタのタブ。シナリオが替わったら Reload で作り直す。</summary>
public interface IEditorTab
{
    string Key { get; }
    string Header { get; }
    UIElement View { get; }
    void Reload();
}

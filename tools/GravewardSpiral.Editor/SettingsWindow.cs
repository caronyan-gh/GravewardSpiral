using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Game.Ai;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// エディタの設定: 画面の言語、AI のモデル、Claude の API キー。設定はゲームと別に持つ(EditorSettings)。
/// API キーだけはゲームと共通(Windows の資格情報マネージャー GravewardSpiral/claude)。今のキーは先頭と末尾だけ見せ、
/// 入れたキーは画面にも設定ファイルにも出さない。
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly EditorSettings _settings;
    private readonly PasswordBox _key = new() { Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBlock _current = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>言語を変えた(閉じたあと、窓を作り直す)。</summary>
    public bool LanguageChanged { get; private set; }

    public SettingsWindow(EditorSettings settings)
    {
        _settings = settings;
        Title = T("設定", "Settings");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        var panel = new StackPanel { Margin = new Thickness(18) };

        panel.Children.Add(Ui.Heading(T("画面", "Display")));
        var lang = new ComboBox { ItemsSource = new[] { new Choice("ja", "日本語"), new Choice("en", "English") }, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        lang.SelectedItem = ((Choice[])lang.ItemsSource).First(c => c.Key == settings.Language);
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedItem is not Choice c || c.Key == _settings.Language) return;
            _settings.Language = c.Key;
            _settings.Save();
            LanguageChanged = true;
        };
        panel.Children.Add(Ui.Row(T("言語", "Language"), lang, 110));
        panel.Children.Add(Ui.Note(T("閉じると、窓をこの言語で作り直す(直しかけのシナリオはそのまま)。", "The window is rebuilt in this language when you close this (your unsaved edits are kept).")));

        panel.Children.Add(Ui.Heading("AI"));
        var model = new ComboBox { ItemsSource = ClaudeClient.Models, SelectedItem = settings.AiModel, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        model.SelectionChanged += (_, _) =>
        {
            if (model.SelectedItem is not string m) return;
            _settings.AiModel = m;
            _settings.Save();
        };
        panel.Children.Add(Ui.Row(T("モデル", "Model"), model, 110));
        panel.Children.Add(Ui.Note(T("物語のタブの「AI に書かせる」で使う(ゲームの AI 設定とは別)。", "Used by \"Write with AI\" on the Story tab (separate from the game's AI setting).")));
        panel.Children.Add(Ui.Row(T("今のキー", "Current key"), _current, 110));
        panel.Children.Add(Ui.Row(T("新しいキー", "New key"), _key, 110));
        panel.Children.Add(Ui.Note(T("Claude の API キーは、ゲームと共通(Windows の資格情報マネージャーに入る)。", "The Claude API key is shared with the game (stored in Windows Credential Manager).")));
        var keyButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = Ui.Button(T("キーを入れる", "Save key"), SaveKey);
        save.Style = (Style)Application.Current.FindResource("AccentButton");
        keyButtons.Children.Add(save);
        keyButtons.Children.Add(Ui.Button(T("キーを消す", "Delete key"), RemoveKey));
        panel.Children.Add(Ui.Row("", keyButtons, 110));
        panel.Children.Add(_message);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var close = Ui.Button(T("閉じる", "Close"), Close);
        close.IsCancel = true;
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        string? key = ApiKeyStore.Load(ApiKeyStore.Claude);
        _current.Text = key == null ? T("無し", "None") : ApiKeyStore.Mask(key);
        _current.Foreground = key == null ? Ui.Dim : Ui.Good;
    }

    private void SaveKey()
    {
        string key = _key.Password.Trim();
        if (key.Length < 20)
        {
            Say(T("キーが短すぎます(sk-ant- で始まる長い文字列)", "The key is too short (a long string starting with sk-ant-)"), warn: true);
            return;
        }
        if (!ApiKeyStore.Save(ApiKeyStore.Claude, key))
        {
            Say(T("入れられませんでした", "Could not save the key"), warn: true);
            return;
        }
        _key.Clear();
        ShowCurrent();
        Say(T("入れました。ゲームでも同じキーを使います", "Saved. The game uses the same key"));
    }

    private void RemoveKey()
    {
        if (MessageBox.Show(this, T("キーを消しますか？(ゲームでも使えなくなります)", "Delete the key? (The game can no longer use it either)"), T("設定", "Settings"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        ApiKeyStore.Delete(ApiKeyStore.Claude);
        ShowCurrent();
        Say(T("消しました", "Deleted"));
    }

    private void Say(string text, bool warn = false)
    {
        _message.Text = text;
        _message.Foreground = warn ? Ui.Warn : Ui.Good;
    }
}

using System.Text.Json;
using GravewardSpiral.Core.Ai;

namespace GravewardSpiral.Editor;

/// <summary>
/// シナリオエディタの設定(%LOCALAPPDATA%\GravewardSpiral\editor.json)。ゲームの設定とは別に持つ(API キーだけはゲームと共通)。
/// </summary>
public sealed class EditorSettings
{
    /// <summary>画面の言語("ja" か "en")。はじめは Windows の言語から。</summary>
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";

    /// <summary>物語の「AI に書かせる」のモデル。</summary>
    public string AiModel { get; set; } = ClaudeClient.DefaultModel;

    /// <summary>AI に下書きを頼むとき、毎回同じ前置き(指示・スキーマ・id の一覧)をプロンプトキャッシュにする。</summary>
    public bool AiCache { get; set; } = true;

    /// <summary>プロンプトキャッシュの説明(設定の窓と、AI に頼む窓のチェックに出す)。</summary>
    public static string CacheExplanation => Loc.T(
        "AI に頼むたびに送る前置き(指示・項目の説明・id の一覧)は毎回同じなので、Anthropic のサーバーに5分だけ覚えさせる。" +
        "5分以内にまた頼むと、前置きの分が約 1/10 の値段になる(検査で落ちて直させる1回も含む)。最初の1回だけ、前置きの分が 1.25 倍になる。" +
        "手元には何も残らず、送るものも変わらない。続けて2回以上頼むなら得、1回だけ頼んで5分あけるなら少し損(イベント1件で 0.5 円ほど)。" +
        "前置きは入力の 9 割ほどで、試したとき(30 件を続けて)は全体で約 45% 安くなった(返事の長いイベントは約 4 割、短い品やモンスターはもっと)。",
        "The preamble sent with every request (instructions, field descriptions, id lists) is always the same, so Anthropic's server is asked to remember it for 5 minutes. " +
        "If you ask again within 5 minutes, the preamble costs about 1/10 (this includes the one retry after a failed check). The first request pays 1.25x for the preamble. " +
        "Nothing is kept on your PC and what is sent does not change. Two or more requests in a row save money; a single request followed by a 5-minute break costs a little more. " +
        "The preamble is about 90% of the input; in a test of 30 requests in a row it was about 45% cheaper overall (about 40% for events with long replies, more for short items and monsters).");

    /// <summary>AI に頼む窓に出す、キャッシュの短い説明(詳しくはチェックのツールチップと設定の窓)。</summary>
    public static string CacheShort => Loc.T(
        "キャッシュ: 毎回同じ前置き(指示・項目の説明・id の一覧)をサーバーに5分覚えさせる。5分以内に続けて頼むと前置きの分が約 1/10 になり、最初の1回だけ 1.25 倍。続けて頼むなら得(試しでは約 45% 安くなった)。下に出る「キャッシュから読んだ」が、安くなった分。",
        "Cache: the server remembers the same preamble (instructions, field descriptions, id lists) for 5 minutes. Asking again within 5 minutes makes the preamble about 1/10 the price; only the first request pays 1.25x. Cheaper when you ask several times (about 45% in a test). \"Read from cache\" below is the part that got cheaper.");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GravewardSpiral", "editor.json");

    /// <summary>確かめの起動(--screenshot)では読み書きしない。</summary>
    public static bool Persist { get; set; } = true;

    public static EditorSettings Load()
    {
        if (!Persist) return new EditorSettings { Language = "ja" };   // 確かめの起動は日本語(--lang で変える)
        try
        {
            var s = File.Exists(FilePath) ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) : null;
            if (s != null && !ClaudeClient.Models.Contains(s.AiModel)) s.AiModel = ClaudeClient.DefaultModel;
            if (s != null && s.Language is not ("ja" or "en")) s.Language = "ja";
            return s ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new EditorSettings();
        }
    }

    public void Save()
    {
        if (!Persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
    }
}

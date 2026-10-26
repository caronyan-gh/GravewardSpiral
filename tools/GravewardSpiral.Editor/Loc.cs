namespace GravewardSpiral.Editor;

/// <summary>
/// エディタの画面の言語(設定の「言語」)。画面の文字は、その場で T("日本語", "English") と2つ並べて書く。
/// 名前などパックの英日の文は、Lang の言語で見せる(無ければもう一方)。
/// </summary>
public static class Loc
{
    /// <summary>画面の言語("ja" か "en")。</summary>
    public static string Lang { get; set; } = "ja";

    public static bool En => Lang == "en";

    /// <summary>画面の文字(今の言語のほう)。</summary>
    public static string T(string ja, string en) => En ? en : ja;

    /// <summary>パックの英日の文を、今の言語で(無ければもう一方)。</summary>
    public static string? Name(Core.Content.LocalizedText? text) =>
        text == null ? null : text.Texts.GetValueOrDefault(Lang) is { Length: > 0 } s ? s : text.Texts.GetValueOrDefault(En ? "ja" : "en") ?? text.Get(Lang);
}

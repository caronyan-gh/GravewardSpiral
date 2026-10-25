using FontStashSharp;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Rendering;

public enum TextAlign { Left, Center, Right }

/// <summary>
/// 文字の役割。Ui はメニュー・数値・説明などのシステムの文字(BIZ UDゴシック。読みやすさ優先)、
/// Story はタイトル・迷宮のメッセージ・戦闘のログ・イベントや会話などの見せる文字(Hina Mincho を少し太らせて描く)。
/// </summary>
public enum FontStyle { Ui, Story }

/// <summary>
/// 日本語の文字を描く。システムの文字は同梱の BIZ UDゴシック、見せる文字は Hina Mincho(無い字は BIZ UDゴシックで補う)。
/// どちらも無ければ Windows のフォントを使う。
/// 文字は実際の画面の大きさで作るので、拡大してもにじまない。
/// </summary>
public sealed class TextRenderer
{
    /// <summary>同梱のフォントを使う順番(役割ごと)。前のフォントに無い字は、次のフォントで描く。</summary>
    private static readonly string[] StoryOrder = ["HinaMincho-Regular.ttf", "BIZUDGothic-Regular.ttf"];
    private static readonly string[] UiOrder = ["BIZUDGothic-Regular.ttf"];

    private static readonly string[] SystemFallbacks =
    [
        @"C:\Windows\Fonts\BIZ-UDGothicR.ttc",
        @"C:\Windows\Fonts\meiryo.ttc",
        @"C:\Windows\Fonts\YuGothM.ttc",
    ];

    private readonly FontSystem _ui, _story;

    /// <summary>読んだフォント(見せる文字に使う順)。</summary>
    public IReadOnlyList<string> FontPaths { get; }

    public TextRenderer()
    {
        FontPaths = FindFonts(StoryOrder);
        if (FontPaths.Count == 0)
            throw new FileNotFoundException("日本語のフォントが見つかりません。Content/fonts に BIZUDGothic-Regular.ttf を置いてください。");
        _story = Load(FontPaths);
        var ui = FindFonts(UiOrder);
        _ui = Load(ui.Count > 0 ? ui : FontPaths);
    }

    private static FontSystem Load(IEnumerable<string> paths)
    {
        var fonts = new FontSystem(new FontSystemSettings { FontResolutionFactor = 1f, KernelWidth = 0, KernelHeight = 0 });
        foreach (var path in paths) fonts.AddFont(FontCollection.ToSingleFont(File.ReadAllBytes(path)));
        return fonts;
    }

    private static List<string> FindFonts(string[] order)
    {
        string bundled = Path.Combine(AppPaths.Content, "fonts");
        var found = order.Select(name => Path.Combine(bundled, name)).Where(File.Exists).ToList();
        if (found.Count == 0)
        {
            var system = SystemFallbacks.FirstOrDefault(File.Exists);
            if (system != null) found.Add(system);
        }
        return found;
    }

    private FontSystem System(FontStyle style) => style == FontStyle.Story ? _story : _ui;

    /// <summary>size は 1280x720 の座標での文字の高さ。</summary>
    public SpriteFontBase Font(float size, float scale, FontStyle style = FontStyle.Ui) =>
        System(style).GetFont(MathF.Max(1f, MathF.Round(size * scale)));

    /// <summary>1280x720 の座標での文字列の大きさ。</summary>
    public Vector2 Measure(string text, float size, FontStyle style = FontStyle.Ui) => System(style).GetFont(size).MeasureString(text);

    public void Draw(Draw2D d, string text, Vector2 position, float size, Color color, TextAlign align = TextAlign.Left,
        FontStyle style = FontStyle.Ui)
    {
        var font = Font(size, d.Scale, style);
        var measured = font.MeasureString(text);
        var screen = d.ToScreen(position);
        screen.X -= align switch
        {
            TextAlign.Center => measured.X / 2f,
            TextAlign.Right => measured.X,
            _ => 0f,
        };
        var at = new Vector2(MathF.Round(screen.X), MathF.Round(screen.Y));
        d.Batch.DrawString(font, text, at, color);
        if (style != FontStyle.Story) return;
        // Hina Mincho は線が細いので、横に少しずらして重ね、太く見せる(大きい文字ほど厚く)
        int extra = Math.Max(1, (int)MathF.Round(size * d.Scale / 28f));
        for (int i = 1; i <= extra; i++) d.Batch.DrawString(font, text, at + new Vector2(i, 0), color);
    }
}

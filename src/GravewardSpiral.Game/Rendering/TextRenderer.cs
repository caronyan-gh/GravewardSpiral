using FontStashSharp;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Rendering;

public enum TextAlign { Left, Center, Right }

/// <summary>
/// 文字の役割。Ui はメニュー・数値・説明などのシステムの文字(BIZ UDゴシック。読みやすさ優先)、
/// Story はタイトル・迷宮のメッセージ・戦闘のログ・イベントや会話などの見せる文字(設定で Hina Mincho・しっぽり明朝・BIZ UDゴシックから選ぶ。明朝は少し太らせて描く)。
/// </summary>
public enum FontStyle { Ui, Story }

/// <summary>
/// 日本語の文字を描く。システムの文字は同梱の BIZ UDゴシック、見せる文字は Hina Mincho(無い字は BIZ UDゴシックで補う)。
/// どちらも無ければ Windows のフォントを使う。
/// 文字は実際の画面の大きさで作るので、拡大してもにじまない。
/// </summary>
public sealed class TextRenderer
{
    /// <summary>見せる文字の書体(設定の物語の書体)。前のフォントに無い字は、次のフォントで描く。</summary>
    public static readonly string[] StoryFonts = ["hina", "shippori", "gothic"];

    private static string[] StoryOrderOf(string id) => id switch
    {
        "shippori" => ["ShipporiMincho-Regular.ttf", "BIZUDGothic-Regular.ttf"],
        "gothic" => ["BIZUDGothic-Regular.ttf"],
        _ => ["HinaMincho-Regular.ttf", "BIZUDGothic-Regular.ttf"],
    };

    private static readonly string[] StoryOrder = StoryOrderOf("hina");

    /// <summary>いま使う見せる文字の書体(GameOptions.Apply が決める)。変えると、次に描くときから効く。</summary>
    public static string StoryFont { get; set; } = "hina";

    private readonly Dictionary<string, FontSystem> _storyByFont = new(StringComparer.Ordinal);
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
        _storyByFont["hina"] = _story;
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

    private FontSystem System(FontStyle style) => style == FontStyle.Story ? StorySystem() : _ui;

    /// <summary>選んでいる書体の見せる文字(初めて使うときに読む。見つからなければ Hina Mincho の組)。</summary>
    private FontSystem StorySystem()
    {
        if (_storyByFont.TryGetValue(StoryFont, out var fonts)) return fonts;
        var paths = FindFonts(StoryOrderOf(StoryFont));
        return _storyByFont[StoryFont] = paths.Count > 0 ? Load(paths) : _story;
    }

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
        if (style != FontStyle.Story || StoryFont == "gothic") return;
        // 明朝は線が細いので、横に少しずらして重ね、太く見せる(大きい文字ほど厚く)
        int extra = Math.Max(1, (int)MathF.Round(size * d.Scale / 28f));
        for (int i = 1; i <= extra; i++) d.Batch.DrawString(font, text, at + new Vector2(i, 0), color);
    }
}

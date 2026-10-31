using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// Content の下の画像(PNG など)を読んで持っておく。読めないものは null を返す
/// (画像が無くても動くようにする。モンスターの画像も同じ扱い)。
/// </summary>
public sealed class TextureCache : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TextureCache(GraphicsDevice device) => _device = device;

    /// <summary>コードで絵を作るとき(迷宮のマウスの矢印など)に使う。</summary>
    public GraphicsDevice Device => _device;

    /// <summary>path は Content からの相対パス(例: srd/art/title/logo.png)か、絶対パス(シナリオのパックの画像)。</summary>
    public Texture2D? Get(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        Texture2D? texture = null;
        string full = Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.Content, path);
        try
        {
            if (File.Exists(full))
            {
                using var stream = File.OpenRead(full);
                // SpriteBatch の AlphaBlend は、乗算済みのアルファを前提にしている
                texture = Texture2D.FromStream(_device, stream, DefaultColorProcessors.PremultiplyAlpha);
            }
            else
            {
                Console.Error.WriteLine($"画像がありません: {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine($"画像が読めません: {path}: {ex.Message}");
        }
        _cache[path] = texture;
        return texture;
    }

    /// <summary>
    /// 単色の背景(マゼンタ #FF00FF か緑 #00FF00)を透明にして読む(モンスターの画像)。
    /// 左上の色が背景色に近ければ、それを抜く。背景色に近い縁は半透明にし、色のにじみを抑える。透過済みの PNG はそのまま使う。
    /// </summary>
    public Texture2D? GetKeyed(string path)
    {
        string cacheKey = "keyed:" + path;
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        Texture2D? texture = null;
        string full = Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.Content, path);
        try
        {
            if (File.Exists(full))
            {
                using var stream = File.OpenRead(full);
                texture = Texture2D.FromStream(_device, stream);
                var px = new Color[texture.Width * texture.Height];
                texture.GetData(px);
                KeyOut(px);
                texture.SetData(px);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine($"画像が読めません: {path}: {ex.Message}");
        }
        _cache[cacheKey] = texture;
        return texture;
    }

    /// <summary>背景色を抜いて、乗算済みのアルファにする。</summary>
    public static void KeyOut(Color[] px)
    {
        if (px.Length == 0) return;
        var key = px[0];
        bool magenta = key.R > 180 && key.B > 180 && key.G < 90;
        bool green = key.G > 180 && key.R < 90 && key.B < 90;
        bool keyed = key.A == 255 && (magenta || green);
        const float inner = 70f, outer = 140f;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float a = c.A / 255f;
            if (keyed)
            {
                float dr = c.R - key.R, dg = c.G - key.G, db = c.B - key.B;
                float dist = MathF.Sqrt(dr * dr + dg * dg + db * db);
                a *= Math.Clamp((dist - inner) / (outer - inner), 0f, 1f);
                if (a < 1f)
                {
                    // 背景色のにじみを抜く(マゼンタなら赤と青を緑の側へ、緑なら緑を赤と青の側へ寄せる)
                    if (magenta)
                    {
                        byte lim = (byte)Math.Max(c.G, (byte)((c.R + c.B) / 2 * a));
                        c = new Color(Math.Min(c.R, lim), c.G, Math.Min(c.B, lim));
                    }
                    else
                    {
                        byte lim = (byte)Math.Max(Math.Max(c.R, c.B), (byte)(c.G * a));
                        c = new Color(c.R, Math.Min(c.G, lim), c.B);
                    }
                }
            }
            px[i] = new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)(a * 255));
        }
    }

    public void Dispose()
    {
        foreach (var t in _cache.Values) t?.Dispose();
        _cache.Clear();
    }
}

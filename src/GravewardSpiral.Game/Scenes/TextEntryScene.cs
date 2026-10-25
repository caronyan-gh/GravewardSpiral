using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 画面の中のキーボード(名前などの短い文字の入力)。ひらがな・カタカナ・英数を切り替えて、上下左右で選んで決定で打つ。
/// キーボードで英数を直接打つこともでき、Backspace で1文字消す。日本語の変換(IME)は使えないので、かなはこの表から選ぶ。
/// </summary>
public sealed class TextEntryScene : Scene
{
    private const int Cols = 10;
    private const float CellW = 62f, CellH = 44f, GridX = 330f, GridY = 196f;

    private static readonly string[] Hiragana =
    [
        "あいうえおかきくけこ", "さしすせそたちつてと", "なにぬねのはひふへほ", "まみむめもやゆよわを",
        "らりるれろんー・！？", "がぎぐげござじずぜぞ", "だぢづでどばびぶべぼ", "ぱぴぷぺぽぁぃぅぇぉ", "っゃゅょゎ　　　　　",
    ];

    private static readonly string[] Alnum =
    [
        "ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZ-.'&", "abcdefghij", "klmnopqrst", "uvwxyz!?_ ", "0123456789",
    ];

    private enum Page { Hiragana, Katakana, Alnum }

    /// <summary>機能のキー。おまかせ(random)は、名前を選ぶ手立てを渡されたときだけ出す。</summary>
    private readonly string[] _functionKeyIds;

    /// <summary>機能の鍵の名前(text の entry.key.*)。</summary>
    private string[] FunctionKeys => _functionKeyIds.Select(k => Services.Strings["entry.key." + k]).ToArray();

    private readonly string _title;
    private readonly int _maxLength;
    private readonly Action<string> _onDone;
    private readonly Action? _onCancel;
    private readonly Func<string?>? _suggest;
    private string _text;
    private Page _page = GameServices.Language == "ja" ? Page.Katakana : Page.Alnum;
    private int _row, _col;
    private string? _message;

    /// <param name="suggest">おまかせのキーで入れる文を返す(名前の一覧から選ぶなど)。null ならおまかせのキーを出さない。</param>
    public TextEntryScene(string title, string initial, int maxLength, Action<string> onDone, Action? onCancel = null, Func<string?>? suggest = null)
    {
        _suggest = suggest;
        _functionKeyIds = suggest != null
            ? ["hiragana", "katakana", "alnum", "space", "delete", "random", "done"]
            : ["hiragana", "katakana", "alnum", "space", "delete", "done"];
        _title = title;
        _text = initial;
        _maxLength = maxLength;
        _onDone = onDone;
        _onCancel = onCancel;
    }

    private string[] Rows => _page switch
    {
        Page.Hiragana => Hiragana,
        Page.Katakana => Hiragana.Select(ToKatakana).ToArray(),
        _ => Alnum,
    };

    /// <summary>ひらがなをカタカナにする(ー・！？などはそのまま)。</summary>
    private static string ToKatakana(string s) =>
        new(s.Select(c => c is >= 'ぁ' and <= 'ゖ' ? (char)(c + 0x60) : c).ToArray());

    /// <summary>行の数(最後の行は機能のキー)。</summary>
    private int RowCount => Rows.Length + 1;

    private bool OnFunctionRow => _row == Rows.Length;

    private static RectangleF KeyRect(int row, int col) => new(GridX + col * CellW, GridY + row * CellH, CellW - 6, CellH - 6);

    private RectangleF FunctionRect(int row, int i) => new(GridX + i * (CellW * Cols / _functionKeyIds.Length), GridY + row * CellH + 10,
        CellW * Cols / _functionKeyIds.Length - 6, CellH - 6);

    public override void Update(GameTime time, InputState input)
    {
        var audio = Services.Audio;
        int rows = RowCount;
        int cols = OnFunctionRow ? FunctionKeys.Length : Cols;
        int beforeRow = _row, beforeCol = _col;
        if (input.Repeat(GameAction.Up)) _row = (_row + rows - 1) % rows;
        if (input.Repeat(GameAction.Down)) _row = (_row + 1) % rows;
        if (input.Repeat(GameAction.Left)) _col = (_col + cols - 1) % cols;
        if (input.Repeat(GameAction.Right)) _col = (_col + 1) % cols;
        if (input.Pressed(GameAction.PageUp) || input.Pressed(GameAction.PageDown)) _page = (Page)(((int)_page + 1) % 3);
        _col = Math.Min(_col, (OnFunctionRow ? FunctionKeys.Length : Cols) - 1);

        // マウス: 載せたキーを選び、クリックで打つ
        bool clicked = false;
        if (input.Mouse is { } m)
        {
            for (int r = 0; r < rows; r++)
            {
                int n = r == Rows.Length ? FunctionKeys.Length : Cols;
                for (int c = 0; c < n; c++)
                {
                    var rect = r == Rows.Length ? FunctionRect(r, c) : KeyRect(r, c);
                    if (!rect.Contains(m)) continue;
                    if (input.MouseMoved) (_row, _col) = (r, c);
                    if (input.LeftClicked && (_row, _col) == (r, c)) clicked = true;
                }
            }
        }
        if ((_row, _col) != (beforeRow, beforeCol)) audio.PlaySe(Sounds.Cursor);

        // キーボードで直接打った文字。WASD や Z・X は動作にも割り当てられているので、打ったフレームは動作を見ない
        if (input.TypedText.Length > 0)
        {
            foreach (char c in input.TypedText) Type(c.ToString());
            return;
        }
        if (input.BackspacePressed)
        {
            Erase();
            return;   // Backspace は取消にも割り当てられているので、ここでは取消にしない
        }
        if (input.EnterPressed)
        {
            Finish();   // キーボードの Enter は入力の決定(選んだキーを打つのは、ゲームパッドの A やマウス)
            return;
        }

        if (input.Pressed(GameAction.Confirm) || clicked) Press();
        else if (input.Pressed(GameAction.Cancel))
        {
            audio.PlaySe(Sounds.Cancel);
            if (_text.Length > 0) Erase();
            else
            {
                Stack.Pop();
                _onCancel?.Invoke();
            }
        }
        else if (input.Pressed(GameAction.Menu)) Finish();
    }

    private void Press()
    {
        if (OnFunctionRow)
        {
            switch (_functionKeyIds[_col])
            {
                case "hiragana": _page = Page.Hiragana; break;
                case "katakana": _page = Page.Katakana; break;
                case "alnum": _page = Page.Alnum; break;
                case "space": Type(" "); break;
                case "delete": Erase(); break;
                case "random":
                    if (_suggest?.Invoke() is { Length: > 0 } name) _text = name.Length > _maxLength ? name[.._maxLength] : name;
                    _message = null;
                    break;
                default: Finish(); return;
            }
            Services.Audio.PlaySe(Sounds.Confirm);
            return;
        }
        char ch = Rows[_row][_col];
        if (ch == '　') return;   // 空きのキー
        Type(ch.ToString());
    }

    private void Type(string s)
    {
        _message = null;
        if (_text.Length + s.Length > _maxLength)
        {
            _message = Services.Strings.Format("text.too_long", _maxLength);
            Services.Audio.PlaySe(Sounds.Bump);
            return;
        }
        _text += s;
        Services.Audio.PlaySe(Sounds.Cursor);
    }

    private void Erase()
    {
        if (_text.Length > 0) _text = _text[..^1];
        _message = null;
    }

    private void Finish()
    {
        string result = _text.Trim();
        if (result.Length == 0)
        {
            _message = Services.Strings["text.empty"];
            Services.Audio.PlaySe(Sounds.Bump);
            return;
        }
        Services.Audio.PlaySe(Sounds.Confirm);
        Stack.Pop();
        _onDone(result);
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.Text(_title, new Vector2(640, 50), 34, Theme.Title, TextAlign.Center);
        var box = new RectangleF(390, 110, 500, 60);
        Theme.Panel(d, box);
        d.Text(_text + "_", new Vector2(box.X + 20, box.Y + 14), 30, Color.White);
        d.Text($"{_text.Length}/{_maxLength}", new Vector2(box.X + box.Width - 14, box.Y + 22), 16, Theme.Dim, TextAlign.Right);

        var rows = Rows;
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                char ch = rows[r][c];
                if (ch == '　') continue;
                var rect = KeyRect(r, c);
                bool selected = r == _row && c == _col;
                d.FillRect(rect, selected ? Theme.SelectedFill : new Color(16, 22, 30, 220));
                d.Rect(rect, selected ? Theme.SelectedFrame : Theme.Frame, selected ? 1.5f : 1f);
                d.Text(ch == ' ' ? "␣" : ch.ToString(), new Vector2(rect.X + rect.Width / 2, rect.Y + 5), 24, selected ? Color.White : Theme.Text, TextAlign.Center);
            }
        }
        for (int i = 0; i < FunctionKeys.Length; i++)
        {
            var rect = FunctionRect(rows.Length, i);
            bool selected = OnFunctionRow && i == _col;
            bool active = (i == 0 && _page == Page.Hiragana) || (i == 1 && _page == Page.Katakana) || (i == 2 && _page == Page.Alnum);
            d.FillRect(rect, selected ? Theme.SelectedFill : new Color(22, 30, 40, 230));
            d.Rect(rect, selected ? Theme.SelectedFrame : active ? Theme.Gold : Theme.Frame, 1.5f);
            float size = Math.Min(22f, 22f * (rect.Width - 8) / Math.Max(1f, d.Fonts.Measure(FunctionKeys[i], 22).X));   // 長い名前は縮めて収める
            d.Text(FunctionKeys[i], new Vector2(rect.X + rect.Width / 2, rect.Y + 10 + (22 - size) / 2), size, selected ? Color.White : Theme.Text, TextAlign.Center);
        }
        if (_message != null) d.Text(_message, new Vector2(640, 668), 18, Theme.Warn, TextAlign.Center);
        d.Text(s["text.hint"], new Vector2(640, 694), 15, Theme.Dim, TextAlign.Center);
    }
}

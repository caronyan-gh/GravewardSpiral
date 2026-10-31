using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GravewardSpiral.Game.Input;

/// <summary>
/// 1フレームぶんの入力。キーボード・ゲームパッドは動作に置き換え、マウスは 1280x720 の座標で渡す。
/// </summary>
public sealed class InputState
{
    /// <summary>
    /// 押し続けたときの繰り返し(最初の待ちと、そのあとの間隔。メニューのカーソル、迷宮で歩くとき、マウスで押し続けて歩くときも同じ)。
    /// 設定の「長押しの繰り返し」で変わる(GameOptions.Apply)。
    /// </summary>
    public static double RepeatDelay { get; set; } = 0.35;
    public static double RepeatInterval { get; set; } = 0.08;

    /// <summary>窓を選び直したときのクリック(放すまで)。ゲームの中のクリックにしない。</summary>
    private bool _ignoreLeft;
    private bool _wasActive;

    private readonly InputBindings _bindings;
    private readonly HashSet<GameAction> _held = [];
    private readonly HashSet<GameAction> _pressed = [];
    private readonly HashSet<GameAction> _repeated = [];
    private readonly Dictionary<GameAction, double> _heldTime = [];
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;

    public InputState(InputBindings bindings) => _bindings = bindings;

    /// <summary>マウスの位置(画面の帯の上なら null)。</summary>
    public Vector2? Mouse { get; private set; }
    public bool MouseMoved { get; private set; }
    public bool LeftClicked { get; private set; }

    /// <summary>左ボタンを押している(つまみを引きずる操作に使う)。</summary>
    public bool LeftHeld { get; private set; }
    public bool RightClicked { get; private set; }
    public int WheelDelta { get; private set; }

    /// <summary>このフレームで押された数字キー(上の段とテンキー)。数を入力する欄で使う。</summary>
    public IReadOnlyList<char> TypedDigits => _typedDigits;
    private readonly List<char> _typedDigits = [];

    /// <summary>このフレームでキーボードから打たれた文字(英数・記号。日本語の変換は通らない)。</summary>
    public string TypedText { get; private set; } = "";
    private readonly System.Text.StringBuilder _typed = new();

    /// <summary>ウィンドウの文字入力の知らせを受け取る(GravewardGame から呼ぶ)。</summary>
    public void AddTyped(char c)
    {
        if (!char.IsControl(c)) _typed.Append(c);
    }

    /// <summary>このフレームで押された F1〜F6(0〜5)。1人を見る画面で、その番の仲間へ移る。割り当ては変えられない。</summary>
    public int? MemberKey { get; private set; }

    /// <summary>このフレームで Backspace が押された(数を入力する欄で、取消より先に使う)。</summary>
    public bool BackspacePressed { get; private set; }

    /// <summary>このフレームでキーボードの Enter が押された(文字の入力で「決定」に使う。ゲームパッドの A とは分ける)。</summary>
    public bool EnterPressed { get; private set; }

    /// <summary>全画面の切り替え(F11 か Alt+Enter)。割り当ては変えられない。</summary>
    public bool ToggleFullscreen { get; private set; }

    /// <summary>
    /// 次の Update で、この動作が押されたことにする(起動引数の自動操作用)。
    /// Reverse は押しながら使うので、次に入れる動作のあいだ押していたことにする。
    /// </summary>
    public void Inject(GameAction action)
    {
        if (action == GameAction.Reverse) _reverseNext = true;
        else _injected.Add(action);
    }

    private bool _reverseNext;

    private readonly HashSet<GameAction> _injected = [];

    public bool Held(GameAction action) => _held.Contains(action);
    public bool Pressed(GameAction action) => _pressed.Contains(action);

    /// <summary>押した瞬間と、押し続けたときの繰り返し(メニューのカーソル用)。</summary>
    public bool Repeat(GameAction action) => _repeated.Contains(action);

    public void Update(GameTime time, VirtualScreen screen, bool windowActive)
    {
        var keys = Keyboard.GetState();
        var pad = GamePad.GetState(PlayerIndex.One);
        var mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        double dt = time.ElapsedGameTime.TotalSeconds;

        _pressed.Clear();
        _repeated.Clear();
        foreach (var action in Enum.GetValues<GameAction>())
        {
            bool down = windowActive && (_bindings.KeysFor(action).Any(k => KeyDown(keys, k))
                        || (pad.IsConnected && _bindings.ButtonsFor(action).Any(pad.IsButtonDown)));
            bool wasDown = _held.Contains(action);
            if (down && !wasDown)
            {
                _pressed.Add(action);
                _repeated.Add(action);
                _heldTime[action] = 0;
                _held.Add(action);
            }
            else if (down)
            {
                double before = _heldTime[action];
                double after = before + dt;
                _heldTime[action] = after;
                if (after >= RepeatDelay && Math.Floor((after - RepeatDelay) / RepeatInterval) != Math.Floor((before - RepeatDelay) / RepeatInterval))
                    _repeated.Add(action);
            }
            else
            {
                _held.Remove(action);
            }
        }

        _typedDigits.Clear();
        if (windowActive)
        {
            for (int i = 0; i <= 9; i++)
            {
                if (Pressed(keys, Keys.D0 + i) || Pressed(keys, Keys.NumPad0 + i)) _typedDigits.Add((char)('0' + i));
            }
        }
        MemberKey = null;
        for (int i = 0; i < 6 && windowActive; i++)
        {
            if (Pressed(keys, Keys.F1 + i)) MemberKey = i;
        }
        BackspacePressed = windowActive && Pressed(keys, Keys.Back);
        EnterPressed = windowActive && Pressed(keys, Keys.Enter);
        TypedText = windowActive ? _typed.ToString() : "";
        _typed.Clear();

        bool alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        ToggleFullscreen = windowActive && ((keys.IsKeyDown(Keys.F11) && _prevKeys.IsKeyUp(Keys.F11))
                           || (alt && keys.IsKeyDown(Keys.Enter) && _prevKeys.IsKeyUp(Keys.Enter)));
        if (ToggleFullscreen) _pressed.Remove(GameAction.Confirm);

        Mouse = windowActive ? screen.ToVirtual(mouse.Position) : null;
        MouseMoved = Mouse.HasValue && mouse.Position != _prevMouse.Position;
        // 窓を選び直したときに押していたボタンは、放すまで数えない(選び直すクリックで、迷宮を歩いたりしない)
        if (windowActive && !_wasActive && mouse.LeftButton == ButtonState.Pressed) _ignoreLeft = true;
        if (mouse.LeftButton == ButtonState.Released) _ignoreLeft = false;
        _wasActive = windowActive;
        LeftClicked = Mouse.HasValue && !_ignoreLeft && mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        LeftHeld = Mouse.HasValue && !_ignoreLeft && mouse.LeftButton == ButtonState.Pressed;
        RightClicked = Mouse.HasValue && mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
        WheelDelta = windowActive ? (mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120 : 0;
        if (RightClicked) _pressed.Add(GameAction.Cancel);
        // ホイールのクリック(中ボタン)は地図
        if (Mouse.HasValue && mouse.MiddleButton == ButtonState.Pressed && _prevMouse.MiddleButton == ButtonState.Released) _pressed.Add(GameAction.Map);
        foreach (var action in _injected)
        {
            _pressed.Add(action);
            _repeated.Add(action);
        }
        if (_reverseNext && _injected.Count > 0)
        {
            _held.Add(GameAction.Reverse);
            _reverseNext = false;
        }
        _injected.Clear();

        _prevKeys = keys;
        _prevMouse = mouse;
    }

    private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    /// <summary>
    /// キーが押されているか。DesktopGL(SDL)は、放した知らせが届かない(日本語入力や窓の切り替えのとき)と押したままと思い続け、
    /// 前進が入りっぱなしになる。Windows では、OS にも今押されているかを確かめる(Keys の値は Windows の仮想キーの番号と同じ)。
    /// </summary>
    private static bool KeyDown(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && (!OperatingSystem.IsWindows() || (GetAsyncKeyState((int)key) & 0x8000) != 0);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

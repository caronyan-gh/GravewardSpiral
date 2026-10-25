using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GravewardSpiral.Game.Input;

/// <summary>
/// 1フレームぶんの入力。キーボード・ゲームパッドは動作に置き換え、マウスは 1280x720 の座標で渡す。
/// </summary>
public sealed class InputState
{
    private const double RepeatDelay = 0.35;
    private const double RepeatInterval = 0.08;

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

    /// <summary>このフレームで Backspace が押された(数を入力する欄で、取消より先に使う)。</summary>
    public bool BackspacePressed { get; private set; }

    /// <summary>このフレームでキーボードの Enter が押された(文字の入力で「決定」に使う。ゲームパッドの A とは分ける)。</summary>
    public bool EnterPressed { get; private set; }

    /// <summary>全画面の切り替え(F11 か Alt+Enter)。割り当ては変えられない。</summary>
    public bool ToggleFullscreen { get; private set; }

    /// <summary>次の Update で、この動作が押されたことにする(起動引数の自動操作用)。</summary>
    public void Inject(GameAction action) => _injected.Add(action);

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
            bool down = windowActive && (_bindings.KeysFor(action).Any(keys.IsKeyDown)
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
        LeftClicked = Mouse.HasValue && mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        LeftHeld = Mouse.HasValue && mouse.LeftButton == ButtonState.Pressed;
        RightClicked = Mouse.HasValue && mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
        WheelDelta = windowActive ? (mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120 : 0;
        if (RightClicked) _pressed.Add(GameAction.Cancel);
        foreach (var action in _injected)
        {
            _pressed.Add(action);
            _repeated.Add(action);
        }
        _injected.Clear();

        _prevKeys = keys;
        _prevMouse = mouse;
    }

    private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && _prevKeys.IsKeyUp(key);
}

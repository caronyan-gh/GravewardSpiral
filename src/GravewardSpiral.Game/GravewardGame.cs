using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes;
using GravewardSpiral.Game.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game;

public sealed class GravewardGame : Microsoft.Xna.Framework.Game
{
    private static readonly Color Background = new(8, 10, 14);

    private readonly GraphicsDeviceManager _graphics;
    private readonly VirtualScreen _screen = new();
    private readonly DisplaySettings _display;
    private readonly LaunchOptions _options;
    private InputState _input = null!;
    private Draw2D _draw = null!;
    private GameServices _services = null!;
    private int _frame;
    private int _updates;
    private int _nextAction;

    public GravewardGame(LaunchOptions options)
    {
        _options = options;
        _display = DisplaySettings.Load(AppPaths.UserFile(DisplaySettings.FileName));
        if (options.Width is int w && options.Height is int h)
        {
            _display.Fullscreen = false;
            _display.WindowWidth = w;
            _display.WindowHeight = h;
        }
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Math.Max(640, _display.WindowWidth),
            PreferredBackBufferHeight = Math.Max(360, _display.WindowHeight),
            HardwareModeSwitch = false,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = true,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        Window.AllowUserResizing = true;
        Window.Title = "GRAVEWARD SPIRAL";
        Window.ClientSizeChanged += (_, _) => OnResize();
    }

    protected override void Initialize()
    {
        if (_display.Fullscreen) SetFullscreen(true);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        var bindings = InputBindings.LoadOrCreate(AppPaths.UserFile(InputBindings.FileName));
        foreach (var warning in bindings.Warnings) Console.Error.WriteLine(warning);
        _input = new InputState(bindings);
        Window.TextInput += (_, e) => _input.AddTyped(e.Character);

        var text = new TextRenderer();
        Console.WriteLine($"font: {string.Join(" > ", text.FontPaths.Select(Path.GetFileName))}");
        _draw = new Draw2D(GraphicsDevice, text);
        var content = ContentLibrary.Load(AppPaths.Content);
        DungeonLoader.ValidateAll(content);
        var data = GameData.Load(content);
        Console.WriteLine($"packs: {string.Join(", ", content.Packs.Select(p => p.Id))}");
        foreach (var problem in content.Problems) Console.Error.WriteLine(problem);

        // 言語は文言を作る前に決める(設定に無ければ、Windows の言語から)
        var startOptions = _options.ScreenshotPath == null ? GameOptions.Load(AppPaths.UserFile(GameOptions.FileName)) : new GameOptions();
        GameServices.UseLanguage(content, _options.Language ?? startOptions.Language);
        _services = new GameServices
        {
            Content = content,
            Data = data,
            Saves = OpenSaves(_options.SavesDir ?? Path.Combine(AppPaths.UserData, "saves"), content),
            Strings = GameServices.MakeStrings(content),
            Textures = new TextureCache(GraphicsDevice),
            // 確認用の起動(--screenshot)では音を出さない
            Audio = new AudioPlayer(path => ResolveContent(_services.Content, path), enabled: _options.ScreenshotPath == null || _options.Volume != null),
            Exit = Exit,
            Debug = _options.Debug,
            Options = startOptions,
            KeepVolume = _options.Volume != null,
            PersistOptions = _options.ScreenshotPath == null,
            // --saves のとき(確かめの起動)は、自由な迷宮から作るシナリオも、その横の使い捨ての場所に書く
            UserScenarios = _options.SavesDir is { } savesDir ? Path.Combine(savesDir, "scenarios") : AppPaths.Scenarios,
        };
        _services.ApplyOptions();
        _services.Audio.NextBgmStart = _options.BgmStart;
        if (_options.Volume is float volume)
        {
            _services.Audio.BgmVolume = volume;
            _services.Audio.SeVolume = volume;
        }
        if (!content.IsValid) _services.Scenes.Push(new ProblemsScene(content.Problems));
        else if (_options.Start == "maze") _services.Scenes.Push(MazeScene.StartTestDungeon(_services));
        else if (_options.Start == "newgame") _services.Scenes.Push(new NewGameScene());
        else if (_options.Start == "party") DebugStart.Party(_services);
        else if (_options.Start == "events") DebugStart.Events(_services);
        else if (_options.Start == "boss") DebugStart.Boss(_services);
        else if (_options.Start == "town") DebugStart.Town(_services);
        else if (_options.Start == "chest") DebugStart.Chest(_services);
        else if (_options.Start == "ending") DebugStart.Ending(_services);
        else if (_options.Start?.StartsWith("scenario:") == true) DebugStart.Scenario(_services, _options.Start["scenario:".Length..]);
        else if (_options.Start?.StartsWith("genmap") == true) DebugStart.GenMap(_services, _options.Start.Length > 7 ? _options.Start[7..] : null);
        else if (_options.Start?.StartsWith("named") == true) DebugStart.Named(_services, _options.Start.Length > 6 ? _options.Start[6..] : null);
        else if (_options.Start?.StartsWith("combat") == true) DebugStart.Combat(_services, _options.Start.Length > 7 ? _options.Start[7..] : null);
        else _services.Scenes.Push(new TitleScene());
        _services.Scenes.Flush();
        _screen.Update(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
    }

    /// <summary>セーブの置き場所を開く。前の形(枠ごとの save.json)があれば、クランと世界に移す(元は saves/old/ に残す)。</summary>
    private static SaveStore OpenSaves(string root, ContentLibrary content)
    {
        var saves = new SaveStore(root);
        int moved = saves.MigrateOldSlots(content);
        if (moved > 0) Console.WriteLine($"saves: {moved} 個の古いセーブを、クランと世界に移しました(元は {Path.Combine(root, "old")})");
        return saves;
    }

    /// <summary>パックの中のファイルを探す。後から読んだパックのものを優先する。</summary>
    private static string? ResolveContent(ContentLibrary content, string relative)
    {
        for (int i = content.Packs.Count - 1; i >= 0; i--)
        {
            string full = Path.Combine(content.Packs[i].Directory, relative);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private void OnResize()
    {
        if (_graphics.IsFullScreen) return;
        var b = Window.ClientBounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        if (_graphics.PreferredBackBufferWidth != b.Width || _graphics.PreferredBackBufferHeight != b.Height)
        {
            _graphics.PreferredBackBufferWidth = b.Width;
            _graphics.PreferredBackBufferHeight = b.Height;
            _graphics.ApplyChanges();
        }
        _display.WindowWidth = b.Width;
        _display.WindowHeight = b.Height;
    }

    private void SetFullscreen(bool on)
    {
        if (on)
        {
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = Math.Max(640, _display.WindowWidth);
            _graphics.PreferredBackBufferHeight = Math.Max(360, _display.WindowHeight);
        }
        _graphics.IsFullScreen = on;
        _graphics.ApplyChanges();
        _display.Fullscreen = on;
    }

    protected override void Update(GameTime gameTime)
    {
        _screen.Update(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        // 起動引数の自動操作: 決まった Update の回数ごとに、動作を1つずつ押したことにする。
        // 重い処理のあとは、描画を挟まずに Update が続けて呼ばれるので、描画の回数ではなく Update の回数で数える
        _updates++;
        if (_updates % LaunchOptions.FramesPerAction == 0 && _nextAction < _options.Actions.Count)
        {
            if (_options.Actions[_nextAction] is { } action) _input.Inject(action);
            _nextAction++;
        }
        // 確認用の起動では、手元のキーやマウスの入力を受けない(自動操作だけにする)
        _input.Update(gameTime, _screen, IsActive && _options.ScreenshotPath == null);
        if (_input.ToggleFullscreen) SetFullscreen(!_graphics.IsFullScreen);

        _services.Scenes.Update(gameTime, _input);
        _services.Audio.Update(gameTime.ElapsedGameTime.TotalSeconds);
        if (_services.Scenes.Count == 0) Exit();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var full = new Viewport(0, 0, GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        GraphicsDevice.Viewport = full;
        GraphicsDevice.Clear(Color.Black);

        GraphicsDevice.Viewport = _screen.Viewport;
        _draw.Begin(_screen.Scale);
        _draw.FillRect(new RectangleF(0, 0, VirtualScreen.Width, VirtualScreen.Height), Background);
        _services.Scenes.Draw(_draw);
        _draw.End();

        GraphicsDevice.Viewport = full;
        base.Draw(gameTime);

        _frame++;
        if (_options.ScreenshotPath != null && _frame == _options.EffectiveScreenshotFrame)
        {
            SaveScreenshot(_options.ScreenshotPath, full.Width, full.Height);
            Exit();
        }
    }

    private void SaveScreenshot(string path, int width, int height)
    {
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        texture.SaveAsPng(file, width, height);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        // 確認用の起動(--screenshot)では、画面の設定を残さない
        if (_options.ScreenshotPath == null)
            _display.Save(AppPaths.UserFile(DisplaySettings.FileName));
        if (_services.Audio.Enabled) Console.WriteLine($"audio: bgm buffers {_services.Audio.BgmBuffersSubmitted}, loops {_services.Audio.BgmLoops}");
        _services.Audio.Dispose();
        base.OnExiting(sender, args);
    }
}

using GravewardSpiral.Core.Ai;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 新しいゲームを作った直後に、AI にその世界の文を書いてもらう画面。書いているあいだは待ち、取消で止めて決まった文で始める。
/// 書けたら背景の序文を見せ、決定で町へ。失敗したら理由を出して、決まった文で町へ。
/// </summary>
public sealed class WorldWritingScene : Scene
{
    private readonly IAiClient? _client;
    private readonly CancellationTokenSource _cancel = new();
    private Task<(WorldText? World, string? Error)>? _task;
    private double _elapsed;
    private string? _error;
    private string? _prologue;
    private TaleView? _tale;

    /// <param name="client">AI の窓口。null なら AI に頼まず、いまの世界の文(部品で作ったもの)の序文を見せるだけ。</param>
    public WorldWritingScene(IAiClient? client) => _client = client;

    public override void Enter()
    {
        var session = Services.Session!;
        var prompt = WorldWriter.Prompt(Services.Content);
        if (_client == null || prompt == null)
        {
            _prologue = session.World?.Prologue is { } p ? Services.Name(p) : null;
            _tale = _prologue != null ? new TaleView(_prologue) : null;
            if (_prologue == null) Stack.Replace(new Town.TownScene());
            return;
        }
        _task = WorldWriter.WriteAsync(_client, Services.Data, prompt, session.Dungeon.Floors.Count, session.ClanName, _cancel.Token, session.Request.Seed, session);
    }

    public override void Update(GameTime time, InputState input)
    {
        _elapsed += time.ElapsedGameTime.TotalSeconds;
        if (_task is { IsCompleted: true } done)
        {
            _task = null;
            var (world, error) = done.Result;
            if (world != null)
            {
                Services.Session!.World = world;
                _prologue = world.Prologue is { } p ? Services.Name(p) : null;
                _tale = _prologue != null ? new TaleView(_prologue) : null;
                Services.Audio.PlaySe(Sounds.Confirm);
                if (_prologue == null) ToTown();
            }
            else
            {
                _error = Services.Strings.Format("ai.world_failed", error ?? "?");
                Console.WriteLine($"ai: world writing failed: {error}");
                Services.Audio.PlaySe(Sounds.Bump);
            }
            return;
        }
        bool next = input.Pressed(GameAction.Confirm) || input.LeftClicked;
        if (_task != null)
        {
            // 書いているあいだは、取消で止める(決まった文で始める)
            if (input.Pressed(GameAction.Cancel))
            {
                _cancel.Cancel();
                Services.Audio.PlaySe(Sounds.Cancel);
                ToTown();
            }
            return;
        }
        // 序文は、決定でページを送り、最後まで読んだら町へ(取消はすぐ町へ)
        if (input.Pressed(GameAction.Cancel) || (_tale != null ? _tale.Update(input, Services.Audio) : next)) ToTown();
    }

    private void ToTown()
    {
        _task = null;
        Services.FinishNewWorld();   // 自由な迷宮は、文が決まったここでシナリオに書き出す
        Stack.Replace(new Town.TownScene());
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(6, 8, 12));
        if (_task != null)
        {
            string dots = new('.', 1 + (int)(_elapsed * 2) % 3);
            d.Text(s["ai.world_writing"] + dots, new Vector2(640, 300), 30, Theme.Title, TextAlign.Center);
            d.Text(s.Format("ai.world_elapsed", (int)_elapsed, _client?.Model ?? ""), new Vector2(640, 350), 18, Theme.Dim, TextAlign.Center);
            d.Text(s["ai.world_cancel"], new Vector2(640, 660), 16, Theme.Dim, TextAlign.Center);
            return;
        }
        if (_error != null)
        {
            d.TextWrapped(_error, new Vector2(240, 300), 800, 22, Theme.Warn);
        }
        else if (_prologue != null)
        {
            var area = new RectangleF(140, 40, 1000, 610);
            Theme.Panel(d, area);
            _tale?.Draw(d, new Vector2(area.X + 40, area.Y + 36), area.Width - 100, area.Height - 70);   // 英語の行末がはみ出さないよう、少し余らせる
        }
        d.Text(s["ai.world_next"], new Vector2(640, 668), 16, Theme.Dim, TextAlign.Center);
    }
}

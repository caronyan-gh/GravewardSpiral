using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>コンテンツパックに問題があるときに出す一覧。直すまでゲームは始めない。</summary>
public sealed class ProblemsScene : Scene
{
    private readonly IReadOnlyList<string> _problems;
    private int _top;

    public ProblemsScene(IReadOnlyList<string> problems) => _problems = problems;

    private const int Visible = 24;

    public override void Update(GameTime time, InputState input)
    {
        if (input.Repeat(GameAction.Down)) _top = Math.Min(Math.Max(0, _problems.Count - Visible), _top + 1);
        if (input.Repeat(GameAction.Up)) _top = Math.Max(0, _top - 1);
        if (input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Confirm)) Services.Exit();
    }

    public override void Draw(Draw2D d)
    {
        // パックの文言が読めていないこともあるので、ここは文言のファイルを使わない
        d.Text($"コンテンツパックに問題があります({_problems.Count} 件)。直してから起動し直してください。", new Vector2(40, 30), 24, new Color(255, 170, 150));
        for (int i = 0; i < Visible && _top + i < _problems.Count; i++)
            d.Text(_problems[_top + i], new Vector2(40, 80 + i * 24), 16, new Color(220, 225, 230));
        d.Text("↑↓: 送る   決定 / 取消: 終わる", new Vector2(40, 680), 16, new Color(120, 130, 140));
    }
}

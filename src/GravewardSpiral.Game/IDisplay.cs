namespace GravewardSpiral.Game;

/// <summary>画面(全画面と窓の大きさ)。設定の画面から変える。ゲーム本体(GravewardGame)が持つ。</summary>
public interface IDisplay
{
    bool Fullscreen { get; }

    /// <summary>窓の大きさ(全画面のあいだも、戻したときの大きさを持っている)。</summary>
    (int Width, int Height) WindowSize { get; }

    /// <summary>選べる窓の大きさ(16:9 で、画面に収まるもの)。</summary>
    IReadOnlyList<(int Width, int Height)> WindowSizes { get; }

    void SetFullscreen(bool on);

    /// <summary>窓の大きさを変える(全画面のあいだは、戻したときの大きさだけ変える)。</summary>
    void SetWindowSize(int width, int height);
}

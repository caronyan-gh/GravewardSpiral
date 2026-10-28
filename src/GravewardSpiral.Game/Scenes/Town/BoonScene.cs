using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// エピックレベルの恩恵を選ぶ(L21 から1レベルごとに1つ)。選んだ分は仮に置き、使い切るか戻るときに確かめてから決める
/// (押し間違えても、やり直せる)。閉じたら onClose(呼んだ画面の一覧を作り直す)。
/// </summary>
public sealed class BoonScene(Character c, Action? onClose = null) : MenuScene
{
    private readonly List<string> _pending = [];

    public override void Leave() => onClose?.Invoke();

    private int Left => c.EpicPoints - _pending.Count;

    /// <summary>能力値の恩恵で、仮の分を足すと 30 を超えるか。</summary>
    private bool AtCap(string kind) =>
        Abilities.All.Any(a => a.Code() == kind) && c.Scores[Abilities.Parse(kind)] + _pending.Count(x => x == kind) >= Character.MaxEpicScore;

    protected override string Title => Services.Strings.Format("boon.title", c.Name, Left);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var kind in Character.BoonKinds)
        {
            int taken = c.EpicBoons.Count(b => b == kind);
            int added = _pending.Count(b => b == kind);
            string? detail = (taken > 0 ? $"×{taken}" : "") + (added > 0 ? $" (+{added})" : "");
            yield return new MenuItem(s["boon." + kind], detail.Length > 0 ? detail.Trim() : null, Left > 0 && !AtCap(kind), kind);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not string kind || kind == "back")
        {
            Cancel();
            return;
        }
        _pending.Add(kind);
        Services.Audio.PlaySe(Sounds.Confirm);
        if (Left == 0) AskToKeep();
        else Refresh();
    }

    protected override void Cancel()
    {
        if (_pending.Count == 0) Stack.Pop();
        else AskToKeep();
    }

    /// <summary>仮に選んだ恩恵を決めるか。決める / やり直す(仮の分を消して続ける) / 選ばずに戻る。</summary>
    private void AskToKeep()
    {
        var s = Services.Strings;
        var picks = Character.BoonKinds.Where(_pending.Contains)
            .Select(k => _pending.Count(x => x == k) is var n && n > 1 ? $"{s["boon." + k]} ×{n}" : s["boon." + k]);
        Stack.Push(new ConfirmScene(s.Format("asi.confirm", string.Join(s["asi.confirm_sep"], picks)),
            [s["asi.keep"], s["asi.redo"], s["boon.leave"]], i =>
            {
                if (i == 0)
                {
                    foreach (var k in _pending) c.TakeBoon(k);
                    Services.Audio.PlaySe(Sounds.LevelUp);
                }
                if (i is 0 or 2) Stack.Pop();
                _pending.Clear();
                Refresh();
            }, cancelIndex: 1));
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        float y = StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
        Line(d, area, y + 10, Services.Strings["boon.about"], Theme.Dim, 17);
    }
}

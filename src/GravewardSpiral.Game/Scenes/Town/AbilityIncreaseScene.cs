using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// 能力値上昇の点を振る(1点ごとに能力値 +1。20 まで)。選んだ分は仮に置き、点を使い切るか戻るときに確かめてから決める
/// (押し間違えても、やり直せる)。閉じたら onClose(呼んだ画面の一覧を作り直す)。
/// </summary>
public sealed class AbilityIncreaseScene(Character c, Action? onClose = null) : MenuScene
{
    private readonly List<Ability> _pending = [];

    public override void Leave() => onClose?.Invoke();

    private int Left => c.AbilityPoints - _pending.Count;
    private int ScoreOf(Ability a) => c.Scores[a] + _pending.Count(x => x == a);

    protected override string Title => Services.Strings.Format("asi.title", c.Name, Left);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var a in Abilities.All)
        {
            int score = ScoreOf(a);
            int added = score - c.Scores[a];
            string detail = score >= Character.MaxIncreasedScore ? $"{score} (MAX)" : $"{score} → {score + 1}";
            yield return new MenuItem(s["ability." + a.Code()], added > 0 ? $"(+{added}) {detail}" : detail, Left > 0 && score < Character.MaxIncreasedScore, a);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is not Ability a)
        {
            Cancel();
            return;
        }
        _pending.Add(a);
        Services.Audio.PlaySe(Sounds.Confirm);
        if (Left == 0) AskToKeep();
        else Refresh();
    }

    protected override void Cancel()
    {
        if (_pending.Count == 0) Stack.Pop();
        else AskToKeep();
    }

    /// <summary>仮に振った分を決めるか。決める / やり直す(仮の分を消して続ける) / 振らずに戻る。</summary>
    private void AskToKeep()
    {
        var s = Services.Strings;
        var changes = Abilities.All.Where(a => _pending.Contains(a))
            .Select(a => $"{s["ability." + a.Code()]} {c.Scores[a]} → {ScoreOf(a)}");
        Stack.Push(new ConfirmScene(s.Format("asi.confirm", string.Join(s["asi.confirm_sep"], changes)),
            [s["asi.keep"], s["asi.redo"], s["asi.leave"]], i =>
            {
                if (i == 0)
                {
                    foreach (var a in _pending) c.IncreaseAbility(a);
                    Spellcasting.AutoPick(Services.Data, c);   // 準備できる数が増えた分を、おまかせで足す(訓練場で選び直せる)
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
        Line(d, area, y + 10, Services.Strings["asi.about"], Theme.Dim, 17);
    }
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 1人を見る画面(持ち物・状態・呪文の準備)で、仲間を切り替える。前・次は L2/R2 と &lt; &gt; のキー、F1〜F6 はその番の人、
/// 見出しの ◀ ▶ はクリック。回すのは開いた元の並び(パーティか、訓練場の一覧)のうち、filter に合う人だけ。
/// </summary>
public sealed class MemberSwitch(IReadOnlyList<Character> members, Character current, Func<Character, bool>? filter = null)
{
    private RectangleF _prev, _next;

    public Character Current { get; private set; } = current;

    /// <summary>下のパーティの一覧の場所(その行のクリックでも切り替える。members がパーティのときだけ)。</summary>
    public RectangleF? PartyArea { get; init; }

    /// <summary>この画面を使える人か(下の一覧で、使えない人の名前をグレーにする)。</summary>
    public bool Allows(Character c) => filter == null || filter(c);

    /// <summary>切り替える相手がいるか。</summary>
    public bool CanSwitch => Choices.Count > 1;

    private List<Character> Choices => members.Where(c => c == Current || filter == null || filter(c)).ToList();

    /// <summary>切り替えたら true(画面は、その人で作り直す)。</summary>
    public bool Update(InputState input, Audio.AudioPlayer audio)
    {
        var list = Choices;
        if (list.Count < 2) return false;
        int at = list.IndexOf(Current);
        Character? to = null;
        if (input.Repeat(GameAction.PrevMember) || input.LeftClicked && input.Mouse is { } m1 && _prev.Contains(m1)) to = list[(at + list.Count - 1) % list.Count];
        else if (input.Repeat(GameAction.NextMember) || input.LeftClicked && input.Mouse is { } m2 && _next.Contains(m2)) to = list[(at + 1) % list.Count];
        else if (input.MemberKey is int k && k < members.Count && list.Contains(members[k])) to = members[k];
        else if (PartyArea is { } area && input.LeftClicked && input.Mouse is { } m3)
        {
            for (int i = 0; i < members.Count; i++)
                if (PartyPanel.RowRect(area, i).Contains(m3) && list.Contains(members[i])) to = members[i];
        }
        if (to == null || to == Current) return false;
        Current = to;
        audio.PlaySe(Sounds.Cursor);
        return true;
    }

    /// <summary>「◀ 2/6 ▶」を、left から右へ描く(切り替える相手がいなければ描かない)。</summary>
    public void Draw(Draw2D d, Vector2 left, float size = 22f)
    {
        var list = Choices;
        if (list.Count < 2) return;
        string count = $"{list.IndexOf(Current) + 1}/{list.Count}";
        float w = d.Fonts.Measure(count, size).X, arrow = size + 12;
        _prev = new RectangleF(left.X, left.Y - 4, arrow, size + 10);
        _next = new RectangleF(left.X + arrow + w + 8, left.Y - 4, arrow, size + 10);
        d.Text("◀", new Vector2(_prev.X + arrow / 2, left.Y), size, Theme.Dim, TextAlign.Center);
        d.Text(count, new Vector2(_prev.X + arrow + 4, left.Y), size, Theme.Text);
        d.Text("▶", new Vector2(_next.X + arrow / 2, left.Y), size, Theme.Dim, TextAlign.Center);
    }
}

using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// イベントの画面。下の画面(迷宮・町)に重ねて、下の窓に文を1つずつ出し、選択肢があればその下に並べる。
/// 戦闘になったら自分を降ろして戦闘の画面を積み、終わったら同じイベントの続きで積み直す
/// (全滅したときに、この画面が残らないように)。
/// </summary>
public sealed class EventScene : Scene
{
    private static readonly RectangleF Box = new(120, 430, 1040, 260);
    private static readonly RectangleF ArtArea = new(440, 40, 400, 370);

    private readonly EventRunner _runner;
    private readonly Callbacks _on;
    private readonly MenuList _menu = new(new RectangleF(Box.X + 40, Box.Y + 100, Box.Width - 80, Box.Height - 110), 36f, 22f);
    private string _text = "";

    /// <summary>このイベントで出した文(書き留めるときに使う)。</summary>
    private readonly List<LocalizedText> _shown = [];
    private string? _speaker;
    private string? _art;
    private bool _choosing;

    /// <summary>イベントが画面の外に頼むこと(戦闘、町へ戻る、終わったとき)。</summary>
    public sealed record Callbacks(Action<EventCombat, Action<bool>> Combat, Action ToTown, Action? Done = null);

    /// <param name="previousBgm">イベントの前に流れていた曲(戦闘のあとで積み直すときに渡す)。</param>
    public EventScene(EventRunner runner, Callbacks callbacks, string? previousBgm = null, float previousGain = 1f)
    {
        _runner = runner;
        _on = callbacks;
        _previousBgm = previousBgm;
        _previousGain = previousGain;
    }

    private string? _previousBgm;
    private float _previousGain = 1f;

    public override bool IsOverlay => true;

    public override void Enter()
    {
        // イベントの曲(物語の場面など)。前の曲を覚えておき、終わったら戻す
        if (_runner.Def.Bgm is { } bgm)
        {
            if (_previousBgm == null && Services.Audio.CurrentBgm != bgm)
            {
                _previousBgm = Services.Audio.CurrentBgm;
                _previousGain = Services.Audio.CurrentBgmGain;
            }
            Services.Audio.PlayBgm(bgm);
        }
        Advance();
    }

    /// <summary>イベントの曲をやめて、前の曲に戻す(前が無音なら止める)。</summary>
    private void RestoreBgm()
    {
        if (_runner.Def.Bgm == null) return;
        if (_previousBgm != null) Services.Audio.PlayBgm(_previousBgm, gain: _previousGain);
        else Services.Audio.StopBgm();
    }

    /// <summary>次の文・選択・戦闘まで進める。</summary>
    private void Advance()
    {
        while (true)
        {
            var o = _runner.Next();
            switch (o)
            {
                case EventSound snd:
                    Services.Audio.PlaySe(snd.Id);
                    continue;
                case EventMessage m:
                    _text = m.Text != null ? Services.Name(m.Text) : Services.Strings.Format(m.Key!, m.Args.Select(Arg).ToArray());
                    if (m.Text != null) _shown.Add(m.Speaker != null ? Said(m.Speaker, m.Text) : m.Text);
                    _speaker = m.Speaker != null ? Services.Name(m.Speaker) : null;
                    if (m.Art != null) _art = m.Art;
                    _choosing = false;
                    return;
                case EventChoice c:
                    if (c.Prompt != null) _text = Services.Name(c.Prompt);
                    _speaker = null;
                    _menu.SetItems(c.Labels.Select(l => new MenuItem(Services.Name(l))), 0);
                    _choosing = true;
                    return;
                case EventCombat fight:
                {
                    Stack.Pop();
                    var (previous, gain) = (_previousBgm, _previousGain);
                    _on.Combat(fight, won =>
                    {
                        _runner.CombatEnded(won);
                        Stack.Push(new EventScene(_runner, _on, previous, gain));
                    });
                    return;
                }
                case EventToTown:
                    Stack.Pop();
                    _on.ToTown();
                    return;
                default:
                    Stack.Pop();
                    RestoreBgm();
                    OfferMemo();
                    _on.Done?.Invoke();
                    return;
            }
        }
    }

    /// <summary>「話す人: 文」の形にする(言語ごとに)。</summary>
    private static LocalizedText Said(LocalizedText speaker, LocalizedText text) =>
        new(text.Texts.ToDictionary(kv => kv.Key, kv => $"{speaker.Get(kv.Key)}: {kv.Value}"));

    /// <summary>
    /// またその場所で起こるイベントなら、メモに書き留めるかを聞く。書き留めたら、その場所のイベントはもう起こらない
    /// (文はその階のメモで読める。地図のマスに印が付く)。
    /// </summary>
    private void OfferMemo()
    {
        if (!_runner.CanNote || _shown.Count == 0) return;
        if (_runner.At is not { } at || Services.Session?.Exploration is not { } state) return;
        var lines = _shown.Distinct().Take(8).ToList();
        Stack.Push(new ConfirmScene(Services.Strings["memo.ask"], () =>
        {
            state.Notes.Add(new MazeNote { Floor = at.Floor, Cell = at.Cell, EventId = _runner.Def.Id, Lines = lines });
            Services.Audio.PlaySe(Audio.Sounds.Confirm);
        }));
    }

    private object Arg(object a) => a switch
    {
        LocalizedText t => Services.Name(t),
        ItemInstance item => ItemText.Name(Services, item),
        Condition c => Services.Strings["condition." + c.ToString().ToLowerInvariant()],
        Ability ab => Services.Strings["ability." + ab.Code()],
        _ => a,
    };

    public override void Update(GameTime time, InputState input)
    {
        if (_choosing)
        {
            var chosen = _menu.Update(input, Services.Audio);
            if (chosen is int i)
            {
                _runner.Choose(i);
                Advance();
            }
            return;
        }
        if (input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel) || input.LeftClicked)
        {
            Services.Audio.PlaySe(Sounds.Cursor);
            Advance();
        }
    }

    public override void Draw(Draw2D d)
    {
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 110));
        if (_art != null && Art(_art) is { } tex)
        {
            float scale = Math.Min(ArtArea.Width / tex.Width, ArtArea.Height / tex.Height);
            float w = tex.Width * scale, h = tex.Height * scale;
            d.Image(tex, new RectangleF(ArtArea.X + (ArtArea.Width - w) / 2, ArtArea.Y + (ArtArea.Height - h) / 2, w, h), Color.White);
        }
        d.FillRect(Box, new Color(12, 14, 20, 240));
        d.Rect(Box, Theme.Frame, 1.5f);
        float y = Box.Y + 22;
        if (_speaker != null)
        {
            d.Text(_speaker, new Vector2(Box.X + 32, y), 20, Theme.Title, style: FontStyle.Story);
            y += 32;
        }
        d.TextWrapped(_text, new Vector2(Box.X + 32, y), Box.Width - 64, 24, Color.White, 10, FontStyle.Story);
        if (_choosing) _menu.Draw(d);
        else d.Text(Services.Strings["event.more"], new Vector2(Box.X + Box.Width - 24, Box.Y + Box.Height - 34), 15, Theme.Dim, TextAlign.Right);
    }

    /// <summary>絵: モンスターの id か、パックの中の画像のパス(後から読んだパックを優先)。</summary>
    private Microsoft.Xna.Framework.Graphics.Texture2D? Art(string art)
    {
        if (Services.Data.Monsters.TryGetValue(art, out var m)) return Services.MonsterArt(m);
        for (int i = Services.Content.Packs.Count - 1; i >= 0; i--)
        {
            string full = Path.Combine(Services.Content.Packs[i].Directory, art);
            if (File.Exists(full)) return Services.Textures.GetKeyed(full);
        }
        return null;
    }
}

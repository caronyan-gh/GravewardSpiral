using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// キャラ作成(訓練場から)。名前 → 種族 → クラス → 能力値 → (好きな能力値) → 背景 → 技能 → (好きな技能) → 性格 → 確認。
/// 取消で1つ前に戻る。能力値は標準の値(15・14・13・12・10・8)を割り振るか、ポイント購入で決める(ランダムには振らない)。
/// </summary>
public sealed class CharacterCreateScene : Scene
{
    private enum Step { Race, Class, Scores, FreeAbilities, Background, Skills, FreeSkills, Alignment, Confirm }

    private static readonly RectangleF ListArea = new(60, 130, 520, 500);
    private static readonly RectangleF DetailArea = new(620, 110, 600, 540);

    private readonly MenuList _list = new(ListArea, 44f, 24f);
    private const float HeaderHeight = 34f;
    private readonly Action<Character> _onCreated;
    private Step _step;
    private string _name = "";
    private string _race = "", _class = "", _background = "";
    private Alignment _alignment = Alignment.Neutral;

    // 能力値
    private bool _scoresSet;
    private AbilityScores _base = new();
    private bool _pointBuy;
    private Ability? _swapFrom;
    private readonly List<Ability> _freeAbilities = [];
    private readonly List<string> _classSkills = [];
    private readonly List<string> _freeSkills = [];
    private string? _message;

    public CharacterCreateScene(Action<Character> onCreated) => _onCreated = onCreated;

    private GameData Data => Services.Data;

    public override void Enter()
    {
        _race = Data.RaceOrder[0];
        _class = Data.ClassOrder[0];
        _background = Data.BackgroundOrder[0];
        AskName();
    }

    private void AskName() =>
        Stack.Push(new TextEntryScene(Services.Strings["create.name_title"], _name, CharacterFactory.MaxNameLength,
            name =>
            {
                _name = name;
                Go(Step.Race);
            },
            onCancel: () => Stack.Pop(), suggest: NameSuggester(Services, null)));

    private static readonly Pcg32 NameRng = new((ulong)Environment.TickCount64, 77);

    /// <summary>
    /// 名前の入力の「おまかせ」。種族の名前の一覧から、名簿にいない名前を選ぶ(種族が決まっていなければ全部から)。
    /// 見た目だけのことなので、セーブの乱数は使わない。
    /// </summary>
    public static Func<string?> NameSuggester(GameServices services, string? raceId) => () =>
        RandomNames.Pick(services.Data, raceId, GameServices.Language, NameRng, services.Session?.Roster.Characters.Select(c => c.Name));

    private void Go(Step step)
    {
        _step = step;
        _message = null;
        _list.Cursor = 0;   // 新しい段は一番上から(一覧で選ぶ段は、下で今の選択に合わせ直す)
        var s = Services.Strings;
        // クラスの一覧だけ、1行目に見出し(クラス / ヒットダイス)を出すので、そのぶん一覧を下げる
        _list.Area = step == Step.Class ? new RectangleF(ListArea.X, ListArea.Y + HeaderHeight, ListArea.Width, ListArea.Height - HeaderHeight) : ListArea;
        switch (step)
        {
            case Step.Race:
                _list.SetItems(Data.RaceOrder.Select(id => new MenuItem(RaceLabel(id), Tag: id)), Data.RaceOrder.IndexOf(_race));
                break;
            case Step.Class:
                _list.SetItems(Data.ClassOrder.Select(id => new MenuItem(Services.Name(Data.Classes[id].Name), $"d{Data.Classes[id].HitDie}", Tag: id)),
                    Data.ClassOrder.IndexOf(_class));
                break;
            case Step.Scores:
                if (!_scoresSet) UseStandardArray();
                RefreshScores();
                break;
            case Step.FreeAbilities:
                if (Race.FreeAbilityPoints == 0)
                {
                    Go(Step.Background);
                    return;
                }
                RefreshPicks(Abilities.All.Where(a => !Race.AbilityBonus.ContainsKey(a.Code())).Select(a => (a.Code(), AbilityName(a))),
                    _freeAbilities.Select(a => a.Code()).ToList(), Race.FreeAbilityPoints);
                break;
            case Step.Background:
                _list.SetItems(Data.BackgroundOrder.Select(id => new MenuItem(Services.Name(Data.Backgrounds[id].Name),
                    Services.Strings.List(Data.Backgrounds[id].Skills.Select(SkillName)), Tag: id)), Data.BackgroundOrder.IndexOf(_background));
                break;
            case Step.Skills:
                _classSkills.RemoveAll(FixedSkills().Contains);
                RefreshPicks(Data.SkillChoicesFor(Class).Where(id => !FixedSkills().Contains(id)).Select(id => (id, SkillName(id))), _classSkills, Class.SkillChoices);
                break;
            case Step.FreeSkills:
                if (Race.FreeSkills == 0)
                {
                    Go(Step.Alignment);
                    return;
                }
                _freeSkills.RemoveAll(id => FixedSkills().Contains(id) || _classSkills.Contains(id));
                RefreshPicks(Data.Skills.Keys.Order().Where(id => !FixedSkills().Contains(id) && !_classSkills.Contains(id)).Select(id => (id, SkillName(id))),
                    _freeSkills, Race.FreeSkills);
                break;
            case Step.Alignment:
                _list.SetItems(Enum.GetValues<Alignment>().Select(a => new MenuItem(s[$"alignment.{a.ToString().ToLowerInvariant()}"], Tag: a)), (int)_alignment);
                break;
            case Step.Confirm:
                _list.SetItems([new MenuItem(s["create.make"]), new MenuItem(s["create.restart"])], 0);
                break;
        }
    }

    private RaceDef Race => Data.Races[_race];
    private ClassDef Class => Data.Classes[_class];

    private HashSet<string> FixedSkills() => Race.Skills.Concat(Data.Backgrounds[_background].Skills).ToHashSet();

    private string RaceLabel(string id)
    {
        var r = Data.Races[id];
        return r.Subrace != null ? $"{Services.Name(r.Name)}({Services.Name(r.Subrace)})" : Services.Name(r.Name);
    }

    private string SkillName(string id) => Data.Skills.TryGetValue(id, out var sk) ? Services.Name(sk.Name) : id;
    private string AbilityName(Ability a) => Services.Strings[$"ability.{a.Code()}"];

    // ---- 能力値 ----------------------------------------------------------------

    /// <summary>標準の値を、クラスの大事な能力値の順に並べる(あとで入れ替えられる)。</summary>
    private void UseStandardArray()
    {
        _base = CharacterFactory.AutoAssign(CharacterFactory.StandardArray, Class);
        _swapFrom = null;
        _scoresSet = true;
    }

    private int PointsUsed => Abilities.All.Sum(a => CharacterFactory.PointCost(Math.Clamp(_base[a], 8, 15)));

    private void RefreshScores()
    {
        var s = Services.Strings;
        var items = Abilities.All.Select(a =>
        {
            int bonus = Race.AbilityBonus.TryGetValue(a.Code(), out var b) ? b : 0;
            bonus += _freeAbilities.Contains(a) ? 1 : 0;
            int total = _base[a] + bonus;
            string mark = _swapFrom == a ? " ◆" : "";
            string detail = bonus > 0 ? $"{_base[a]} +{bonus} = {total}({Signed(Abilities.Modifier(total))})" : $"{total}({Signed(Abilities.Modifier(total))})";
            return new MenuItem(AbilityName(a) + mark, detail, Tag: a);
        }).ToList();
        if (_pointBuy)
        {
            items.Add(new MenuItem(s.Format("create.points_left", CharacterFactory.PointBuyBudget - PointsUsed), Enabled: false));
            items.Add(new MenuItem(s["create.to_roll"], Tag: "mode"));
        }
        else
        {
            items.Add(new MenuItem(s["create.auto"], Tag: "auto"));
            items.Add(new MenuItem(s["create.to_point_buy"], Tag: "mode"));
        }
        items.Add(new MenuItem(s["create.next"], Enabled: !_pointBuy || PointsUsed <= CharacterFactory.PointBuyBudget, Tag: "next"));
        _list.SetItems(items);
    }

    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();

    // ---- 複数を選ぶ一覧(技能・好きな能力値) -------------------------------------------

    private List<(string Id, string Label)> _pickChoices = [];
    private List<string> _picked = [];
    private int _pickNeed;

    private void RefreshPicks(IEnumerable<(string Id, string Label)> choices, List<string> picked, int need)
    {
        _pickChoices = choices.ToList();
        _picked = picked;
        _pickNeed = need;
        RefreshPickList();
    }

    private void RefreshPickList()
    {
        var s = Services.Strings;
        var items = _pickChoices.Select(c => new MenuItem((_picked.Contains(c.Id) ? "■ " : "□ ") + c.Label, Tag: c.Id)).ToList();
        items.Add(new MenuItem(s.Format("create.pick_done", _picked.Count, _pickNeed), Enabled: _picked.Count == _pickNeed, Tag: "next"));
        _list.SetItems(items);
    }

    private void TogglePick(string id)
    {
        if (_picked.Contains(id)) _picked.Remove(id);
        else if (_picked.Count < _pickNeed) _picked.Add(id);
        else
        {
            _message = Services.Strings.Format("create.pick_full", _pickNeed);
            return;
        }
        _message = null;
        RefreshPickList();
    }

    // ---- 入力 --------------------------------------------------------------------

    /// <summary>ポイント購入で、その能力値を delta だけ上げ下げする(8〜15、27 点の中で)。</summary>
    private void AddPoint(Ability a, int delta)
    {
        int old = _base[a];
        _base[a] = Math.Clamp(old + delta, 8, 15);
        if (PointsUsed > CharacterFactory.PointBuyBudget) _base[a] = old;
        else if (_base[a] != old) Services.Audio.PlaySe(Sounds.Cursor);
        RefreshScores();
    }

    public override void Update(GameTime time, InputState input)
    {
        if (_step == Step.Scores && _pointBuy)
        {
            // マウス: 左の欄の中にマウスがあり、選択中の行が能力値なら、左クリックで上げ、右クリックで下げる(右クリックを取消にしない)
            if ((input.LeftClicked || input.RightClicked) && input.Mouse is { } m && ListArea.Contains(m) && _list.Current?.Tag is Ability ma)
            {
                AddPoint(ma, input.LeftClicked ? 1 : -1);
                return;
            }
            if (_list.Current?.Tag is Ability pa)
            {
                int delta = (input.Repeat(GameAction.Right) ? 1 : 0) - (input.Repeat(GameAction.Left) ? 1 : 0);
                if (delta != 0) AddPoint(pa, delta);
            }
        }

        var chosen = _list.Update(input, Services.Audio);
        if (chosen is int i) Choose(_list.Items[i]);
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Back();
        }
    }

    private void Back()
    {
        switch (_step)
        {
            case Step.Race: AskName(); break;
            case Step.Class: Go(Step.Race); break;
            case Step.Scores: Go(Step.Class); break;
            case Step.FreeAbilities: Go(Step.Scores); break;
            case Step.Background: Go(Race.FreeAbilityPoints > 0 ? Step.FreeAbilities : Step.Scores); break;
            case Step.Skills: Go(Step.Background); break;
            case Step.FreeSkills: Go(Step.Skills); break;
            case Step.Alignment: Go(Race.FreeSkills > 0 ? Step.FreeSkills : Step.Skills); break;
            case Step.Confirm: Go(Step.Alignment); break;
        }
    }

    private void Choose(MenuItem item)
    {
        switch (_step)
        {
            case Step.Race:
                if ((string)item.Tag! != _race) _freeAbilities.Clear();
                _race = (string)item.Tag!;
                Go(Step.Class);
                break;
            case Step.Class:
                if ((string)item.Tag! != _class) _classSkills.Clear();
                _class = (string)item.Tag!;
                if (_scoresSet && !_pointBuy) UseStandardArray();
                Go(Step.Scores);
                break;
            case Step.Scores:
                ChooseScoreRow(item);
                break;
            case Step.FreeAbilities:
                if (item.Tag is "next")
                {
                    _freeAbilities.Clear();
                    _freeAbilities.AddRange(_picked.Select(Abilities.Parse));
                    Go(Step.Background);
                }
                else TogglePick((string)item.Tag!);
                break;
            case Step.Background:
                _background = (string)item.Tag!;
                Go(Step.Skills);
                break;
            case Step.Skills:
            case Step.FreeSkills:
                if (item.Tag is "next") Go(_step == Step.Skills ? Step.FreeSkills : Step.Alignment);
                else TogglePick((string)item.Tag!);
                break;
            case Step.Alignment:
                _alignment = (Alignment)item.Tag!;
                Go(Step.Confirm);
                break;
            case Step.Confirm:
                if (_list.Cursor == 0) Make();
                else
                {
                    _scoresSet = false;
                    _classSkills.Clear();
                    _freeSkills.Clear();
                    _freeAbilities.Clear();
                    AskName();
                }
                break;
        }
    }

    private void ChooseScoreRow(MenuItem item)
    {
        switch (item.Tag)
        {
            case Ability a when !_pointBuy:
                // 2 つ選んで値を入れ替える
                if (_swapFrom is { } from && from != a)
                {
                    (_base[from], _base[a]) = (_base[a], _base[from]);
                    _swapFrom = null;
                }
                else _swapFrom = _swapFrom == a ? null : a;
                break;
            case "auto": UseStandardArray(); break;
            case "mode":
                _pointBuy = !_pointBuy;
                if (_pointBuy) foreach (var x in Abilities.All) _base[x] = 8;
                else UseStandardArray();
                break;
            case "next":
                Go(Step.FreeAbilities);
                return;
        }
        int cursor = _list.Cursor;
        RefreshScores();
        _list.Cursor = cursor;
    }

    private CharacterChoices Choices() => new(_name, _race, _class, _background, _alignment, _base.Clone(), _freeAbilities.ToList(), _classSkills.ToList(), _freeSkills.ToList());

    private void Make()
    {
        var problems = CharacterFactory.Validate(Data, Choices());
        if (problems.Count > 0)
        {
            _message = problems[0];
            return;
        }
        var c = CharacterFactory.Create(Data, Choices());
        Stack.Pop();
        _onCreated(c);
    }

    // ---- 描画 --------------------------------------------------------------------

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.Text(s[$"create.step.{_step.ToString().ToLowerInvariant()}"], new Vector2(60, 50), 32, Theme.Title);
        d.Text(_name, new Vector2(60, 92), 22, Theme.Gold);
        Theme.Panel(d, new RectangleF(ListArea.X - 10, ListArea.Y - 10, ListArea.Width + 20, ListArea.Height + 20));
        if (_step == Step.Class)
        {
            float hy = ListArea.Y + 4;
            d.Text(s["create.class_header"], new Vector2(ListArea.X + 14, hy), 18, Theme.Dim);
            d.Text(s["create.hit_die_header"], new Vector2(ListArea.X + ListArea.Width - 14, hy), 18, Theme.Dim, TextAlign.Right);
            d.FillRect(new RectangleF(ListArea.X, ListArea.Y + HeaderHeight - 8, ListArea.Width, 1), Theme.Dim * 0.5f);
        }
        _list.Draw(d);
        Theme.Panel(d, DetailArea);
        DrawDetail(d, new Vector2(DetailArea.X + 24, DetailArea.Y + 20));
        if (_message != null) d.Text(_message, new Vector2(640, 664), 18, Theme.Warn, TextAlign.Center);
        d.Text(s["create.hint"], new Vector2(640, 694), 15, Theme.Dim, TextAlign.Center);
    }

    private void DrawDetail(Draw2D d, Vector2 at)
    {
        var s = Services.Strings;
        float y = at.Y;
        void Line(string text, Color? color = null, float size = 20f) =>
            y = d.TextWrapped(text, new Vector2(at.X, y), DetailArea.Width - 48, size, color ?? Theme.Text) + 2;
        switch (_step)
        {
            case Step.Race when _list.Current?.Tag is string rid:
            {
                var r = Data.Races[rid];
                Line(RaceLabel(rid), Theme.Title, 26);
                Line(string.Join("  ", r.AbilityBonus.Select(kv => $"{AbilityName(Abilities.Parse(kv.Key))} +{kv.Value}")) +
                     (r.FreeAbilityPoints > 0 ? s.Format("create.free_abilities", r.FreeAbilityPoints) : ""));
                if (r.Skills.Count > 0) Line(s.Format("create.gain_skills", Services.Strings.List(r.Skills.Select(SkillName))), Theme.Text, 18);   // 種族の技能
                Wrap(d, Services.Name(r.Description), at.X, ref y);
                break;
            }
            case Step.Class when _list.Current?.Tag is string cid:
            {
                var c = Data.Classes[cid];
                Line(Services.Name(c.Name), Theme.Title, 26);
                Line(s.Format("create.hit_die", c.HitDie));
                Line(s.Format("create.saves", Services.Strings.List(c.Saves.Select(x => AbilityName(Abilities.Parse(x))))));
                Line(s.Format("create.primary", Services.Strings.List(c.PrimaryAbilities.Take(2).Select(AbilityName))));
                // 最初の習熟(データから。マルチクラスで別のクラスを取れば増える)
                var (armor, weapons) = Town.StatusScene.Proficiencies(Services, [c]);
                Line(s.Format("create.armor_prof", armor), Theme.Text, 18);
                Line(s.Format("create.weapon_prof", weapons), Theme.Text, 18);
                Wrap(d, Services.Name(c.Description), at.X, ref y);
                break;
            }
            case Step.Scores:
                Line(s[_pointBuy ? "create.about_point_buy" : "create.about_roll"], Theme.Dim, 18);
                if (!_pointBuy) Line(s.Format("create.rolled", string.Join(" ", CharacterFactory.StandardArray)), Theme.Text, 20);
                Line(s.Format("create.hp_preview", Math.Max(1, Class.HitDie + Abilities.Modifier(_base.Con + (Race.AbilityBonus.TryGetValue("con", out var cb) ? cb : 0)) + Race.HpPerLevel)), Theme.Good);
                break;
            case Step.Skills:
            case Step.FreeSkills:
            case Step.FreeAbilities:
                Line(s[$"create.about_{_step.ToString().ToLowerInvariant()}"], Theme.Dim, 18);
                if (_step != Step.FreeAbilities) Line(s.Format("create.fixed_skills", Services.Strings.List(FixedSkills().Select(SkillName))), Theme.Text, 18);
                // カーソルの技能の説明(元の能力値と、何に使うか)
                if (_step != Step.FreeAbilities && _list.Current?.Tag is string sid && Data.Skills.TryGetValue(sid, out var skill))
                {
                    y += 14;
                    Line($"{Services.Name(skill.Name)}({AbilityName(skill.AbilityValue)})", Theme.Title, 24);
                    if (skill.Description != null) Wrap(d, Services.Name(skill.Description), at.X, ref y);
                }
                break;
            case Step.Background when _list.Current?.Tag is string bid:
                Line(Services.Name(Data.Backgrounds[bid].Name), Theme.Title, 26);
                Line(s.Format("create.gain_skills", Services.Strings.List(Data.Backgrounds[bid].Skills.Select(SkillName))), Theme.Text, 18);   // 経歴で得る技能
                Wrap(d, Services.Name(Data.Backgrounds[bid].Description), at.X, ref y);
                break;
            case Step.Alignment:
                Wrap(d, s["create.about_alignment"], at.X, ref y);
                break;
            case Step.Confirm:
            {
                var problems = CharacterFactory.Validate(Data, Choices());
                if (problems.Count > 0)
                {
                    foreach (var p in problems) Line(p, Theme.Warn, 18);
                    break;
                }
                var c = CharacterFactory.Create(Data, Choices());
                Line($"{c.Name}  {RaceLabel(_race)}  {Services.Name(Class.Name)}", Theme.Title, 24);
                Line(string.Join("  ", Abilities.All.Select(a => $"{AbilityName(a)} {c.Scores[a]}")));
                Line(s.Format("create.summary_hp_ac", c.MaxHp, c.ArmorClass(Data)));
                Line(s.Format("create.summary_skills", Services.Strings.List(c.Skills.Select(SkillName))), Theme.Text, 18);
                Line(s.Format("create.summary_bg", Services.Name(Data.Backgrounds[_background].Name), s[$"alignment.{_alignment.ToString().ToLowerInvariant()}"]), Theme.Text, 18);
                break;
            }
        }
    }

    /// <summary>長い文を詳しい欄の幅で折り返して描く。</summary>
    private static void Wrap(Draw2D d, string text, float x, ref float y, float size = 19f) =>
        y = d.TextWrapped(text, new Vector2(x, y), DetailArea.Width - 48, size, Theme.Text);
}

using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.Text;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 戦闘の画面(初代風)。左上に先頭のグループの絵、右上に敵のグループ、その下にログ、右下にコマンド、左下にパーティ。
/// 全員の行動を順に選んでからラウンドを進め、ログを1行ずつ見せる(決定で次へ、押し続けると早送り)。
/// 終わったら、勝ち・逃げは迷宮に戻り、全滅はタイトルへ戻る。
/// </summary>
public sealed class CombatScene : Scene
{
    private static readonly RectangleF ArtArea = new(40, 30, 760, 370);
    private static readonly RectangleF GroupArea = new(830, 30, 410, 370);
    private static readonly RectangleF LogArea = new(40, 418, 760, 112);
    private static readonly RectangleF CommandArea = new(830, 418, 410, 272);
    private static readonly RectangleF PartyArea = new(40, 572, 760, 118);
    private const int LogLines = 3;

    /// <summary>決定を押し続けたときの、ログ1行の時間(秒)。ふつうの時間は設定のログの速さ。</summary>
    private const double FastSeconds = 0.07;

    private double LineSeconds => Services.Options.LogSeconds;

    private enum Phase { Input, Spell, Slot, ItemList, Target, Member, Playing, Result }

    private readonly IReadOnlyList<(MonsterDef Def, int Count)> _groups;
    private readonly Action<CombatOutcome>? _onEnd;
    private CombatEngine _engine = null!;
    private CombatState _state = null!;
    private readonly MenuList _menu = new(new RectangleF(CommandArea.X + 16, CommandArea.Y + 50, CommandArea.Width - 32, CommandArea.Height - 64), 34f, 21f);
    private readonly Dictionary<string, PlayerCommand> _commands = [];
    private Phase _phase;
    private int _member;          // 行動を選んでいる人(_state.Party の添え字)
    private CommandKind _pending; // 援護・並び替え・呪文で、相手を選んでいるとき
    private SpellDef? _spell;      // 選んでいる呪文と、スロットのレベル
    private int _slot;
    private int _itemIndex = -1;   // 使うアイテム(袋の中の何番目か)
    private int _shown;           // 見せ終わったログの行数
    private double _wait;
    private double _shake;         // 絵の揺れの残り時間(秒)と強さ。絵の敵にダメージが入ると揺れる
    private float _shakePower;
    private const double ShakeSeconds = 0.3;
    private readonly List<string> _lines = [];
    private readonly List<Color?> _lineColors = [];   // 呪文の属性の色(無ければ白)
    private readonly List<(string Text, Color? Color)> _backlog = [];   // この戦闘のログの全部(メニューのキーで読み返す)

    /// <param name="onEnd">終わったときに呼ぶ(迷宮が、次の遭遇までの歩数を戻すなど)。</param>
    /// <param name="noMagic">魔法禁止区域での戦闘(呪文と、呪文のアイテムが使えない)。</param>
    /// <param name="scale">深い階の敵の強化。</param>
    /// <param name="bgm">戦闘の曲(省くと、ふつうの戦闘の曲)。ボス戦は BossBgm。</param>
    public CombatScene(IReadOnlyList<(MonsterDef Def, int Count)> groups, Action<CombatOutcome>? onEnd = null, bool noMagic = false,
        MonsterScale? scale = null, string? bgm = null)
    {
        _bgm = bgm ?? Bgm;
        _scale = scale;
        _groups = groups;
        _onEnd = onEnd;
        _noMagic = noMagic;
    }

    private readonly bool _noMagic;
    private readonly MonsterScale? _scale;

    private Strings S => Services.Strings;

    /// <summary>ふつうの戦闘の BGM(「The Sunless Gauntlet」)。</summary>
    public const string Bgm = "audio/bgm/battle.mp3";

    /// <summary>ボス戦の BGM(「Vigil of the Fallen King」)。</summary>
    public const string BossBgm = "audio/bgm/boss.mp3";

    private readonly string _bgm;
    private string? _previousBgm;   // 戦闘の前に流れていた曲(終わったら戻す)
    private float _previousGain = 1f;

    public override void Enter()
    {
        _previousBgm ??= Services.Audio.CurrentBgm;
        _previousGain = Services.Audio.CurrentBgmGain;
        Services.Audio.PlayBgm(_bgm, fadeInSeconds: 0.3f);
        var session = Services.Session;
        IRandom rng = session?.Rng[RngStreams.Names.Combat] ?? new Pcg32((ulong)Environment.TickCount64, 11);
        _engine = new CombatEngine(Services.Data, rng);
        var party = session?.Roster.PartyMembers.ToList() ?? [];
        _state = _engine.Start(party, _groups, _scale);
        _state.NoMagic = _noMagic;
        if (_noMagic) _state.Add("combat.antimagic_zone");
        Services.Audio.PlaySe(Sounds.Encounter);
        _phase = Phase.Playing;   // あらわれた、を見せてから入力へ
    }

    // ---------------- 入力 ----------------

    private void BeginInput()
    {
        if (_state.Round == 0 && _state.Surprised == Side.Party)
        {
            RunRound();   // 不意を打たれたら、最初のラウンドは何もできない
            return;
        }
        _commands.Clear();
        _member = -1;
        NextMember();
    }

    /// <summary>次に行動を選ぶ人へ。全員選んだらラウンドを進める。</summary>
    private void NextMember()
    {
        do _member++;
        while (_member < _state.Party.Count && !_state.Party[_member].CanAct);
        if (_member >= _state.Party.Count)
        {
            RunRound();
            return;
        }
        ShowCommands();
    }

    private void PreviousMember()
    {
        int i = _member;
        do i--;
        while (i >= 0 && !_state.Party[i].CanAct);
        if (i < 0) return;
        _member = i;
        _commands.Remove(_state.Party[i].Id);
        ShowCommands();
    }

    private PartyMember Current => _state.Party[_member];

    private void ShowCommands()
    {
        _phase = Phase.Input;
        bool reach = _state.AttackableGroups(Current).Any();
        var items = new List<MenuItem>
        {
            new(S["combat.cmd.attack"], reach ? null : S["combat.cannot_reach"], reach, CommandKind.Attack),
            // 習熟の無い鎧を着ていると唱えられない(5版)
            Current.Character.ArmorProficient(Services.Data)
                ? new(S["combat.cmd.cast"], Enabled: CombatSpells().Any(x => x.Slot != null), Tag: CommandKind.Cast)
                : new(S["combat.cmd.cast"], S["inv.no_proficiency"], false, CommandKind.Cast),
            new(S["combat.cmd.item"], Enabled: UsableItems().Any(), Tag: CommandKind.Item),
            new(S["combat.cmd.dodge"], Tag: CommandKind.Dodge),
            new(S["combat.cmd.help"], Enabled: _state.Party.Count(p => p.Standing) > 1, Tag: CommandKind.Help),
            new(S["combat.cmd.hide"], Tag: CommandKind.Hide),
            new(S["combat.cmd.swap"], Enabled: _state.Party.Count > 1, Tag: CommandKind.Swap),
            new(S["combat.cmd.flee"], Tag: CommandKind.Flee),
            new(S["combat.cmd.auto"], Tag: "auto"),
            new(S["combat.cmd.auto_nospell"], Tag: "auto_nospell"),
        };
        if (_state.Party.Take(_member).Any(p => p.CanAct)) items.Add(new MenuItem(S["combat.cmd.back"], Tag: "back"));
        SetMenu(items);
    }

    private ClassDef? CurrentClass => Spellcasting.CasterClass(Services.Data, Current.Character);

    /// <summary>戦闘で唱えられる呪文と、唱えられる一番低いスロット(無ければ null)。</summary>
    private IEnumerable<(SpellDef Spell, int? Slot)> CombatSpells()
    {
        if (CurrentClass is not { } cls) return [];
        return Spellcasting.Castable(Services.Data, Current.Character).Where(x => x.InCombat)
            .Select(x => (x, Spellcasting.LowestSlot(cls, Current.Character, x)));
    }

    /// <summary>戦闘で使える袋の中のアイテム(袋の何番目か)。</summary>
    private IEnumerable<(int Index, Core.Items.ItemInstance Item, Core.Items.ItemDef Def)> UsableItems()
    {
        var items = Current.Character.Inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (!Services.Data.Items.TryGetValue(items[i].ItemId, out var def)) continue;
            if (def.Kind == Core.Items.ItemKind.Ammo)
            {
                yield return (i, items[i], def);   // 弾は付け替える
                continue;
            }
            if (!def.IsUsable) continue;
            if (def.UseSpell != null && !(Services.Data.Spells.GetValueOrDefault(def.UseSpell)?.InCombat ?? false)) continue;
            yield return (i, items[i], def);
        }
    }

    private void ShowItems()
    {
        _phase = Phase.ItemList;
        SetMenu(UsableItems().Select(x => new MenuItem(ItemText.Name(Services, x.Item), Tag: x.Index)));
    }

    /// <summary>アイテムの対象を決める(呪文のアイテムは呪文の対象、それ以外は use_target)。</summary>
    private void ChooseItemTarget()
    {
        var def = Services.Data.Items[Current.Character.Inventory.Items[_itemIndex].ItemId];
        _pending = CommandKind.Item;
        if (def.Kind == Core.Items.ItemKind.Ammo)
        {
            Set(new PlayerCommand(CommandKind.Item, -1, -1, ItemIndex: _itemIndex));
            return;
        }
        var spell = def.UseSpell != null ? Services.Data.Spells.GetValueOrDefault(def.UseSpell) : null;
        bool enemies = spell?.TargetsEnemies ?? def.UseTarget is "enemy" or "group";
        bool ally = spell != null ? spell.Target == SpellTarget.Ally : def.UseTarget is null or "ally";
        if (enemies)
        {
            var reach = _state.LiveGroups.Select(g => _state.Groups.IndexOf(g)).ToList();
            if (reach.Count == 1) Set(new PlayerCommand(CommandKind.Item, reach[0], ItemIndex: _itemIndex));
            else ShowGroups(reach.ToHashSet());
        }
        else if (ally) ShowMembers();
        else Set(new PlayerCommand(CommandKind.Item, ItemIndex: _itemIndex));
    }

    private void ShowSpells()
    {
        _phase = Phase.Spell;
        var cls = CurrentClass!;
        var items = CombatSpells().Select(x => SpellText.CastItem(Services, Current.Character, x.Spell,
            x.Spell.IsCantrip ? S["spell.cantrip"] : $"Lv{x.Spell.Level} ({Spellcasting.SlotsLeft(cls, Current.Character, x.Spell.Level)})",
            x.Slot != null));
        SetMenu(items);
    }

    /// <summary>上のスロットで唱えるか(選べるのが1つなら飛ばす)。</summary>
    private void ShowSlots()
    {
        var cls = CurrentClass!;
        var levels = _spell!.IsCantrip ? [] : Enumerable.Range(_spell.Level, 10 - _spell.Level)
            .Where(l => Spellcasting.SlotsLeft(cls, Current.Character, l) > 0).ToList();
        if (levels.Count <= 1)
        {
            _slot = levels.Count == 1 ? levels[0] : 0;
            ChooseSpellTarget();
            return;
        }
        _phase = Phase.Slot;
        SetMenu(levels.Select(l => new MenuItem(S.Format("spell.level", l), $"({Spellcasting.SlotsLeft(cls, Current.Character, l)})", Tag: l)));
    }

    private void ChooseSpellTarget()
    {
        var sp = _spell!;
        _pending = CommandKind.Cast;
        if (sp.TargetsEnemies)
        {
            var reach = CombatEngine.SpellReach(_state, Current, sp).ToList();
            if (reach.Count == 1) Set(new PlayerCommand(CommandKind.Cast, reach[0], -1, sp.Id, _slot));
            else ShowGroups(reach.ToHashSet());
        }
        else if (sp.Target == SpellTarget.Ally) ShowMembers();
        else Set(new PlayerCommand(CommandKind.Cast, -1, -1, sp.Id, _slot));
    }

    private void ShowGroups(HashSet<int>? allowed = null)
    {
        _phase = Phase.Target;
        var reach = allowed ?? _state.AttackableGroups(Current).ToHashSet();
        var items = _state.Groups.Select((g, i) => (g, i)).Where(x => !x.g.Wiped)
            .Select(x => new MenuItem($"{x.i + 1} {Services.Name(x.g.DisplayName)}", $"×{x.g.AliveCount}", reach.Contains(x.i), x.i));
        SetMenu(items);
    }

    private void ShowMembers()
    {
        _phase = Phase.Member;
        var items = _state.Party.Select((p, i) => new MenuItem($"{i + 1} {p.Character.Name}", $"{p.Hp}/{p.MaxHp}",
            _pending == CommandKind.Cast ? p.Character.State is LifeState.Alive or LifeState.Unconscious
                : p != Current && (_pending != CommandKind.Help || p.Standing), i));
        SetMenu(items);
    }

    /// <summary>一覧を入れ替え、カーソルをその人が前に選んだ項目に置く(選べなければ、選べる最初の項目)。</summary>
    private void SetMenu(IEnumerable<MenuItem> items)
    {
        var list = items.ToList();
        int at = Services.CombatCursor.TryGetValue((Current.Id, _phase.ToString()), out var last)
            ? list.FindIndex(i => i.Enabled && CursorKey(i.Tag) == last) : -1;
        _menu.SetItems(list, at >= 0 ? at : Math.Max(0, list.FindIndex(i => i.Enabled)));
    }

    /// <summary>
    /// 覚えておく選んだものの名前。呪文は id、グループはその敵の id、仲間はキャラの id にして、次の戦闘でも合わせられるようにする。
    /// </summary>
    private string? CursorKey(object? tag) => (_phase, tag) switch
    {
        (_, SpellDef sp) => sp.Id,
        (Phase.Target, int g) when g >= 0 && g < _state.Groups.Count => _state.Groups[g].Def.Id,
        (Phase.Member, int m) when m >= 0 && m < _state.Party.Count => _state.Party[m].Id,
        (_, "back") => null,
        _ => tag?.ToString(),
    };

    private void Choose(MenuItem item)
    {
        // その人がこの画面で選んだものを覚える(次に同じ画面を開くと、カーソルがそこにある)
        if (CursorKey(item.Tag) is { } key) Services.CombatCursor[(Current.Id, _phase.ToString())] = key;
        switch (_phase)
        {
            case Phase.Input when item.Tag is "auto" or "auto_nospell":
                foreach (var kv in CombatSimulator.AutoCommands(_state, Services.Data, useSpells: item.Tag is "auto")) _commands.TryAdd(kv.Key, kv.Value);
                RunRound();
                break;
            case Phase.Input when item.Tag is "back":
                PreviousMember();
                break;
            case Phase.Input when item.Tag is CommandKind.Attack:
                var reach = _state.AttackableGroups(Current).ToList();
                if (reach.Count == 1) Set(new PlayerCommand(CommandKind.Attack, reach[0]));
                else ShowGroups();
                break;
            case Phase.Input when item.Tag is CommandKind.Cast:
                ShowSpells();
                break;
            case Phase.Input when item.Tag is CommandKind.Item:
                ShowItems();
                break;
            case Phase.ItemList when item.Tag is int ii:
                _itemIndex = ii;
                ChooseItemTarget();
                break;
            case Phase.Target when item.Tag is int ig && _pending == CommandKind.Item:
                Set(new PlayerCommand(CommandKind.Item, ig, ItemIndex: _itemIndex));
                break;
            case Phase.Member when item.Tag is int im && _pending == CommandKind.Item:
                Set(new PlayerCommand(CommandKind.Item, -1, im, ItemIndex: _itemIndex));
                break;
            case Phase.Spell when item.Tag is SpellDef sp:
                _spell = sp;
                ShowSlots();
                break;
            case Phase.Slot when item.Tag is int level:
                _slot = level;
                ChooseSpellTarget();
                break;
            case Phase.Target when item.Tag is int tg && _pending == CommandKind.Cast:
                Set(new PlayerCommand(CommandKind.Cast, tg, -1, _spell!.Id, _slot));
                break;
            case Phase.Member when item.Tag is int tm && _pending == CommandKind.Cast:
                Set(new PlayerCommand(CommandKind.Cast, -1, tm, _spell!.Id, _slot));
                break;
            case Phase.Input when item.Tag is CommandKind.Help or CommandKind.Swap:
                _pending = (CommandKind)item.Tag;
                ShowMembers();
                break;
            case Phase.Input when item.Tag is CommandKind k:
                Set(new PlayerCommand(k));
                break;
            case Phase.Target when item.Tag is int g:
                Set(new PlayerCommand(CommandKind.Attack, g));
                break;
            case Phase.Member when item.Tag is int m:
                Set(new PlayerCommand(_pending, TargetMember: m));
                break;
        }
    }

    private void Set(PlayerCommand cmd)
    {
        _commands[Current.Id] = cmd;
        // 逃げるは全員で: 残りの人の行動は選ばずに、みんなで逃げる(だれか1人でも成功すれば逃げられる)
        if (cmd.Kind == CommandKind.Flee)
        {
            foreach (var p in _state.Party.Skip(_member + 1).Where(p => p.CanAct)) _commands[p.Id] = cmd;
            _member = _state.Party.Count;
            RunRound();
            return;
        }
        NextMember();
    }

    private void RunRound()
    {
        _engine.RunRound(_state, _commands);
        _phase = Phase.Playing;
        _wait = 0;
    }

    /// <summary>
    /// 倒れた人を後ろへ回し(Wizardry 風。立っている人の並びは変えない)、名簿のパーティの並びにも写す。
    /// </summary>
    private void SyncPartyOrder()
    {
        var sorted = _state.Party.Where(p => p.Standing).Concat(_state.Party.Where(p => !p.Standing)).ToList();
        _state.Party.Clear();
        _state.Party.AddRange(sorted);
        if (Services.Session is not { } session) return;
        session.Roster.Party = _state.Party.Select(p => p.Id).ToList();
    }

    // ---------------- 進行 ----------------

    public override void Update(GameTime time, InputState input)
    {
        _shake = Math.Max(0, _shake - time.ElapsedGameTime.TotalSeconds);
        // メニューのキーで、この戦闘のログを読み返す(読んでいるあいだ戦闘は止まる)
        if (input.Pressed(GameAction.Menu))
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            Stack.Push(new BacklogScene(_backlog));
            return;
        }
        switch (_phase)
        {
            case Phase.Playing:
                Play(time, input);
                break;
            case Phase.Result:
                if (input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel) || input.LeftClicked) End();
                break;
            default:
                var chosen = _menu.Update(input, Services.Audio);
                if (chosen is int i) Choose(_menu.Items[i]);
                else if (input.Pressed(GameAction.Cancel))
                {
                    Services.Audio.PlaySe(Sounds.Cancel);
                    if (_phase == Phase.Input) PreviousMember();
                    else if (_phase == Phase.Slot) ShowSpells();
                    else ShowCommands();
                }
                break;
        }
    }

    private void Play(GameTime time, InputState input)
    {
        _wait -= time.ElapsedGameTime.TotalSeconds;
        bool skip = input.Pressed(GameAction.Confirm) || input.LeftClicked;   // クリックでも1行進める
        bool fast = input.Held(GameAction.Confirm);
        if (_shown < _state.Log.Count)
        {
            if (_wait > 0 && !skip) return;
            var entry = _state.Log[_shown++];
            AddLine(Format(entry), CombatText.ColorOf(Services.Data, entry));
            PlayFor(entry);
            _wait = fast ? FastSeconds : LineSeconds;
            return;
        }
        if (_wait > 0 && !skip) return;
        SyncPartyOrder();   // ログを見せ終わってから並びを直す(先に並びが変わると、倒れたのが分かってしまう)
        if (_state.Outcome == CombatOutcome.Ongoing) BeginInput();
        else Finish();
    }

    private void AddLine(string text, Color? color = null)
    {
        _lines.Add(text);
        _lineColors.Add(color);
        _backlog.Add((text, color));
        if (_lines.Count > 40)
        {
            _lines.RemoveAt(0);
            _lineColors.RemoveAt(0);
        }
    }

    private void PlayFor(CombatLogEntry e)
    {
        string? se = e.Key switch
        {
            "combat.hit" => Sounds.Hit,
            "combat.crit" => Sounds.Critical,
            "combat.miss" => Sounds.Miss,
            "combat.slain" => Sounds.EnemyDie,
            "combat.cast" => Sounds.SpellCast,   // 敵味方とも、呪文を唱えたとき
            "combat.down" or "combat.dead" => Sounds.PartyDown,
            // 仲間がダメージを受けた声(クリティカルの直後は叫び声)
            "combat.damage" when e.Args[0] is LocalizedText who && _state.Party.Any(p => p.Character.Name == who.Get("en"))
                => _shown >= 2 && _state.Log[_shown - 2].Key == "combat.crit" ? Sounds.PartyHurtCrit : Sounds.PartyHurt,
            _ => null,
        };
        if (se != null) Services.Audio.PlaySe(se);

        // 絵に出ている敵にダメージが入ったら揺らす(クリティカルの直後は強く)
        if (e.Key == "combat.damage" && e.Args[0] is LocalizedText target && ArtGroup() is { } g && target.Get("en") == g.DisplayName.Get("en"))
        {
            _shake = ShakeSeconds;
            _shakePower = _shown >= 2 && _state.Log[_shown - 2].Key == "combat.crit" ? 16f : 8f;
        }
    }

    private void Finish()
    {
        _phase = Phase.Result;
        switch (_state.Outcome)
        {
            case CombatOutcome.Victory:
                // 勝っても音は鳴らさない(ユーザーの指定。Wizardry らしく静かに)
                foreach (var p in _state.Party.Where(p => p.Character.CanLevelUp && p.Character.State == LifeState.Alive))
                    AddLine(S.Format("combat.level_up_ready", p.Character.Name));
                break;
            case CombatOutcome.Defeat:
                Services.Audio.PlaySe(Sounds.PartyWipe);
                AddLine(S["combat.defeat"]);
                break;
        }
        KeepLog();
    }

    /// <summary>この戦闘のログを、キャンプで読み返せるように残し、combat.log にも足す。</summary>
    private void KeepLog()
    {
        Services.LastCombatLog = [.. _backlog];
        string floor = Services.Session?.Exploration is { } ex ? $"B{ex.Position.Floor + 1} " : "";
        string foes = string.Join(", ", _state.Groups.Select(g => $"{Services.Name(g.Def.Name)} x{g.Units.Count}"));
        Services.AppendCombatLog($"{DateTime.Now:yyyy-MM-dd HH:mm} {floor}{foes} -> {_state.Outcome} (round {_state.Round})",
            _backlog.Select(l => l.Text));
    }

    private void End()
    {
        // 戦闘の前の曲に戻す(迷宮の曲、イベントの曲など。全滅して町へ行くときは、町の曲に替わる)
        if (_previousBgm != null && _previousBgm != _bgm) Services.Audio.PlayBgm(_previousBgm, gain: _previousGain);
        else Services.Audio.StopBgm();
        if (_state.Outcome == CombatOutcome.Defeat)
        {
            // 全滅: 難易度に合わせて、死体を迷宮に残すか、町へ運ぶ。どちらもセーブして町へ
            var session = Services.Session;
            Stack.Pop();
            if (session?.Exploration is { } exploration)
            {
                session.Wipe(exploration.Position);
                session.TownNotice = Services.Strings[session.Difficulty.BodiesRemain ? "town.wiped_bodies" : "town.wiped_carried"];
                Services.SaveSession();
                Stack.Replace(new TownScene());
            }
            else
            {
                Services.Session = null;
                Stack.Replace(new TitleScene());
            }
            return;
        }
        // 先に自分を降ろしてから知らせる(知らせを受けた迷宮が宝箱の画面を積むため)
        Stack.Pop();
        _onEnd?.Invoke(_state.Outcome);
    }

    // ---------------- 文言 ----------------

    private string Format(CombatLogEntry e) => CombatText.Format(Services, e);

    // ---------------- 描画 ----------------

    public override void Draw(Draw2D d)
    {
        DrawArt(d);
        DrawGroups(d);

        Theme.Panel(d, LogArea);
        int start = Math.Max(0, _lines.Count - LogLines);
        for (int i = start; i < _lines.Count; i++)
        {
            bool latest = i == _lines.Count - 1;
            // 呪文の行は属性の色。古い行は暗くする
            var color = _lineColors[i] ?? Color.White;
            if (!latest) color = Color.Lerp(color, Theme.Dim, _lineColors[i] == null ? 1f : 0.45f);
            Fit(d, _lines[i], new Vector2(LogArea.X + 16, LogArea.Y + 10 + (i - start) * 32), LogArea.Width - 32, 24, color, FontStyle.Story);
        }

        Theme.Panel(d, CommandArea);
        switch (_phase)
        {
            case Phase.Input or Phase.Spell or Phase.Slot or Phase.ItemList or Phase.Target or Phase.Member:
                string ask = _phase switch
                {
                    Phase.Spell => S["combat.ask_spell"],
                    Phase.ItemList => S["combat.ask_item"],
                    Phase.Slot => S["combat.ask_slot"],
                    Phase.Target => S["combat.ask_group"],
                    Phase.Member => S["combat.ask_member"],
                    _ => S.Format("combat.ask", Current.Character.Name),
                };
                d.Text(ask, new Vector2(CommandArea.X + 16, CommandArea.Y + 12), 21, Theme.Title);
                _menu.Draw(d);
                break;
            case Phase.Playing:
                d.Text(S.Format("combat.round", Math.Max(1, _state.Round)), new Vector2(CommandArea.X + 16, CommandArea.Y + 12), 21, Theme.Title);
                d.TextWrapped(S["combat.skip_hint"], new Vector2(CommandArea.X + 16, CommandArea.Y + CommandArea.Height - 60), CommandArea.Width - 32, 15, Theme.Dim);
                break;
            case Phase.Result:
                d.TextWrapped(S["combat.result_hint"], new Vector2(CommandArea.X + 16, CommandArea.Y + CommandArea.Height - 60), CommandArea.Width - 32, 15, Theme.Dim);
                break;
        }
        if (_phase is Phase.Playing or Phase.Result)
            d.Text(S["combat.backlog_hint"], new Vector2(CommandArea.X + 16, CommandArea.Y + CommandArea.Height - 22), 15, Theme.Dim);

        int? highlight = _phase is Phase.Input or Phase.Spell or Phase.Slot or Phase.ItemList or Phase.Target or Phase.Member ? _member : null;
        PartyPanel.Draw(d, Services, PartyArea, highlight, Shown?.Party);
        DrawSpellInfo(d);
    }

    private static readonly RectangleF SpellInfoArea = new(60, 50, 560, 330);

    /// <summary>呪文やスロットを選んでいるあいだ、左上に呪文の効果を出す(スロットを選ぶときは、そのレベルでの効果)。</summary>
    private void DrawSpellInfo(Draw2D d)
    {
        SpellDef? sp = null;
        int level = 0;
        if (_phase == Phase.Spell && _menu.Current?.Tag is SpellDef cur)
        {
            sp = cur;
            level = cur.IsCantrip ? 0 : cur.Level;
        }
        else if (_phase == Phase.Slot && _spell != null)
        {
            sp = _spell;
            level = _menu.Current?.Tag is int l ? l : _spell.Level;
        }
        if (sp == null) return;
        if (sp.Scale == "character") level = Current.Character.Level;
        Theme.Panel(d, SpellInfoArea);
        float x = SpellInfoArea.X + 20, w = SpellInfoArea.Width - 40;
        float y = SpellInfoArea.Y + 16;
        y = d.TextWrapped(Services.Name(sp.Name), new Vector2(x, y), w, 26, Theme.Title) + 2;
        y = d.TextWrapped(SpellText.Header(S, sp), new Vector2(x, y), w, 17, Theme.Dim) + 6;
        foreach (var line in SpellText.Effects(S, sp, Math.Max(1, level)))
            y = d.TextWrapped(line, new Vector2(x, y), w, 19, Theme.Text, 6);
        if (sp.Note != null) d.TextWrapped(sp.Note, new Vector2(x, y + 6), w, 15, Theme.Dim, 4);
    }

    /// <summary>最後に見せたログの行(その時点の HP と敵の数を出す)。</summary>
    private CombatLogEntry? Shown => _shown > 0 && _phase == Phase.Playing ? _state.Log[_shown - 1] : null;

    private int Alive(int group) => Shown is { } e && group < e.GroupAlive.Count ? e.GroupAlive[group] : _state.Groups[group].AliveCount;

    /// <summary>絵に出す敵(生き残っている先頭のグループ)。</summary>
    private EnemyGroup? ArtGroup() => _state.Groups.Where((g, i) => Alive(i) > 0).FirstOrDefault();

    private void DrawArt(Draw2D d)
    {
        d.FillRect(ArtArea, new Color(6, 8, 12));
        d.Rect(ArtArea, Theme.Frame, 1.5f);
        var group = ArtGroup();
        if (group == null) return;
        var tex = Services.MonsterArt(group.Def);
        if (tex != null)
        {
            float scale = Math.Min((ArtArea.Width - 40) / tex.Width, (ArtArea.Height - 30) / tex.Height);
            float w = tex.Width * scale, h = tex.Height * scale;
            // 揺れ: 左右に細かく震わせ、だんだん弱める。当たった瞬間は少し赤く
            float t = (float)(_shake / ShakeSeconds);
            float dx = t > 0 ? MathF.Sin((float)_shake * 90f) * _shakePower * t : 0;
            var tint = Color.Lerp(Color.White, new Color(255, 120, 120), t * 0.8f);
            d.Image(tex, new RectangleF(ArtArea.X + (ArtArea.Width - w) / 2 + dx, ArtArea.Y + (ArtArea.Height - h) / 2, w, h), tint);
            return;
        }
        // 画像が無い敵は、影絵の代わりの枠と名前
        var c = new Vector2(ArtArea.X + ArtArea.Width / 2, ArtArea.Y + ArtArea.Height / 2);
        float r = group.Def.Size switch { "tiny" => 50, "small" => 70, "medium" => 95, "large" => 120, _ => 150 };
        var shadow = new Color(40, 46, 56);
        d.FillRect(new RectangleF(c.X - r * 0.45f, c.Y - r * 0.55f, r * 0.9f, r * 1.4f), shadow);
        d.FillRect(new RectangleF(c.X - r * 0.25f, c.Y - r * 0.95f, r * 0.5f, r * 0.45f), shadow);
        d.Text(Services.Name(group.DisplayName), new Vector2(c.X, ArtArea.Y + ArtArea.Height - 48), 26, Theme.Title, TextAlign.Center, FontStyle.Story);
    }

    private void DrawGroups(Draw2D d)
    {
        Theme.Panel(d, GroupArea);
        d.Text(S["combat.groups"], new Vector2(GroupArea.X + 16, GroupArea.Y + 12), 18, Theme.Dim);
        float y = GroupArea.Y + 48;
        int n = 0;
        for (int i = 0; i < _state.Groups.Count; i++)
        {
            var g = _state.Groups[i];
            int alive = Alive(i);
            if (alive == 0) continue;
            n++;
            bool near = n <= CombatState.NearGroups;
            Fit(d, $"{n} {Services.Name(g.DisplayName)}", new Vector2(GroupArea.X + 16, y), GroupArea.Width - 130, 22, Theme.Text);
            d.Text($"×{alive}", new Vector2(GroupArea.X + GroupArea.Width - 62, y), 22, Theme.Text, TextAlign.Right);
            d.Text(S[near ? "combat.near" : "combat.far"], new Vector2(GroupArea.X + GroupArea.Width - 20, y + 2), 18, near ? Theme.Warn : Theme.Dim, TextAlign.Right);
            y += 44;
        }
    }

    private static void Fit(Draw2D d, string text, Vector2 at, float width, float size, Color color, FontStyle style = FontStyle.Ui)
    {
        float w = d.Fonts.Measure(text, size, style).X;
        if (w > width) size *= width / w;
        d.Text(text, at, size, color, style: style);
    }
}

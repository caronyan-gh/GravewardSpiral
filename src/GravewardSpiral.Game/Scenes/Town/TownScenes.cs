using GravewardSpiral.Core.Save;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>町。酒場・宿屋・商店・寺院・訓練場と、迷宮への入口。町に来るたびに自動でセーブする。</summary>
public sealed class TownScene : MenuScene
{
    /// <summary>施設が10あるので、一覧の行を少し詰めて、全部が見えるようにする。</summary>
    public TownScene() : base(37f, 23f) { }

    private enum Place { Tavern, Inn, Shop, Temple, Training, House, Visit, Story, Maze, Quit }

    /// <summary>町の BGM(「Candlelight and Cold Stone」)。町の中の画面のあいだ、ずっと流す。</summary>
    public const string Bgm = "audio/bgm/town.mp3";

    /// <summary>町の名前(自由な迷宮は、由来で決まった町の名前)。</summary>
    private string TownName => Services.Session?.Origin is { } o && Services.Data.WorldParts.GetValueOrDefault(o.Town)?.Name is { } n
        ? Services.Name(n) : Services.Strings["town.title"];

    protected override string Title => Services.Session?.ClanName is { } clan
        ? Services.Strings.Format("town.title_clan", TownName, clan) : TownName;

    public override void Enter()
    {
        base.Enter();
        Services.Session!.ReturnToTown();
        Services.Audio.PlayBgm(Bgm);
        Message = Services.SaveSession() ? Services.Strings["town.saved"] : Services.Strings["town.save_failed"];
        if (Services.Session.TownNotice is { } notice)
        {
            Message = notice;
            Services.Session.TownNotice = null;
        }
        // 最下層のボス(final)を倒した証を持って戻るか、イベントで ending に着いたら、エンディング
        if (Services.Session.EndingDue(Services.Data)) Stack.Push(new EndingScene());
    }

    private bool PartyCanEnter => Services.Session!.Roster.PartyMembers.Any(c => c.CanAct);

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        yield return new MenuItem(s["town.tavern"], Tag: Place.Tavern);
        yield return new MenuItem(s["town.inn"], Tag: Place.Inn);
        yield return new MenuItem(s["town.shop"], Tag: Place.Shop);
        yield return new MenuItem(s["town.temple"], Tag: Place.Temple);
        yield return new MenuItem(s["town.training"], Tag: Place.Training);
        yield return new MenuItem(s["town.house"], Tag: Place.House);
        // 町のイベントがあるときだけ出す
        if (Services.Data.Events.Values.Any(e => e.Place == "town")) yield return new MenuItem(s["town.visit"], Tag: Place.Visit);
        yield return new MenuItem(s["town.maze"], Enabled: PartyCanEnter, Tag: Place.Maze);
        // この迷宮のいわれ(序文)があれば、読み返せる
        if (StoryScene.Available(Services)) yield return new MenuItem(s["town.story"], Tag: Place.Story);
        yield return new MenuItem(s["town.quit"], Tag: Place.Quit);
    }

    protected override void Choose(MenuItem item, int index)
    {
        switch ((Place)item.Tag!)
        {
            case Place.Tavern: Stack.Push(new TavernScene(Refresh)); break;
            case Place.Inn: Stack.Push(new InnScene()); break;
            case Place.Shop: Stack.Push(new ShopScene()); break;
            case Place.Temple: Stack.Push(new TempleScene()); break;
            case Place.Training: Stack.Push(new TrainingScene(Refresh)); break;
            case Place.Visit: Stack.Push(new VisitScene()); break;
            case Place.Story: Stack.Push(new StoryScene()); break;
            case Place.House: Stack.Push(new ClanHouseScene()); break;
            case Place.Maze:
                var state = Services.Session!.EnterMaze();
                Stack.Replace(new MazeScene(state, Services.Session.Request.Seed));
                break;
            case Place.Quit: Quit(); break;
        }
    }

    protected override void Cancel() => Stack.Push(new SystemMenuScene(Services.Strings["town.quit"], Quit));

    private void Quit() =>
        Stack.Push(new ConfirmScene(Services.Strings["town.confirm_quit"], () =>
        {
            Services.SaveSession();
            Services.Session = null;
            Stack.Replace(new TitleScene());
        }));

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        string key = List.Current?.Tag switch
        {
            Place.Tavern => "town.about_tavern",
            Place.Inn => "town.about_inn",
            Place.Shop => "town.about_shop",
            Place.Temple => "town.about_temple",
            Place.Training => "town.about_training",
            Place.House => "town.about_house",
            Place.Visit => "town.about_visit",
            Place.Story => "town.about_story",
            Place.Maze => PartyCanEnter ? "town.about_maze" : "town.about_maze_no_party",
            _ => "town.about_quit",
        };
        float y = area.Y + 20;
        y = Line(d, area, y, List.Current?.Label ?? "", Theme.Title, 26);
        Line(d, area, y, s[key], Theme.Text, 19);
    }
}

/// <summary>酒場: パーティを組む(最大6人。前の3人が前列)。</summary>
public sealed class TavernScene : MenuScene
{
    private enum Mode { Main, Add, Remove, Reorder }

    private readonly Action _onLeave;
    private Mode _mode;

    /// <summary>新しい隊列(選んだ順)。</summary>
    private readonly List<Character> _newOrder = [];

    public TavernScene(Action onLeave) => _onLeave = onLeave;

    private Roster Roster => Services.Session!.Roster;

    protected override string Title => Services.Strings["town.tavern"];

    protected override int? PartyHighlight => _mode switch
    {
        Mode.Remove => List.Cursor,
        Mode.Reorder when List.Current?.Tag is Character c => Roster.Party.IndexOf(c.Id),
        _ => null,
    };

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                yield return new MenuItem(s["tavern.add"], Enabled: Roster.Party.Count < Roster.MaxParty && Available().Any());
                yield return new MenuItem(s["tavern.remove"], Enabled: Roster.Party.Count > 0);
                yield return new MenuItem(s["tavern.swap"], Enabled: Roster.Party.Count > 1);
                yield return new MenuItem(s["tavern.rumor"]);
                yield return new MenuItem(s["common.back"]);
                break;
            case Mode.Add:
                foreach (var c in Available()) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
            case Mode.Reorder:
                // まだ選んでいない人だけ(選んだ並びは文で見せる)
                foreach (var c in Roster.PartyMembers.Where(c => !_newOrder.Contains(c))) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
            default:
                foreach (var c in Roster.PartyMembers) yield return new MenuItem(c.Name, Describe(c), Tag: c);
                break;
        }
    }

    private IEnumerable<Character> Available() =>
        Roster.Characters.Where(c => !Roster.InParty(c) && Roster.Available(c));

    private string Describe(Character c) => $"{Services.Name(Services.Data.Classes[c.ClassId].Name)} Lv{c.Level}";

    private void SetMode(Mode mode)
    {
        _mode = mode;
        List.SetItems(Items(), 0);
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (_mode)
        {
            case Mode.Main:
                if (index == 2)
                {
                    _newOrder.Clear();
                    Message = OrderMessage();
                }
                if (index == 3)
                {
                    Message = Rumors();
                    break;
                }
                SetMode(index switch { 0 => Mode.Add, 1 => Mode.Remove, 2 => Mode.Reorder, _ => Mode.Main });
                if (index == 4) Cancel();
                break;
            case Mode.Add:
                Roster.AddToParty((Character)item.Tag!);
                Message = s.Format("tavern.added", item.Label);
                if (Roster.Party.Count >= Roster.MaxParty || !Available().Any()) SetMode(Mode.Main);
                else Refresh();
                break;
            case Mode.Remove:
                Roster.RemoveFromParty((Character)item.Tag!);
                Message = s.Format("tavern.removed", item.Label);
                SetMode(Roster.Party.Count > 0 ? Mode.Remove : Mode.Main);
                break;
            case Mode.Reorder:
            {
                _newOrder.Add((Character)item.Tag!);
                // 残りが1人なら、その人が最後
                var rest = Roster.PartyMembers.Where(x => !_newOrder.Contains(x)).ToList();
                if (rest.Count == 1) _newOrder.Add(rest[0]);
                if (rest.Count <= 1)
                {
                    Roster.Reorder(_newOrder);
                    Message = s["camp.order_done"];
                    SetMode(Mode.Main);
                    break;
                }
                Message = OrderMessage();
                SetMode(Mode.Reorder);
                break;
            }
        }
    }

    /// <summary>
    /// 酒場の噂を1つ聞く。候補は、この世界にいる名のある敵のうち噂に乗っていてまだ倒されていないもの(階は「あたり」でぼかす。
    /// 本当の階か、1つ上か下)、シナリオの噂(条件に合うもの。出やすさの重み)、伝説の噂。聞くたびに重みで1つ選び、
    /// 候補が2つ以上あれば、すぐ前と同じものは選ばない。候補が無ければ、ありふれた話。
    /// </summary>
    private string Rumors()
    {
        var s = Services.Strings;
        var session = Services.Session!;
        var lines = new List<(string Text, double Weight, Action? Heard)>();
        foreach (var n in session.Named.Where(n => n.Rumored && !n.Defeated))
        {
            if (!Services.Data.NamedMonsters.TryGetValue(n.Id, out var def)) continue;
            // 階のぼかしは、同じ噂がいつも同じになるように id と階から決める
            int floors = session.Dungeon.Floors.Count;
            int shift = (n.Id.Sum(ch => ch) + n.Floor) % 3 - 1;
            int shown = Math.Clamp(n.Floor + shift, 0, floors - 1) + 1;
            lines.Add((s.Format(n.Lair ? "tavern.rumor_lair" : "tavern.rumor_wander", shown, Services.Name(def.Name)), 1, null));
        }
        // シナリオの噂(条件に合うもの。文はかぎ括弧なしで書く)
        var rng = session.Rng[Core.Rng.RngStreams.Names.Loot];
        var ctx = new Core.Events.EventContext { Data = Services.Data, Session = session, Rng = rng };
        foreach (var said in Core.Events.RumorDef.Available(Services.Data, ctx)) lines.Add((s.Format("tavern.rumor_said", Services.Name(said.Text)), said.Weight, null));
        // 伝説の噂(ほかの世界の話)。聞くと、クランが覚えて、次に作る世界で出やすくなる
        if (session.LegendRumor(Services.Data) is { Rumor: { } rumor } legend)
            lines.Add((s.Format("tavern.rumor_legend", Services.Name(rumor)), 1, () => session.HearLegend(legend.Id)));
        if (lines.Count > 1) lines.RemoveAll(l => l.Text == _lastRumor);
        if (lines.Count == 0) return s["tavern.rumor_none"];
        double x = rng.NextDouble() * lines.Sum(l => l.Weight);
        int i = 0;
        while (i < lines.Count - 1 && (x -= lines[i].Weight) >= 0) i++;
        lines[i].Heard?.Invoke();
        return _lastRumor = lines[i].Text;
    }

    /// <summary>すぐ前に聞いた噂(続けて同じ話をしない)。</summary>
    private string? _lastRumor;

    /// <summary>並べ直しの途中の文(ここまでの並びと、次は何番目か)。</summary>
    private string OrderMessage()
    {
        var s = Services.Strings;
        if (_newOrder.Count == 0) return s["camp.order_first"];
        string sofar = string.Join("  ", _newOrder.Select((c, i) => s.Format("camp.order_n", i + 1, c.Name)));
        return sofar + "\n" + s.Format("camp.order_next", _newOrder.Count + 1);
    }

    protected override void Cancel()
    {
        if (_mode == Mode.Main)
        {
            Stack.Pop();
            _onLeave();
        }
        else if (_mode == Mode.Reorder && _newOrder.Count > 0)
        {
            _newOrder.RemoveAt(_newOrder.Count - 1);   // 1人戻す
            Message = OrderMessage();
            SetMode(Mode.Reorder);
        }
        else
        {
            Message = null;
            SetMode(Mode.Main);
        }
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = Line(d, area, area.Y + 20, s[$"tavern.about_{_mode.ToString().ToLowerInvariant()}"], Theme.Text, 19);
        if (List.Current?.Tag is Character c) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, y + 10));
    }
}

/// <summary>宿屋: 大休憩。部屋の種類で料金と戻る HP が変わる。料金は1人ずつ自分の金で払う。</summary>
public sealed class InnScene : MenuScene
{
    private sealed record Room(string Key, int GoldPerLevel, double HpFraction);

    private static readonly Room[] Rooms = [new("inn.stable", 0, 0.5), new("inn.room", 10, 1.0)];

    protected override string Title => Services.Strings["town.inn"];

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        foreach (var r in Rooms)
            yield return new MenuItem(s[r.Key], r.GoldPerLevel == 0 ? s["inn.free"] : s.Format("inn.price", r.GoldPerLevel), Tag: r);
        yield return new MenuItem(s["camp.prepare"], Enabled: Services.Session!.Roster.PartyMembers.Any(Prepares), Tag: "prepare");
        yield return new MenuItem(s["common.back"]);
    }

    private bool Prepares(Character c) =>
        Spellcasting.CasterClasses(Services.Data, c).Any(cls => Spellcasting.Kind(cls) is CasterKind.Prepared or CasterKind.Book);

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is "prepare")
        {
            Stack.Push(new MemberPickScene(Services.Strings["camp.prepare"], Prepares, c => Stack.Push(new SpellPickScene(c, SpellPickScene.Kind.Prepared))));
            return;
        }
        if (item.Tag is not Room room)
        {
            Cancel();
            return;
        }
        var s = Services.Strings;
        // 料金はパーティの金から(足りる人まで、並びの順に休む)
        var party = Services.Session!.Roster.PartyMembers.ToList();
        var skipped = new List<string>();
        long paid = 0;
        int rested = 0;
        foreach (var c in party)
        {
            if (!c.IsAlive) continue;
            long price = room.GoldPerLevel * (long)c.Level;
            if (!Core.Items.PartyGold.Pay(party, price))
            {
                skipped.Add(c.Name);
                continue;
            }
            paid += price;
            c.LongRest(room.HpFraction);
            rested++;
        }
        if (paid > 0) Services.Audio.PlaySe(Audio.Sounds.Coins);
        if (rested > 0) Services.Audio.PlaySe(Audio.Sounds.InnRest);
        Message = skipped.Count == 0 ? s["inn.rested"] : s.Format("inn.no_money", Services.Strings.List(skipped));
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        string key = List.Current?.Tag is Room r ? r.Key + "_about" : "inn.about";
        float y = Line(d, area, area.Y + 20, s["inn.about"], Theme.Dim, 18);
        Line(d, area, y, s[key], Theme.Text, 19);
    }
}

/// <summary>訓練場: キャラクターを作る・見る・レベルを上げる・名前を変える・消す。</summary>
public sealed class TrainingScene : MenuScene
{
    /// <summary>名簿に載せられる人数(クランハウスの大広間で増える)。</summary>
    private int MaxRoster => Services.Session!.ClanHouse.RosterLimit(Core.Save.ClanHouseLevel.Load(Services.Content));
    private readonly Action _onLeave;
    private bool _pickingClass;   // レベルアップで、上げるクラスを選んでいる
    private Character? _selected;

    public TrainingScene(Action onLeave) => _onLeave = onLeave;

    private Roster Roster => Services.Session!.Roster;

    protected override string Title => _selected?.Name ?? Services.Strings["town.training"];

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        if (_selected is { } pc && _pickingClass)
        {
            // 上げるクラス: 今のクラスと、マルチクラスの条件を満たすクラス(満たさないものは条件を出して選べない)
            var data = Services.Data;
            foreach (var id in pc.ClassIds.Concat(data.ClassOrder.Where(id => !pc.ClassIds.Contains(id))))
            {
                var cls = data.Classes[id];
                int lv = pc.LevelIn(id);
                bool ok = pc.CanTakeClass(data, id);
                string detail = lv > 0 ? $"Lv{lv} → {lv + 1}" : ok ? s["training.new_class"] : Requirement(cls) + (cls.MeetsMulticlass(pc.Scores) ? s["training.now_short"] : "");
                yield return new MenuItem(Services.Name(cls.Name), detail, ok, cls);
            }
            yield return new MenuItem(s["common.back"], Tag: "back");
            yield break;
        }
        if (_selected is { } c)
        {
            yield return new MenuItem(s["training.status"], Tag: "status");
            yield return new MenuItem(s["camp.items"], Tag: "items");
            yield return new MenuItem(s["training.level_up"], c.CanLevelUp ? s["training.can"] : null, Enabled: c.CanLevelUp && c.IsAlive, Tag: "level");
            if (c.AbilityPoints > 0) yield return new MenuItem(s.Format("training.asi", c.AbilityPoints), Enabled: c.IsAlive, Tag: "asi");
            if (c.EpicPoints > 0) yield return new MenuItem(s.Format("training.boon", c.EpicPoints), Tag: "boon");
            // 呪文は術者のクラスごと(マルチクラスでは、選ぶ画面で左右にクラスを切り替える)
            var casters = Spellcasting.CasterClasses(Services.Data, c).ToList();
            if (casters.Any(cls => Spellcasting.CantripsKnown(cls, c) > 0)) yield return new MenuItem(s["training.cantrips"], Tag: "cantrips");
            var knowers = casters.Where(cls => Spellcasting.Kind(cls) is CasterKind.Known or CasterKind.Pact or CasterKind.Book && Spellcasting.SpellsKnown(cls, c) > 0).ToList();
            if (knowers.Count > 0)
                yield return new MenuItem(s[knowers.All(cls => Spellcasting.Kind(cls) == CasterKind.Book) ? "training.spellbook" : "training.known"], Tag: "known");
            if (casters.Any(cls => Spellcasting.Kind(cls) is CasterKind.Prepared or CasterKind.Book && Spellcasting.PreparedCount(cls, c) > 0))
                yield return new MenuItem(s["camp.prepare"], Tag: "prepared");
            yield return new MenuItem(s["training.rename"], Tag: "rename");
            yield return new MenuItem(s["training.delete"], Tag: "delete");
            yield return new MenuItem(s["common.back"], Tag: "back");
            yield break;
        }
        yield return new MenuItem(s["training.create"], Roster.Characters.Count >= MaxRoster ? s["training.full"] : null,
            Enabled: Roster.Characters.Count < MaxRoster, Tag: "create");
        foreach (var ch in Roster.Characters)
        {
            // ほかの世界の迷宮にいる人は、この世界では使えない
            if (Roster.Away.TryGetValue(ch.Id, out var where))
                yield return new MenuItem(ch.Name, s.Format("roster.away", Services.ScenarioDisplayName(where.StartsWith("free-") ? null : where)), Enabled: false, Tag: ch);
            else
                yield return new MenuItem(ch.Name, $"{Services.Name(Services.Data.Classes[ch.ClassId].Name)} Lv{ch.Level}{(ch.CanLevelUp || ch.AbilityPoints > 0 ? " ↑" : "")}", Tag: ch);
        }
        yield return new MenuItem(s["training.export_clan"], Enabled: Roster.Characters.Count > 0, Tag: "export");
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    /// <summary>マルチクラスの条件の文(「筋力 13 か 敏捷力 13」)。</summary>
    private string Requirement(ClassDef cls)
    {
        var s = Services.Strings;
        return string.Join(s["training.or"], cls.Multiclass.Select(g => Services.Strings.List(g.Select(kv => $"{s["ability." + kv.Key]} {kv.Value}"))));
    }

    private void Select(Character? c)
    {
        _pickingClass = false;
        _selected = c;
        List.SetItems(Items(), 0);
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (item.Tag)
        {
            case "export":
                // 名簿をまるごとクランファイルに書き出す(別のシナリオや新しいゲームに連れていける)
                Stack.Push(new TextEntryScene(s["training.clan_name"], Services.Session!.ClanName ?? "", NewGameScene.ClanNameLength, name =>
                {
                    Services.Session!.ClanName ??= name;   // 名前の無いクランなら、書き出した名前をクランの名前にする
                    Core.Save.OriginalItems.Snapshot(Services.Data, Core.Save.OriginalItems.All(Services.Session!));   // オリジナルの品は中身も書く
                    var clan = ClanFile.From(Services.Session!, name, Services.Content.Scenario?.Id);
                    try
                    {
                        clan.Save(AppPaths.Clans);
                        Message = s.Format("training.clan_saved", name, clan.Characters.Count);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Message = ex.Message;
                    }
                }, suggest: Services.ClanNameSuggester()));
                break;
            case "create":
                Stack.Push(new CharacterCreateScene(c =>
                {
                    Roster.Characters.Add(c);
                    if (Roster.Party.Count < Roster.MaxParty) Roster.AddToParty(c);
                    Message = s.Format("training.created", c.Name);
                    Refresh();
                }));
                break;
            case Character c:
                Select(c);
                break;
            case "status":
                Stack.Push(new StatusScene(_selected!));
                break;
            case "items":
                Stack.Push(new InventoryScene(_selected!));
                break;
            case "boon":
                Stack.Push(new BoonScene(_selected!, Refresh));
                break;
            case "asi":
                Stack.Push(new AbilityIncreaseScene(_selected!, Refresh));
                break;
            case "cantrips":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Cantrips));
                break;
            case "known":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Known));
                break;
            case "prepared":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Prepared));
                break;
            case "level":
                _pickingClass = true;
                List.SetItems(Items(), 0);
                break;
            case ClassDef cls when _pickingClass:
            {
                // 1レベルずつ、選んだクラスを上げる(確かめてから。まだ上げられれば、続けて選ぶ)
                var who = _selected!;
                string ask = who.LevelIn(cls.Id) > 0
                    ? s.Format("training.confirm_level", Services.Name(cls.Name), who.LevelIn(cls.Id) + 1)
                    : s.Format("training.confirm_multiclass", Services.Name(cls.Name));
                Stack.Push(new ConfirmScene(ask, () =>
                {
                    int before = who.MaxHp;
                    who.LevelUp(Services.Data, cls.Id, maxLevels: 1);
                    Message = s.Format("training.leveled_class", who.Name, Services.Name(cls.Name), who.LevelIn(cls.Id), who.MaxHp - before);
                    if (who.AbilityPoints > 0) Message += "\n" + s.Format("training.asi_ready", who.AbilityPoints);
                    Services.Audio.PlaySe(Audio.Sounds.LevelUp);
                    if (!who.CanLevelUp) _pickingClass = false;
                    List.SetItems(Items(), 0);
                }));
                break;
            }
            case "rename":
                var target = _selected!;
                Stack.Push(new TextEntryScene(s["training.rename"], target.Name, CharacterFactory.MaxNameLength, name =>
                {
                    target.Name = name;
                    Refresh();
                }, suggest: CharacterCreateScene.NameSuggester(Services, target.RaceId)));
                break;
            case "delete":
                var victim = _selected!;
                Stack.Push(new ConfirmScene(s.Format("training.confirm_delete", victim.Name), () =>
                {
                    Roster.Delete(victim);
                    Message = s.Format("training.deleted", victim.Name);
                    Select(null);
                }));
                break;
            case "back" when _pickingClass:
                _pickingClass = false;
                List.SetItems(Items(), 0);
                break;
            case "back":
                Cancel();
                break;
        }
    }

    protected override void Cancel()
    {
        if (_pickingClass)
        {
            _pickingClass = false;
            List.SetItems(Items(), 0);
            return;
        }
        if (_selected != null)
        {
            Select(null);
            return;
        }
        Stack.Pop();
        _onLeave();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        // 上げるクラスを選んでいるときは、カーソルのクラスの説明
        if (_pickingClass && _selected is { } who && List.Current?.Tag is ClassDef cls)
        {
            var s = Services.Strings;
            float y = Line(d, area, area.Y + 20, Services.Name(cls.Name), Theme.Title, 26);
            int now = who.LevelIn(cls.Id);
            y = Line(d, area, y, now > 0 ? s.Format("training.class_next", now, now + 1) : s["training.class_new"], Theme.Gold, 19);
            y = Line(d, area, y, s.Format("create.hit_die", cls.HitDie), Theme.Text, 19);
            y = Line(d, area, y, s.Format("create.saves", s.List(cls.Saves.Select(x => s[$"ability.{x}"]))), Theme.Text, 19);
            y = Line(d, area, y, s.Format("create.primary", s.List(cls.PrimaryAbilities.Take(2).Select(a => s[$"ability.{a.Code()}"]))), Theme.Text, 19);
            Line(d, area, y + 6, Services.Name(cls.Description), Theme.Dim, 18);
            return;
        }
        var c = _selected ?? List.Current?.Tag as Character;
        if (c != null) StatusScene.DrawSummary(d, Services, c, new Vector2(area.X + 20, area.Y + 20));
        else Line(d, area, area.Y + 20, Services.Strings["training.about"], Theme.Text, 19);
    }
}

/// <summary>
/// キャラクターの詳しい状態。決定か取消で閉じる。左は要約、右は左右で「技能・能力」「呪文」(術者だけ)「装備」「持ち物」のページを切り替える
/// (高いレベルの術者の呪文の一覧や、全部の枠に付けた装備、大きな袋の中身は、同じ欄に並べると枠からはみ出すため)。
/// </summary>
public sealed class StatusScene : Scene
{
    private readonly Character _c;
    private int _page;

    public StatusScene(Character c) => _c = c;

    /// <summary>右側のページ(呪文のページは術者だけ)。</summary>
    private string[] Pages => Spellcasting.Castable(Services.Data, _c).Any() ? ["skills", "spells", "equipment", "bag"] : ["skills", "equipment", "bag"];

    public override void Update(GameTime time, Input.InputState input)
    {
        int step = input.Pressed(Input.GameAction.Right) ? 1 : input.Pressed(Input.GameAction.Left) ? -1 : 0;
        if (step != 0)
        {
            _page = (_page + step + Pages.Length) % Pages.Length;
            Services.Audio.PlaySe(Audio.Sounds.Cursor);
            return;
        }
        if (input.Pressed(Input.GameAction.Confirm) || input.Pressed(Input.GameAction.Cancel) || input.LeftClicked || input.RightClicked)
        {
            Services.Audio.PlaySe(Audio.Sounds.Cancel);
            Stack.Pop();
        }
    }

    /// <summary>短い要約(名前・種族とクラス・レベル・HP・AC・能力値)。</summary>
    public static float DrawSummary(Draw2D d, GameServices sv, Character c, Vector2 at)
    {
        var s = sv.Strings;
        var data = sv.Data;
        float y = at.Y;
        void Text(string t, Color color, float size = 20f)
        {
            d.Text(t, new Vector2(at.X, y), size, color);
            y += size + 9;
        }
        var race = data.Races[c.RaceId];
        Text(c.Name, Theme.Title, 26);
        Text($"{sv.Name(race.Subrace ?? race.Name)}  {sv.Name(data.Classes[c.ClassId].Name)}  Lv{c.Level}  {s[$"alignment.{c.Alignment.ToString().ToLowerInvariant()}"]}", Theme.Text);
        Text(s.Format("status.hp_ac", c.Hp, c.MaxHp, c.ArmorClass(data), c.ProficiencyBonus), Theme.Text);
        Text(s.Format("status.hit_dice", c.HitDice, c.Level, data.Classes[c.ClassId].HitDie), c.HitDice == 0 ? Theme.Warn : Theme.Dim, 18);
        Text(string.Join("  ", Abilities.All.Select(a => $"{s[$"ability_short.{a.Code()}"]} {c.Scores[a]}")), Theme.Text, 18);
        Text(s.Format("status.xp", c.Xp, Progression.XpForLevel(c.Level + 1)), Theme.Dim, 18);
        Text(s.Format("status.gold", c.Gold), Theme.Gold, 18);
        if (!c.ArmorProficient(data)) Text(s["status.armor_unproficient"], Theme.Warn, 18);   // 呪文を唱えられず、攻撃が不利
        // 習熟(鎧と盾は、無いと着られない。武器は持てるが、習熟ボーナスが付かない)。武器は長いので、欄の幅で折り返す
        var (armor, weapons) = Proficiencies(sv, c);
        Text(s.Format("status.armor_prof", armor), Theme.Text, 18);
        y = d.TextWrapped(s.Format("status.weapon_prof", weapons), new Vector2(at.X, y), 500, 18, Theme.Text, 9);
        if (UI.SpellText.SlotLines(s, data, c) is { } slots)
            foreach (var line in slots) Text(line, Theme.Text, 18);
        return y;
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        var data = Services.Data;
        var area = new RectangleF(80, 60, 1120, 600);
        Theme.Panel(d, area);
        float y = DrawSummary(d, Services, _c, new Vector2(area.X + 30, area.Y + 24));

        // 右側: 技能・セーヴ・クラスの能力か、呪文(術者は左右で切り替える)
        float x2 = area.X + 600;
        float y2 = area.Y + 24;
        void Text2(string t, Color color, float size = 18f)
        {
            d.Text(t, new Vector2(x2, y2), size, color);
            y2 += size + 8;
        }
        var castable = Spellcasting.Castable(data, _c).ToList();
        var pages = Pages;
        _page = Math.Clamp(_page, 0, pages.Length - 1);
        string page = pages[_page];
        // ページの見出し(今のページを明るく)
        float hx = x2;
        d.Text("◀", new Vector2(hx, y2 + 2), 16, Theme.Dim);
        hx += 24;
        for (int i = 0; i < pages.Length; i++)
        {
            if (i > 0)
            {
                d.Text("/", new Vector2(hx, y2), 18, Theme.Dim);
                hx += 16;
            }
            string name = s["status.page_" + pages[i]];
            d.Text(name, new Vector2(hx, y2), 18, i == _page ? Theme.Title : Theme.Dim);
            hx += d.Fonts.Measure(name, 18).X + 10;
        }
        d.Text("▶", new Vector2(hx, y2 + 2), 16, Theme.Dim);
        y2 += 34;
        if (page == "equipment")
        {
            // 付けているものだけを出す(名前は見える姿で。未鑑定や、呪いがまだ分からない品の正体は出さない)
            Text2(s["status.equipment"], Theme.Title, 22);
            foreach (var slot in Enum.GetValues<EquipSlot>())
            {
                if (_c.Inventory[slot] is not { } it) continue;
                y2 = d.TextWrapped($"{s[$"slot.{slot.ToString().ToLowerInvariant()}"]}: {UI.ItemText.Name(Services, it)}", new Vector2(x2, y2),
                    area.X + area.Width - x2 - 24, 17, Theme.Text, 7);
            }
        }
        else if (page == "bag")
        {
            // 1つずつの行で2列に(一番大きな袋の 30 個まで収まる)。長い名前は欄に収まるまで字を小さくする
            Text2(s.Format("status.bag", _c.Inventory.Items.Count, _c.Inventory.Capacity(data.Items)), Theme.Title, 22);
            var items = _c.Inventory.Items;
            int rows = Math.Max(1, (items.Count + 1) / 2);
            float colW = (area.X + area.Width - x2 - 24) / 2;
            for (int i = 0; i < items.Count; i++)
            {
                string name = UI.ItemText.Name(Services, items[i]);
                float size = Math.Min(17f, 17f * (colW - 12) / Math.Max(1f, d.Fonts.Measure(name, 17).X));
                d.Text(name, new Vector2(x2 + (i / rows) * colW, y2 + (i % rows) * 24 + (17 - size) / 2), size, Theme.Text);
            }
        }
        else if (page == "spells")
        {
            // 呪文の難易度と攻撃の修正は、術者のクラスごと
            foreach (var cls in Spellcasting.CasterClasses(data, _c))
                Text2((Spellcasting.CasterClasses(data, _c).Count() > 1 ? Services.Name(cls.Name) + " " : "")
                    + s.Format("status.spells", Spellcasting.SaveDc(cls, _c), Signed(Spellcasting.AttackBonus(cls, _c))), Theme.Title, 22);
            foreach (var group in castable.GroupBy(x => x.Level))
            {
                // 呪文の名前は長いので、欄の幅で折り返す
                string line = $"{UI.SpellText.Level(s, group.First())}: {Services.Strings.List(group.Select(x => Services.Name(x.Name)))}";
                y2 = d.TextWrapped(line, new Vector2(x2, y2), area.X + area.Width - x2 - 24, 16, Theme.Text, 6);
            }
        }
        else
        {
        Text2(s["status.skills"], Theme.Title, 22);
        foreach (var id in _c.Skills)
            Text2($"{Services.Name(data.Skills[id].Name)}  {Signed(_c.SkillBonus(data, id))}", Theme.Text);
        y2 += 8;
        Text2(s["status.saves"], Theme.Title, 22);
        // 6つ並ぶと欄の幅を越えるので、3つずつ2行に(体の能力値と、心の能力値)
        foreach (var three in Abilities.All.Chunk(3))
            Text2(string.Join("  ", three.Select(a => $"{s[$"ability.{a.Code()}"]} {Signed(_c.SaveBonus(data, a))}")), Theme.Text);
        // クラスの能力(マルチクラスでは全部のクラスのもの): 回数の残りと、勝手に効く能力の名前
        var pools = ClassFeatures.Pools.Where(p => ClassFeatures.MaxUses(_c, p) > 0).ToList();
        var passives = ClassFeatures.Passives(_c).Distinct().Select(id => s["passive." + id]).ToList();
        if (ClassFeatures.StyleOf(data, _c) is var style and not FightingStyle.None)
            passives.Insert(0, s.Format("status.fighting_style", s["style." + style.ToString().ToLowerInvariant()]));
        if (pools.Count > 0 || passives.Count > 0)
        {
            y2 += 8;
            Text2(s["status.features"], Theme.Title, 22);
            // 回数は1つずつ行を分ける(折り返しで「5/5」が切れないように)
            foreach (var p in pools) Text2($"{s["pool." + p]}  {ClassFeatures.UsesLeft(_c, p)}/{ClassFeatures.MaxUses(_c, p)}", Theme.Text, 17);
            if (passives.Count > 0)
                y2 = d.TextWrapped(s.List(passives), new Vector2(x2, y2), area.X + area.Width - x2 - 24, 16, Theme.Dim, 6);
        }
        }

        d.Text(s["status.close_pages"], new Vector2(640, 690), 15, Theme.Dim, TextAlign.Center);
    }

    /// <summary>鎧と武器の習熟の一覧(クラスを合わせて。分類は名前で、決まった武器は品の名前で)。</summary>
    public static (string Armor, string Weapons) Proficiencies(GameServices sv, Character c)
    {
        var s = sv.Strings;
        var classes = c.ClassIds.Select(id => sv.Data.Classes[id]).ToList();
        var armor = new[] { "light", "medium", "heavy", "shield" }.Where(a => classes.Any(k => k.Armor.Contains(a)))
            .Select(a => a == "shield" ? s["status.prof_shield"] : s["armor." + a]).ToList();
        var weapons = classes.SelectMany(k => k.Weapons).Distinct()
            .OrderBy(w => w switch { "simple" => 0, "martial" => 1, _ => 2 })
            .Select(w => w is "simple" or "martial" ? s["weapon." + w] : sv.Data.Items.TryGetValue(w, out var def) ? sv.Name(def.Name) : w).ToList();
        string none = s["status.prof_none"];
        return (armor.Count == 0 ? none : s.List(armor), weapons.Count == 0 ? none : s.List(weapons));
    }

    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
}

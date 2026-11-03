using GravewardSpiral.Core.Save;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>町。クランハウス・酒場・宿屋・商店・寺院・訓練場と、迷宮への入口。町に来るたびに自動でセーブする。</summary>
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
        yield return new MenuItem(s["town.house"], Tag: Place.House);   // クランの拠点(パーティを組む)を一番上に
        yield return new MenuItem(s["town.tavern"], Tag: Place.Tavern);
        yield return new MenuItem(s["town.inn"], Tag: Place.Inn);
        yield return new MenuItem(s["town.shop"], Tag: Place.Shop);
        yield return new MenuItem(s["town.temple"], Tag: Place.Temple);
        yield return new MenuItem(s["town.training"], Tag: Place.Training);
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
            case Place.House: Stack.Push(new ClanHouseScene(Refresh)); break;
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
            Place.Temple => Services.Session!.Difficulty.RiskyRevive ? "town.about_temple" : "town.about_temple_sure",   // 蘇生が失敗しない難易度では、灰の警告を出さない
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

/// <summary>酒場: 客の噂を聞く(払うほど役に立つ話になる。TavernRumors)。パーティを組むのはクランハウスの「仲間の管理」(前は酒場だった)。</summary>
public sealed class TavernScene : MenuScene
{
    private readonly Action _onLeave;

    public TavernScene(Action onLeave) => _onLeave = onLeave;

    protected override string Title => Services.Strings["town.tavern"];

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        var session = Services.Session!;
        long gold = Core.Items.PartyGold.Total(session.Roster.PartyMembers.ToList());
        for (int tier = 0; tier <= Core.Events.TavernRumors.MaxTier; tier++)
        {
            int price = Core.Events.TavernRumors.Prices[tier];
            string label = s["tavern.ask." + Core.Events.TavernRumors.Sources[tier]];
            if (!Core.Events.TavernRumors.Present(tier, session.Request.Seed, session.Exploration?.Steps ?? 0))
                yield return new MenuItem(label, s["tavern.away"], false, tier);   // 今日はいない
            else
                yield return new MenuItem(label, price > 0 ? $"{price} G" : null, gold >= price, tier);
        }
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        if (item.Tag is int tier)
        {
            Message = Rumors(tier);
            Refresh();   // 払えるかが変わる
        }
        else Cancel();
    }

    /// <summary>
    /// 噂を1つ聞く(tier は誰に聞くか)。候補は、噂の文(パックの rumors.json。条件に合うもの。格は tier)、この世界にいる名のある敵のうち
    /// 噂に乗っていてまだ倒されていないもの(冒険者は階を「あたり」でぼかし、情報屋は本当の階)、伝説の噂(旅商人)。
    /// その格に話が無ければ、お金を取らない。
    /// </summary>
    private string Rumors(int tier)
    {
        var s = Services.Strings;
        var session = Services.Session!;
        var all = new List<Core.Events.TavernRumors.Candidate>();
        // 噂の文(条件に合うもの。文はかぎ括弧なしで書く)
        var rng = session.Rng[Core.Rng.RngStreams.Names.Loot];
        var ctx = new Core.Events.EventContext { Data = Services.Data, Session = session, Rng = rng };
        foreach (var said in Core.Events.RumorDef.Available(Services.Data, ctx))
            all.Add(new(Math.Clamp(said.Tier, 0, Core.Events.TavernRumors.MaxTier), s.Format("tavern.rumor_said", Services.Name(said.Text)), said.Weight));
        foreach (var n in session.Named.Where(n => n.Rumored && !n.Defeated))
        {
            if (!Services.Data.NamedMonsters.TryGetValue(n.Id, out var def)) continue;
            // 階のぼかしは、同じ噂がいつも同じになるように id と階から決める
            int floors = session.Dungeon.Floors.Count;
            int shift = (n.Id.Sum(ch => ch) + n.Floor) % 3 - 1;
            int shown = Math.Clamp(n.Floor + shift, 0, floors - 1) + 1;
            all.Add(new(2, s.Format(n.Lair ? "tavern.rumor_lair" : "tavern.rumor_wander", shown, Services.Name(def.Name)), 1));
            all.Add(new(3, s.Format(n.Lair ? "tavern.rumor_lair_exact" : "tavern.rumor_wander_exact", n.Floor + 1, Services.Name(def.Name)), 1));
        }
        // 伝説の噂(ほかの世界の話)。聞くと、クランが覚えて、次に作る世界で出やすくなる
        if (session.LegendRumor(Services.Data) is { Rumor: { } rumor } legend)
            all.Add(new(4, s.Format("tavern.rumor_legend", Services.Name(rumor)), 1, () => session.HearLegend(legend.Id)));
        var picked = Core.Events.TavernRumors.Pick(all, tier, _lastRumor, rng);
        if (picked == null) return s[tier == 0 ? "tavern.rumor_none" : "tavern.no_story." + Core.Events.TavernRumors.Sources[tier]];
        int price = Core.Events.TavernRumors.Prices[tier];
        if (price > 0)
        {
            if (!Core.Items.PartyGold.Pay(Services.Session!.Roster.PartyMembers.ToList(), price)) return s["tavern.no_money"];
            Services.Audio.PlaySe(Audio.Sounds.Coins);
        }
        picked.Heard?.Invoke();
        return _lastRumor = picked.Text;
    }

    /// <summary>すぐ前に聞いた噂(続けて同じ話をしない)。</summary>
    private string? _lastRumor;

    protected override void Cancel()
    {
        Stack.Pop();
        _onLeave();
    }

    protected override void DrawInfo(Draw2D d, RectangleF area) =>
        Line(d, area, area.Y + 20, Services.Strings["tavern.about_main"], Theme.Text, 19);
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
            Stack.Push(new MemberPickScene(Services.Strings["camp.prepare"], Prepares, c => Stack.Push(new SpellPickScene(c, SpellPickScene.Kind.Prepared, Services.Session!.Roster.PartyMembers.ToList()))));
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

    /// <summary>選んだ人がパーティにいればパーティ(持ち物などの画面で、仲間を切り替えられる)。いなければ null(その人だけ)。</summary>
    private List<Character>? SelectedParty => Roster.PartyMembers.ToList() is var party && _selected != null && party.Contains(_selected) ? party : null;

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
            if (c.AbilityPoints(Services.Data) > 0) yield return new MenuItem(s.Format("training.asi", c.AbilityPoints(Services.Data)), Enabled: c.IsAlive, Tag: "asi");
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
            {
                // 「↑」の右下に小さく、レベルを上げられるなら LV、振っていない能力値上昇の点があれば ST(両方なら LV)
                string? mark = ch.CanLevelUp ? s["training.mark_level"] : ch.AbilityPoints(Services.Data) > 0 ? s["training.mark_asi"] : null;
                yield return new MenuItem(ch.Name, $"{Services.Name(Services.Data.Classes[ch.ClassId].Name)} Lv{ch.Level}{(mark != null ? " ↑" : "")}", Tag: ch, DetailMark: mark);
            }
        }
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
                Stack.Push(new StatusScene(_selected!, SelectedParty));
                break;
            case "items":
                Stack.Push(new InventoryScene(_selected!, SelectedParty));
                break;
            case "boon":
                Stack.Push(new BoonScene(_selected!, Refresh));
                break;
            case "asi":
                Stack.Push(new AbilityIncreaseScene(_selected!, Refresh));
                break;
            case "cantrips":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Cantrips, SelectedParty));
                break;
            case "known":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Known, SelectedParty));
                break;
            case "prepared":
                Stack.Push(new SpellPickScene(_selected!, SpellPickScene.Kind.Prepared, SelectedParty));
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
                    if (who.AbilityPoints(Services.Data) > 0) Message += "\n" + s.Format("training.asi_ready", who.AbilityPoints(Services.Data));
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

    /// <summary>成長表のボタン(クラスを選んでいるときに説明の欄に出す。Draw で決める)。</summary>
    private RectangleF? _growthButton;
    private Vector2? _mouse;   // ボタンを光らせる

    public override void Update(GameTime time, Input.InputState input)
    {
        _mouse = input.Mouse;
        // クラスを選んでいるときは、地図のキーかボタンで、カーソルのクラスの成長表を開く
        if (_pickingClass && _selected is { } who && List.Current?.Tag is ClassDef cls
            && (input.Pressed(Input.GameAction.Map) || input.LeftClicked && _growthButton is { } b && input.Mouse is { } m && b.Contains(m)))
        {
            Services.Audio.PlaySe(Audio.Sounds.Confirm);
            Stack.Push(new ClassGrowthScene(cls, who.LevelIn(cls.Id)));
            return;
        }
        base.Update(time, input);
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
            if (now == 0 && who.ClassIds.Any()) y = Line(d, area, y, s["training.no_multiclass_saves"], Theme.Dim, 16);   // マルチクラスでは、セーヴの習熟は得ない(5版)
            y = Line(d, area, y, s.Format("create.primary", s.List(cls.PrimaryAbilities.Take(2).Select(a => s[$"ability.{a.Code()}"]))), Theme.Text, 19);
            var gains = ClassGrowth.Gains(Services, who, cls);
            // 得る技と能力の名前の上にマウスを載せると、その説明
            y = gains.Count > 0
                ? UI.LinkFlow.Draw(d, gains, s["common.list_sep"], new Vector2(area.X + 20, y + 6), area.Width - 70, 18, Theme.Good, lead: s.Format("training.gains", "")) + 2
                : Line(d, area, y + 6, s["training.gains_none"], Theme.Good, 18);
            // 成長表のボタン(地図のキーでも開く)
            string label = s["training.growth"];
            float w = d.Fonts.Measure(label, 17).X + 28;
            var button = new RectangleF(area.X + 20, y + 8, w, 32);
            bool hover = _mouse is { } hm && button.Contains(hm);
            if (hover) d.FillRect(button, Theme.SelectedFill);
            d.Rect(button, hover ? Theme.SelectedFrame : Theme.Frame, 1f);
            d.Text(label, new Vector2(button.X + 14, button.Y + 6), 17, Theme.Text);
            _growthButton = button;
            y = button.Y + button.Height;
            // クラスの説明は、欄に収まる大きさで(英語は長い)
            string desc = Services.Name(cls.Description);
            float room = area.Y + area.Height - 12 - (y + 6), size = 17;
            while (size > 12 && d.WrapLines(desc, area.Width - 40, size).Count * (size + 8) > room) size--;
            Line(d, area, y + 6, desc, Theme.Dim, size);
            return;
        }
        _growthButton = null;
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
    private readonly MemberSwitch _switch;
    private int _page;

    /// <summary>ページの見出しのクリックできる場所(◀ は -1、▶ は -2、ページの名前はその番号)。Draw で決める。</summary>
    private readonly List<(RectangleF Area, int Page)> _pageHits = [];

    /// <param name="members">切り替えられる仲間の並び(L2/R2・&lt; &gt;・F1〜F6・◀ ▶)。省くとその人だけ。</param>
    public StatusScene(Character c, IReadOnlyList<Character>? members = null) => _switch = new MemberSwitch(members ?? [c], c);

    private Character Member => _switch.Current;

    /// <summary>右側のページ(呪文のページは術者だけ)。</summary>
    private string[] Pages => Spellcasting.Castable(Services.Data, Member).Any() ? ["skills", "spells", "equipment", "bag"] : ["skills", "equipment", "bag"];

    public override void Update(GameTime time, Input.InputState input)
    {
        string shown = Pages[Math.Clamp(_page, 0, Pages.Length - 1)];
        if (_switch.Update(input, Services.Audio))
        {
            _page = Math.Max(0, Array.IndexOf(Pages, shown));   // 同じページのまま次の人へ(術者でない人に呪文のページは無いので、そのときは最初のページ)
            return;
        }
        int step = input.Pressed(Input.GameAction.Right) ? 1 : input.Pressed(Input.GameAction.Left) ? -1 : 0;
        // ページの見出しをクリック: ◀ ▶ は前と次、名前はそのページ(ほかの所のクリックは、今までどおり閉じる)
        if (input.LeftClicked && input.Mouse is { } m && _pageHits.FirstOrDefault(h => h.Area.Contains(m)) is { Area.Width: > 0 } hit)
        {
            if (hit.Page >= 0)
            {
                if (hit.Page != _page) Services.Audio.PlaySe(Audio.Sounds.Cursor);
                _page = hit.Page;
                return;
            }
            step = hit.Page == -1 ? -1 : 1;
        }
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

    /// <summary>
    /// 短い要約(名前・種族とクラス・レベル・HP・AC・能力値)。能力値は装備などを含めた今の値。detail(状態の画面)では
    /// 「能力値 現在値(ベース)」の見出しで3つずつ2行にし、変わったものだけ色を付けてカッコにベースを出す。
    /// </summary>
    /// <param name="width">欄の幅(長い行は折り返す。英語のヒットダイスの行など)。</param>
    public static float DrawSummary(Draw2D d, GameServices sv, Character c, Vector2 at, bool detail = false, float width = 540f)
    {
        var s = sv.Strings;
        var data = sv.Data;
        float y = at.Y;
        void Text(string t, Color color, float size = 20f) => y = LinkText.Draw(d, t, new Vector2(at.X, y), width, size, color, 9);   // 決まりの言葉(AC・習熟など)に説明
        var race = data.Races[c.RaceId];
        Text(c.Name, Theme.Title, 26);
        // マルチクラスは最初のクラスの名前に (M) を付ける(Lv は合計。クラスごとのレベルは下に出す)
        string multi = c.ClassIds.Distinct().Count() > 1 ? "(M)" : "";
        Text($"{sv.Name(race.Subrace ?? race.Name)}  {sv.Name(data.Classes[c.ClassId].Name)}{multi}  Lv{c.Level}  {s[$"alignment.{c.Alignment.ToString().ToLowerInvariant()}"]}", Theme.Text);
        Text(s.Format("status.hp_ac", c.Hp, c.MaxHp, c.ArmorClass(data), c.ProficiencyBonus), Theme.Text);
        Text(s.Format("status.hit_dice", c.HitDice, c.Level, data.Classes[c.ClassId].HitDie), c.HitDice == 0 ? Theme.Warn : Theme.Dim, 18);
        var now = c.Stats(data);
        if (!detail) Text(string.Join("  ", Abilities.All.Select(a => $"{s[$"ability_short.{a.Code()}"]} {now[a]}")), Theme.Text, 18);
        else
        {
            Text(s["status.abilities_header"], Theme.Dim, 16);
            foreach (var three in Abilities.All.Chunk(3))
            {
                float x = at.X;
                foreach (var a in three)
                {
                    bool changed = now[a] != c.Scores[a];
                    string label = $"{s[$"ability.{a.Code()}"]} ";
                    d.Text(label, new Vector2(x, y), 18, Theme.Text);
                    x += d.Fonts.Measure(label, 18).X;
                    string value = changed ? $"{now[a]}({c.Scores[a]})" : $"{now[a]}";
                    d.Text(value, new Vector2(x, y), 18, changed ? Theme.Good : Theme.Text);
                    x += d.Fonts.Measure(value, 18).X + 18;
                }
                y += 18 + 9;
            }
        }
        Text(s.Format("status.xp", c.Xp, Progression.XpForLevel(c.Level + 1)), Theme.Dim, 18);
        Text(s.Format("status.gold", c.Gold), Theme.Gold, 18);
        if (!c.ArmorProficient(data)) Text(s["status.armor_unproficient"], Theme.Warn, 18);   // 呪文を唱えられず、攻撃が不利
        // 習熟(鎧と盾は、無いと着られない。武器は持てるが、習熟ボーナスが付かない)。武器は長いので、欄の幅で折り返す
        var (armor, weapons) = Proficiencies(sv, c);
        Text(s.Format("status.armor_prof", armor), Theme.Text, 18);
        y = d.TextWrapped(s.Format("status.weapon_prof", weapons), new Vector2(at.X, y), width, 18, Theme.Text, 9);
        if (UI.SpellText.SlotLines(s, data, c) is { } slots)
            foreach (var line in slots) Text(line, Theme.Text, 18);
        // マルチクラスなら、クラスごとのレベル(上の行の Lv は合計。取った順に1行ずつ)
        var levels = c.ClassIds.Distinct().ToList();
        if (levels.Count > 1)
            foreach (var id in levels)
                Text($"{sv.Name(data.Classes[id].Name)}  Lv{c.LevelIn(id)}", Theme.Text, 18);
        return y;
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        var data = Services.Data;
        var area = new RectangleF(80, 60, 1120, 600);
        Theme.Panel(d, area);
        float y = DrawSummary(d, Services, Member, new Vector2(area.X + 30, area.Y + 24), detail: true, width: 525f);
        // 左の欄の右寄りに、いつも同じ場所に(名前の長さで動かない)。名前が長くて届くときだけ、名前のすぐ右に
        _switch.Draw(d, new Vector2(Math.Max(area.X + 440, area.X + 30 + d.Fonts.Measure(Member.Name, 26).X + 24), area.Y + 28));

        // 右側: 技能・セーヴ・クラスの能力か、呪文(術者は左右で切り替える)
        float x2 = area.X + 600;
        float y2 = area.Y + 24;
        void Text2(string t, Color color, float size = 18f)
        {
            d.Text(t, new Vector2(x2, y2), size, color);
            y2 += size + 8;
        }
        var castable = Spellcasting.Castable(data, Member).ToList();
        var pages = Pages;
        _page = Math.Clamp(_page, 0, pages.Length - 1);
        string page = pages[_page];
        // ページの見出し(今のページを明るく)
        // ◀・名前・▶ はクリックでも切り替える(マウスを載せると明るく)
        _pageHits.Clear();
        float hx = x2;
        bool Over(RectangleF r) => UI.Tooltip.Over(r);
        var prev = new RectangleF(hx - 6, y2 - 4, 26, 28);
        _pageHits.Add((prev, -1));
        d.Text("◀", new Vector2(hx, y2 + 2), 16, Over(prev) ? Theme.Title : Theme.Dim);
        hx += 24;
        for (int i = 0; i < pages.Length; i++)
        {
            if (i > 0)
            {
                d.Text("/", new Vector2(hx, y2), 18, Theme.Dim);
                hx += 16;
            }
            string name = s["status.page_" + pages[i]];
            float nw = d.Fonts.Measure(name, 18).X;
            var r = new RectangleF(hx - 4, y2 - 4, nw + 8, 28);
            _pageHits.Add((r, i));
            d.Text(name, new Vector2(hx, y2), 18, i == _page ? Theme.Title : Over(r) ? Theme.Text : Theme.Dim);
            hx += nw + 10;
        }
        var next = new RectangleF(hx - 6, y2 - 4, 26, 28);
        _pageHits.Add((next, -2));
        d.Text("▶", new Vector2(hx, y2 + 2), 16, Over(next) ? Theme.Title : Theme.Dim);
        y2 += 34;
        if (page == "equipment")
        {
            // 付けているものだけを出す(名前は見える姿で。未鑑定や、呪いがまだ分からない品の正体は出さない)
            Text2(s["status.equipment"], Theme.Title, 22);
            foreach (var slot in Enum.GetValues<EquipSlot>())
            {
                if (Member.Inventory[slot] is not { } it) continue;
                y2 = d.TextWrapped($"{s[$"slot.{slot.ToString().ToLowerInvariant()}"]}: {UI.ItemText.Name(Services, it)}", new Vector2(x2, y2),
                    area.X + area.Width - x2 - 24, 17, Theme.Text, 7);
            }
        }
        else if (page == "bag")
        {
            // 1つずつの行で2列に(一番大きな袋の 30 個まで収まる)。長い名前は欄に収まるまで字を小さくする
            Text2(s.Format("status.bag", Member.Inventory.Items.Count, Member.Inventory.Capacity(data.Items)), Theme.Title, 22);
            var items = Member.Inventory.Items;
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
            foreach (var cls in Spellcasting.CasterClasses(data, Member))
                Text2((Spellcasting.CasterClasses(data, Member).Count() > 1 ? Services.Name(cls.Name) + " " : "")
                    + s.Format("status.spells", Spellcasting.SaveDc(data, cls, Member), Signed(Spellcasting.AttackBonus(data, cls, Member))), Theme.Title, 22);
            foreach (var group in castable.GroupBy(x => Spellcasting.AssignedLevel(data, Member, x)))   // クラスで割り当てたレベルごと
            {
                // 呪文の名前は長いので、欄の幅で折り返す(名前の上にマウスを載せると、呪文の説明)
                var words = group.Select(x => new UI.LinkWord(Services.Name(x.Name), null, SpellAbout(x))).ToList();
                y2 = UI.LinkFlow.Draw(d, words, s["common.list_sep"], new Vector2(x2, y2), area.X + area.Width - x2 - 24, 16, Theme.Text,
                    lead: $"{UI.SpellText.Level(s, group.First(), group.Key)}: ");
            }
        }
        else
        {
        Text2(s["status.skills"], Theme.Title, 22);
        // 技能の名前の上にマウスを載せると、技能の説明
        foreach (var id in Member.Skills)
        {
            var skill = data.Skills[id];
            float w = UI.LinkFlow.Word(d, new UI.LinkWord(Services.Name(skill.Name), null, skill.Description is { } sd ? Services.Name(sd) : null), new Vector2(x2, y2), 18, Theme.Text);
            d.Text($"  {Signed(Member.SkillBonus(data, id))}", new Vector2(x2 + w, y2), 18, Theme.Text);
            y2 += 26;
        }
        y2 += 8;
        Text2(s["status.saves"], Theme.Title, 22);
        // 6つ並ぶと欄の幅を越えるので、3つずつ2行に(体の能力値と、心の能力値)
        foreach (var three in Abilities.All.Chunk(3))
            Text2(string.Join("  ", three.Select(a => $"{s[$"ability.{a.Code()}"]} {Signed(Member.SaveBonus(data, a))}")), Theme.Text);
        // クラスの能力(マルチクラスでは全部のクラスのもの): 回数の残りと、勝手に効く能力の名前
        var pools = ClassFeatures.PoolsOf(Services.Data, Member).ToList();
        var passives = ClassFeatures.Passives(data, Member).Select(id => UI.FeatureText.Link(Services, id)).ToList();
        var styles = ClassFeatures.ActiveStyles(data, Member);
        bool hasStyle = ClassFeatures.HasFightingStyle(data, Member);
        if (pools.Count > 0 || passives.Count > 0 || hasStyle)
        {
            y2 += 8;
            Text2(s["status.features"], Theme.Title, 22);
            // ファイティングスタイル: 今働くスタイルとその効果(無ければ「なし」)、持つ武器で変わること
            if (hasStyle)
            {
                string now = styles.Count == 0 ? s["style.none"]
                    : string.Join(" / ", styles.Select(id => s.Format("status.style_item", UI.FeatureText.Name(Services, id), UI.FeatureText.About(Services, id) ?? "")));
                y2 = d.TextWrapped(s.Format("status.fighting_style", now), new Vector2(x2, y2), area.X + area.Width - x2 - 24, 17, Theme.Text, 4);
                y2 = d.TextWrapped(s["status.style_auto"], new Vector2(x2, y2 + 2), area.X + area.Width - x2 - 24, 15, Theme.Dim, 4) + 4;
            }
            // 回数は1つずつ行を分ける(折り返しで「5/5」が切れないように)
            // 回数の名前と勝手に効く能力の名前の上にマウスを載せると、その説明
            foreach (var p in pools)
            {
                float w = UI.LinkFlow.Word(d, new UI.LinkWord(UI.FeatureText.PoolName(Services, p), null, UI.FeatureText.PoolAbout(Services, Member, p)), new Vector2(x2, y2), 17, Theme.Text);
                d.Text($"  {ClassFeatures.UsesLeft(Services.Data, Member, p)}/{ClassFeatures.MaxUses(Services.Data, Member, p)}", new Vector2(x2 + w, y2), 17, Theme.Text);
                y2 += 25;
            }
            if (passives.Count > 0)
                y2 = UI.LinkFlow.Draw(d, passives, s["common.list_sep"], new Vector2(x2, y2), area.X + area.Width - x2 - 24, 16, Theme.Dim);
        }
        }

        d.Text(s[_switch.CanSwitch ? "status.close_pages_members" : "status.close_pages"], new Vector2(640, 690), 15, Theme.Dim, TextAlign.Center);
    }

    /// <summary>鎧と武器の習熟の一覧(クラスを合わせて。分類は名前で、決まった武器は品の名前で)。</summary>
    public static (string Armor, string Weapons) Proficiencies(GameServices sv, Character c) =>
        Proficiencies(sv, c.ClassIds.Select(id => sv.Data.Classes[id]).ToList());

    /// <summary>そのクラスを合わせた鎧と武器の習熟(キャラ作成では、選んでいるクラスの最初の習熟)。</summary>
    public static (string Armor, string Weapons) Proficiencies(GameServices sv, IReadOnlyList<ClassDef> classes)
    {
        var s = sv.Strings;
        var armor = new[] { "light", "medium", "heavy", "shield" }.Where(a => classes.Any(k => k.Armor.Contains(a)))
            .Select(a => a == "shield" ? s["status.prof_shield"] : s["armor." + a]).ToList();
        var weapons = classes.SelectMany(k => k.Weapons).Distinct()
            .OrderBy(w => w switch { "simple" => 0, "martial" => 1, _ => 2 })
            .Select(w => w is "simple" or "martial" ? s["weapon." + w] : sv.Data.Items.TryGetValue(w, out var def) ? sv.Name(def.Name) : w).ToList();
        string none = s["status.prof_none"];
        return (armor.Count == 0 ? none : s.List(armor), weapons.Count == 0 ? none : s.List(weapons));
    }

    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();

    /// <summary>呪文の説明の窓の中身(書いた説明があればそれと、レベルと系統などの見出しと、効果)。</summary>
    private string SpellAbout(SpellDef sp)
    {
        var s = Services.Strings;
        // 初級呪文はその人の合計のレベルの強さ(ダイスや光線の数)で、ほかは元のレベルのスロットで唱えたときの強さ
        var lines = UI.SpellText.Effects(s, sp, sp.Scale == "character" ? Member.Level : sp.Level).Prepend(UI.SpellText.Header(s, sp, Spellcasting.AssignedLevel(Services.Data, Member, sp)));
        if (sp.Description is { } desc) lines = lines.Prepend(Services.Name(desc));   // 書いた説明があれば先に
        return string.Join("\n", lines);
    }
}

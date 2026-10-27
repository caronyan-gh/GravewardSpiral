using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// タブごとの説明(右上の ? ボタンで吹き出しに出す)。画面にはくどい説明を置かず、ここにまとめる。
/// 文は、見出し(「■」で始まる行)と本文の行。
/// </summary>
public static class Help
{
    public static string For(string tab) => tab switch
    {
        "basic" => T("""
            ■ シナリオ
            名前・説明(英日)・作者・版は、ゲームのシナリオを選ぶ画面に出る。「クランを連れて入れる」を外すと、新しいクランでしか始められない。
            ■ レベル帯
            B1 と最下層の目安のレベル。階ごとの敵の強さと宝はここから決まる(敵と宝のタブで見られる)。強いクランで入ると、帯はクランに合わせて上がる。
            ■ 中身を自動で決める
            出る敵・名のある敵・散らすイベントを、ゲームの新しい世界と同じ選び方で選んで、シナリオに書き込む(先に保存する)。書き込んだものは、誰が何度遊んでも同じ。
            ・決めていないところを埋める: まだ決めていない所だけ。
            ・選び直す: 決めてあるものも選び直す。
            ・散らしたものを消す: 散らしたイベントの印を消す(自分で置いたイベントは残る)。
            ・標準のパックのイベントも使う: 外すと、このシナリオのイベントだけを散らす。
            決めていないものは、遊ぶたびに選ぶ。
            """, """
            ■ Scenario
            Name, description (English/Japanese), author and version appear on the game's scenario screen. Unchecking "Allow bringing a clan in" means it can only be started with a new clan.
            ■ Level range
            Target levels at B1 and at the bottom floor. Monster strength and treasure per floor follow from these (see Enemies & loot). A strong clan raises the range to match.
            ■ Fill in contents automatically
            Picks monsters, named monsters and scattered events the same way a new game does, and writes them into the scenario (saves first). What is written is the same for everyone, every time.
            ・Fill undecided: only what isn't decided yet.
            ・Re-pick: re-picks what is decided too.
            ・Remove scattered: removes scattered event marks (events you placed yourself stay).
            ・Also use the standard pack's events: uncheck to scatter only this scenario's events.
            Anything undecided is picked on each new game.
            """),
            "maze" => T("""
            ■ 選ぶ
            地図のマスをクリックすると選ぶ。押したまま動かすと四角く選ぶ(右の変更は、選んだマス全部に当たる)。
            ■ このマスにあるもの
            1マスに1つ: なし・階段・回転床・テレポーター・落とし穴・シュート・エレベーター・岩盤・イベント・ボス・鍵・文言・門・着地点。選ぶと、それに合う欄(どのイベント、行き先など)だけが下に出る。
            ・イベント: このシナリオのイベントと、迷宮に置いてある標準のもの。ほかの標準のイベントは「標準のイベントから選ぶ」。自分のイベントはイベントのタブで作る。
            ・ボス: 1体に1マス。ほかのマスに置くと動く。ボスは物語のタブで足す。
            ・文言: 文言のタブの文を出す(張り紙など)。
            ・門: イベントの手順「門を開ける」で開く扉。「門を閉める」でまた閉まる。
            ・着地点: イベントの手順「別のマスへ移す」で、パーティを移す先(ほかの階からも)。
            ・エレベーター: 縦穴を1本通す。全部の階で同じ場所で、下の欄の「通す階」の範囲のうち、このマスが空いている階に自動で止まる(岩や壁、ほかの仕掛けのある階は素通りで、地図に薄い e が出る)。ほかの階に置き直す必要はない。「なし」にすると縦穴ごと消える。「スイッチを入れるまで動かない」にすると、フラグが立つまで動かない。標準のイベント「エレベーターのスイッチ」を、どこかのマス(下の階がおすすめ)に置く。
            ■ 見え方と魔法禁止
            見え方は、ふつう・暗闇(前が見えず、地図は触れた壁だけ)・霧(見えるが地図に残らない)のどれか。魔法禁止は呪文が唱えられない(見え方と重ねられる)。
            ■ 壁と扉
            地図の右の縦の道具で塗る。選ぶ(矢印)以外を押すと、クリックが一番近いマスの縁に当たり、その種類にする(押したまま続けて)。右クリックで壁や扉を消す(通路にする)。一方通行は、同じ縁をもう一度クリックで向きが逆になる。Esc で選ぶに戻る。
            右の「四方の壁を消す」「四方を壁にする」で、選んだマスをまとめて変えられる。岩盤(黒いマス)の縁は壁から変えられないので、先に岩盤を消す。
            ■ 部屋と着く場所
            部屋: 四角く選んで「この四角を部屋にする」。部屋の中のマスを選ぶと消せる。部屋は、名のある敵のねぐらやはめ込む部屋の目安。
            着く場所: 上り階段から来たとき(と、この階から始めるとき)に立つマスと向き(赤い矢印)。
            ■ 地図の印
            上・下 階段、回 回転床、T テレポーター、穴 落とし穴、落 シュート、E エレベーター(薄い e は縦穴が素通りする階)、★ イベント、主 ボス、鍵、文 文言、門、着 着地点、名 名のある敵のねぐら。黒は岩盤、薄い黄色は部屋。
            ■ この階
            地図の上: 名前(空なら物語のタブの名前)、大きさ(左上をそろえて右と下を足すか削る)、端のつながり。左: 階を足す・消す・並べ替え・自動で作り直す。
            """, """
            ■ Selecting
            Click a cell to select it. Drag to select a rectangle (changes on the right apply to every selected cell).
            ■ What is here
            One per cell: nothing, stairs, spinner, teleporter, pit, chute, elevator, solid rock, event, boss, key, text or gate. Picking one shows only the fields it needs (which event, destination, etc.).
            ・Elevator: makes one shaft. It is at the same place on every floor and stops automatically on floors in the "Floors" range below where this cell is open (it passes through rock, walls and other features, shown as a faint e). No need to place it on other floors. Setting "Nothing" removes the whole shaft. "Doesn't run until a switch is turned on" keeps it still until the flag is set; place the standard event "Elevator switch" on a cell somewhere (a lower floor works well).
            ・Event: this scenario's events and standard ones already placed. Other standard events: "Pick a standard event". Make your own on the Events tab.
            ・Boss: one cell per boss; placing it elsewhere moves it. Add bosses on the Story tab.
            ・Text: shows text from the Text tab (notices, etc.).
            ・Gate: a door opened by the event step "Open gate"; "Close gate" locks it again.
            ・Landing point: where the event step "Move the party" sends the party (also from other floors).
            ■ Visibility and anti-magic
            Visibility is normal, darkness (can't see ahead; the map keeps only touched walls) or fog (visible, but not recorded on the map). Anti-magic blocks spells (can be combined).
            ■ Walls and doors
            Use the vertical tools right of the map. Any tool but Select (arrow) makes a click hit the nearest cell edge and set it to that kind (drag to continue). Right-click removes walls and doors (open). Click a one-way edge again to flip it. Esc goes back to Select.
            "Open all sides" and "Wall all sides" on the right change selected cells at once. Edges next to solid rock (black) stay walls, so clear the rock first.
            ■ Rooms and arrival
            Room: select a rectangle and "Make this rectangle a room"; select a cell inside a room to remove it. Rooms guide named monster lairs and prefab rooms.
            Arrival: the cell and facing when coming down the stairs (or starting on this floor), shown as a red arrow.
            ■ Map marks
            U/D stairs, S spinner, T teleporter, P pit, C chute, E elevator, ★ event, B boss, K key, M text, G gate, L landing point, N named monster lair. Black is solid rock, light yellow is a room.
            ■ This floor
            Above the map: name (empty uses the Story tab's name), size (keeps the top-left; adds or trims right and bottom), wrapping. Left: add, delete, reorder and regenerate floors.
            """),
            "advice" => T("""
            ■ 出る敵
            決めない: 遊ぶたびに、階のテーマ(種類の多さ)でその階に合う敵から 14 種を選ぶ。「出るかもしれない敵」を開くと、合う敵が種類ごとに並ぶ。
            決める: この階には、並べた敵だけが出る(誰が何度遊んでも同じ)。札の × で外し、下の欄から足す(★ はこの階に合う敵。種類と脅威度で絞り込める)。「テーマから選び直す」で選び直す。
            「種類から何種」の行を足すと、その種類から遊ぶたびに選ぶ(決めた敵と混ぜられる)。
            迷宮での戦闘は、顔ぶれの中から、その戦闘の強さに合う敵を選ぶので、出る割合は均等ではない。
            ■ 名のある敵
            名前の付いた強い敵。ねぐら(地図に「名」)にいるか、階をさまよう。噂に乗ると、酒場で聞ける。決めていなければ、遊ぶたびに 0〜2 体を抽選する。
            ■ 宝
            その深さで出る宝の等級の割合(レベル帯で決まる)。
            """, """
            ■ Monsters
            Undecided: each game picks 14 monsters that fit the floor, weighted by the floor theme. Open "Monsters that may appear" to see them by kind.
            Decide: only the listed monsters appear (the same in every game). Remove with ×, add from the field below (★ fits this floor; filter by kind and CR). "Re-pick from the theme" picks again.
            A "pick N from a kind" row picks from that kind each game (can mix with listed monsters).
            In the maze, each fight picks from the roster to fit its difficulty, so the shares are not equal.
            ■ Named monsters
            Strong named monsters, in a lair (N on the map) or wandering the floor. Rumored ones can be heard about in the tavern. If undecided, 0-2 are drawn each game.
            ■ Treasure
            Treasure rarity shares at this depth (set by the level range).
            """),
            "events" => T("""
            ■ 一覧
            このシナリオのイベント。言葉・場所・置き方・中身の種類・レア度で絞り込める。薄い札は、迷宮に置いてある標準のパックのイベント(選ぶと中身を見て、このシナリオに写して直せる。同じ id で写すので、迷宮の印はそのまま)。
            「標準のイベントから写す」で、標準のパックのイベントを名前と中身を見て写す。
            ■ 中身
            真ん中の木が手順と条件。上で組と部品を選んで「手順を足す」「条件を足す」、選択肢は「選択肢を足す」。右で選んだ部品の項目を直す。JSON でも直せる(「JSON を当てる」)。
            ■ 置き方
            「迷宮に散らす」を付けると、合う深さの空いたマスに置く(基本のタブの「散らして置く」で)。付けないイベントは、迷宮のタブで「このマスにあるもの」をイベントにして置く。町のイベントは「起こる場所」を町にすると、町の「人を訪ねる」に題で出る。
            """, """
            ■ List
            This scenario's events, filterable by words, place, placing, content kind and rarity. Faint entries are standard pack events placed in the maze (select one to view it and copy it into this scenario to edit; it keeps the same id, so the maze marks stay).
            "Copy a standard event" copies one from the standard pack after viewing its name and content.
            ■ Contents
            The tree in the middle holds steps and conditions. Pick a group and part above and press "Add step" / "Add condition"; "Add choice" for choices. Edit the selected part on the right, or edit the JSON ("Apply JSON").
            ■ Placing
            "Scatter in the maze" places it in free cells at a fitting depth (via "Scatter now" on the Basic tab). Otherwise place it on the Maze tab by setting a cell's "What is here" to the event. Town events (place: town) appear under "Visit" in town by their title.
            """),
            "data" => T("""
            ■ データ
            このシナリオの品・モンスター・呪文。「写す元を選ぶ」で標準のものを名前と絞り込み(品は種類と等級、モンスターは種類と脅威度、呪文はレベルと系統)で選んで「写して一部を変える」(extends。変えた所だけ持つ)か「全部写して作る」。名前の欄と、全体の JSON で直す。
            ・モンスターの絵は art/monsters/<id>.png(無ければ元の絵)。
            ・呪文は名前だけ変えられる。
            ・JSON の下の「JSON を確かめる」は、当てる前にゲームと同じ検査をする(書き間違えた項目名、無い呪文や元の品、ダイスの書き方など。保存はしない)。「項目の一覧…」は、書ける項目と説明・例の表(ダブルクリックで JSON に足して、値を選んだ形にする。効果などの中の部品の項目は、カーソルの位置に書き込む)。
            ・イベントでだけ渡す品は「おまかせでは出さない」に印を付ける(宝の抽選・商店に出ず、イベントの「品を渡す」や落とし物の表で名指ししたときだけ手に入る)。
            ・オリジナルの品は、シナリオを消しても持ち出した品が残るよう、セーブに中身ごと入る。
            """, """
            ■ Data
            This scenario's items, monsters and spells. "Choose the original…" picks a standard one by name and filters (items: kind and rarity; monsters: kind and CR; spells: level and school), then "Copy and change parts" (extends; keeps only what changes) or "Copy everything". Edit the name fields and the whole JSON.
            ・Monster art is art/monsters/<id>.png (falls back to the base art).
            ・Spells can only be renamed.
            ・"Check JSON" below the JSON runs the game's checks before applying (misspelled fields, missing spells or base items, dice syntax, etc.; nothing is saved). "Field list…" shows the fields you can write, with descriptions and examples (double-click adds the field to the JSON with its value selected; fields of inner parts such as effects go in at the cursor).
            ・For items given only by events, tick "Never at random" (not in treasure rolls or shops; only obtained when an event's give item or a drop table names it).
            ・Original items are saved with their full data, so items taken out survive even if the scenario is deleted.
            """),
            "music" => T("""
            ■ 曲
            標準の曲(タイトル・町・迷宮・戦闘・ボス戦・物語の場面・エンディング)を、ファイルを選んで差し替える。階ごとの曲(迷宮と、ふつうの戦闘)も置ける(無い階は上の曲)。
            ほかの名前の曲は、イベントの「曲」で選んでイベントのあいだ流す。mp3 をシナリオの audio/bgm に写す。聞いて確かめられる。
            行ごとの「棚」では、ゲームに入っている標準の曲の棚(第1部・第2部の曲など)から選べる。ファイルは写さずに割り当て、割り当てはファイルより先に使う(空にすると外す)。下の「棚の曲をイベントの曲に足す」で、棚の曲をイベントの曲にもできる。
            """, """
            ■ Music
            Replace the standard tracks (title, town, maze, battle, boss, story scenes, ending) by choosing files. Per-floor tracks (maze and normal battles) are possible too (floors without one use the track above).
            Other tracks can be picked as an event's "Music" and play during that event. MP3s are copied into the scenario's audio/bgm. You can listen to check them.
            The "Shelf" box on each row picks from the standard tracks shipped with the game (Part 1, Part 2 and so on). It assigns the track without copying, and an assignment wins over a file (clear it to remove). "Add a shelf track as an event track" below makes a shelf track usable as an event track.
            """),
            "text" => T("""
            ■ 文言
            キーと文(日本語・英語)。迷宮の「文言」のマスは、ここの文を出す。標準のパックと同じキーを書くと、このシナリオのあいだだけその文が変わる。
            「足りないキーを足す」で、迷宮の文言のマスが使っていて、まだ無いキーを足す。
            """, """
            ■ Text
            Keys and text (Japanese / English). A maze "Text" cell shows the text written here. Using a standard pack key changes that text for this scenario only.
            "Add missing keys" adds keys that maze text cells use but don't exist yet.
            """),
            "story" => T("""
            ■ 序文
            新しいゲームの始めと、町の「この迷宮のいわれ」で出る。長ければ、ゲームの画面でスクロールする。
            ■ 最下層の主と、足したボス
            「迷宮に置く」で、その階の着く場所から一番遠い空いたマスに置く(迷宮のタブで動かせる)。名前・出会ったとき・倒したとき・敵の組(最初の行が主。2行目からは従う者で、名前を付けられる)。足したボスは HP の倍率と、倒したら立てるフラグも。
            ■ 酒場の噂
            「噂を聞く」で、聞くたびに1つ出る(条件に合う噂と、名のある敵・伝説の噂から重みで選ぶ)。条件で、物語の進み具合に合わせて出し分ける。文はかぎ括弧なしで書く。
            ■ 階ごとの文
            階の名前、入ったときの文、書き付け(イベントの手順「AI の書き付け」で出る)。
            ■ AI に書かせる
            設定のモデルと API キーで、空の欄を AI に書かせる(「書いてある欄も書き直す」で全部)。お金がかかる。捨てた欄(長すぎるなど)は知らせに出る。
            """, """
            ■ Prologue
            Shown at the start of a new game and under "The tale of this dungeon" in town. Long text scrolls in the game.
            ■ Final boss and added bosses
            "Place in the maze" puts it on the free cell farthest from the floor's arrival (move it on the Maze tab). Name, meeting and defeat lines, enemy groups (the first row is the boss; later rows are named attendants). Added bosses also have an HP multiplier and a flag set on defeat.
            ■ Tavern rumors
            Each "Hear rumors" tells one (picked by weight from matching rumors, named monsters and legends). Use conditions to change them as the story goes on. Write them without quotation marks.
            ■ Per-floor text
            Floor names, entry lines and notes (shown by the event step "AI-written note").
            ■ Write with AI
            Uses the model and API key from Settings to fill empty fields ("Overwrite filled fields too" for all). It costs money. Dropped fields (too long, etc.) are reported.
            """),
            "check" => T("""
            ■ 検査
            保存してから、読み込みの検査・迷宮の辻褄・町から行けるか・ボットが主まで歩けるか・イベントの検査・印が指すイベント・文言のキー・曲と絵のファイルを確かめる。
            中身の整合性も見る: ボスの印とボス(無いボス、1体に印が2つ、置いていないボス)、名のある敵(無い敵・無い階・ねぐらが岩盤)、錠のある階の鍵、テレポーターとシュートの行き先、エレベーターの組、門と「門を開ける」、連動するイベントの組の置き忘れ、知らない印。
            ■ 注意(！)
            わざとかもしれないものは、問題ではなく注意で出す(そのままでも検査は通る): ボスが何か所にもいる・迷宮にいない、最下層の主が最下層以外にいる・いない、使われていないイベント(散らさず、置かず、ほかのイベントからも続かない)、散らすのに置かれていないイベント。
            ■ ボットの通し
            「ボットに通しで遊ばせる」は、選んだ編成のボットに最初から最後まで遊ばせて、何回クリアできるか、どこで詰まるかを見る(何分かかかる)。編成を替えると、術者だけや3人でも試せる。
            ■ 書き出し
            zip に書き出すと、ほかの人に渡せる(シナリオの置き場所に置くか、ゲームのタイトル画面に落とすと、ゲームで選べる)。
            """, """
            ■ Check
            Saves, then checks loading, maze consistency, reachability from town, whether a bot can walk to the boss, events, marks pointing at events, text keys, and music and art files.
            It also checks content consistency: boss marks and bosses (missing bosses, two marks for one boss, unplaced bosses), named monsters (missing monster or floor, lair on solid rock), keys on floors with locked doors, teleporter and chute destinations, elevator groups, gates and "Open gate" steps, partly placed linked event sets, and unknown marks.
            ■ Cautions (!)
            Things that may be intended are shown as cautions, not problems (the check still passes): a boss in several places or not in the maze, the final boss off the bottom floor or missing, unused events (not scattered, not placed, not run by other events), and scattered events not placed.
            ■ Bot play-through
            "Play through with bots" has a bot party of the chosen makeup play from start to finish, to see how often it clears and where it gets stuck (takes a few minutes). Change the party to try casters only or a party of 3.
            ■ Export
            Export to zip to share it (put it in Documents\GravewardSpiral\Scenarios to pick it in the game).
            """),
        _ => "",
    };

    /// <summary>? ボタン(押すと、今のタブの説明を吹き出しで出す)。tab は押したときのタブの key。</summary>
    public static Button Button(Func<string> tab)
    {
        var b = new Button
        {
            Content = "?", Width = 26, Height = 26, Padding = new Thickness(0), FontWeight = FontWeights.Bold, ToolTip = T("このタブの説明", "Help for this tab"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0),
        };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush(Theme.Palette.Text) };
        void Fill()
        {
            text.Inlines.Clear();
            foreach (var line in For(tab()).Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.StartsWith('■')) text.Inlines.Add(new System.Windows.Documents.Run((text.Inlines.Count > 0 ? "\n" : "") + l.TrimStart('■', ' ') + "\n") { FontWeight = FontWeights.SemiBold, FontSize = 14 });
                else text.Inlines.Add(new System.Windows.Documents.Run(l + "\n"));
            }
        }
        var popup = new Popup
        {
            PlacementTarget = b, Placement = PlacementMode.Bottom, HorizontalOffset = -480, StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Background = Theme.Brush(Theme.Palette.Surface), BorderBrush = Theme.Brush(Theme.Palette.BorderStrong), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Width = 500, MaxHeight = 560, Margin = new Thickness(0, 0, 8, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.18 },
                Child = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            },
        };
        b.Click += (_, _) =>
        {
            if (!popup.IsOpen) Fill();
            popup.IsOpen = !popup.IsOpen;
        };
        return b;
    }
}

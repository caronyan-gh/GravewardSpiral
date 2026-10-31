# scenario-kit(シナリオを作る人とエージェントのための手引き)

GRAVEWARD SPIRAL のシナリオは、データ(JSON と迷宮のテキスト)だけで作れる。ソースを読まなくても作れるように、
書き方・すき間の説明・確かめる道具をここにまとめた。人が読んでも、エージェント(Claude Code・Codex など)に渡してもよい。

エージェントに頼むときは、「インストール先の scenario-kit/README.md を読んでから作って」と伝えれば足りる。

## まず守ること

- **本物のセーブとシナリオを触らない**。セーブと設定は `%LOCALAPPDATA%\GravewardSpiral`、置いたシナリオは `%LOCALAPPDATA%\GravewardSpiral\Scenarios` にある。
  - ゲームを道具から起こすときは、必ず `--saves <使い捨てのフォルダ>` を付ける(付けないと本物のセーブを読み書きする)。
  - エディタを道具から起こすときは、必ず `--scenarios <使い捨てのフォルダ>` を付け、開くのは写しにする(エディタの `--screenshot` も保存する)。エディタの使い方は `GravewardSpiralEditor.exe --help`(窓を開かずに出す)。
  - exe は PATH に入っていない。インストール先のフルパスで呼ぶ(既定のインストール先は `%LOCALAPPDATA%\Programs\GravewardSpiral`。この手引きのある `scenario-kit` の1つ上のフォルダ)。そのまま写して使える起こし方(PowerShell):

    ```text
    $gs = "$env:LOCALAPPDATA\Programs\GravewardSpiral"
    & "$gs\ScenarioCheck.exe" --help
    & "$gs\ScenarioCheck.exe" new --help
    & "$gs\GravewardSpiralEditor.exe" --help
    & "$gs\GravewardSpiral.exe" --help
    & "$gs\GravewardSpiral.exe" --saves "<作業フォルダ>\saves" --start "scenario:<シナリオのフォルダ>:1" --screenshot "<作業フォルダ>\b1.png" --frames 120
    & "$gs\GravewardSpiralEditor.exe" --scenarios "<作業フォルダ>\editor" --open "<シナリオの写し>" --tab check --screenshot "<作業フォルダ>\check.png"
    ```

  - 道具のコマンドごとの説明は `ScenarioCheck <コマンド> --help`(例: `new --help`)。

  - パスは Windows の形(`C:\...`)で渡す。Git Bash の `/tmp/...` や `/c/...` の形は、ゲームとエディタが読めない(`cygpath -w` で直す)。PowerShell で `&` を付けて起こすときは、`& "C:\...\ScenarioCheck.exe" check "C:\...\my_scenario"` のように、exe とパスを引用符で囲む。
  - 作るシナリオは、自分の作業フォルダで作り、できたら Scenarios に置く(か zip にしてタイトル画面に落とす)。
- **JSON と手引きは UTF-8 のまま読み書きする**。Windows PowerShell 5 の `Get-Content` は、指定しないと日本語を化けて読む(この README も)。読むときは必ず `Get-Content -Encoding UTF8 <ファイル>`、書くときも `Set-Content -Encoding utf8` にする。
  - PowerShell の `ConvertTo-Json` は、パイプで渡した要素が1つだけの配列を、配列でない1つのオブジェクトにしてしまう。一番上が配列のファイル(`dungeons.json`・`bosses.json`・`encounters.json`・`worlds.json`・`events.json`・`monsters.json`・`items.json` など。`manifest.json` と `text/*.json` はオブジェクト)は、`ConvertTo-Json -InputObject @($list) -Depth 20` のように渡して、`[ ]` のまま保つ。書いたあとは `check` を流す(形が崩れると読み込みで × が出る)。
- **同梱のシナリオを写して始めるときは、`manifest.json` の `id` と `guid` を新しくする**。同じ id・guid のままだと、元のシナリオと同じ物として扱われる。
- **できないことを先に読む**(下の「シナリオでできること・できないこと」)。いちばん時間を失うのは、できると思い込んで作り込むこと。

## 置き場所(インストール先)

| 場所 | 中身 |
|---|---|
| `GravewardSpiral.exe` | ゲーム |
| `GravewardSpiralEditor.exe` | シナリオエディタ(迷宮・イベント・物語・検査を画面で) |
| `ScenarioCheck.exe` | 検査の道具(画面なし。エージェントはこれで確かめる) |
| `Content/srd/` | 土台のデータ(SRD 5.1 のクラス・呪文・モンスター・品・標準のイベント)。**手本として読む。直さない** |
| `Content/scenarios/` | 同梱のシナリオ(第1部〜第3部)。完成した手本 |
| `scenario-kit/` | この手引き |

## まず見本を見る

[examples/kit_example](examples/kit_example) は、検査に注意 0 件で通る小さな完成例。2階の迷宮に、鍵と錠、フラグを立てるレバーとそれを読む祭壇、スイッチで開く門、最下層の主、印の文、階の名前と序文が入っている。
どのファイルも短いので、最初に全部読む。仕掛けの組み方は [gaps.md](gaps.md) の「仕掛けの組み方」で、この見本の行を指して説明している。

## 作る順番

1. **雛形を作る**: `ScenarioCheck new <作業フォルダ>\<id> --floors 3 --size 12x12 --levels 1-5` で、manifest・dungeons・自動生成した迷宮の雛形ができる(id はフォルダの名前。`--levels` を省くと 1-5)。迷宮の形はつまみでテーマに寄せる(下の表)。手で地図を一から書くより、ずっと早くて間違いが少ない。
   - 雛形の地図には、土台のはめ込む部屋(レバーの部屋・双子のスイッチ・印の祠・封印の宝物庫・謎かけの扉など)が入り、その標準イベントの印(`event:lever`、`gate:twin` など)が最初からある。`events.json` が空でもそのまま動く。使うならそのまま、使わないなら組ごと外す(部屋の `mark` の行を全部消し、門の錠の辺 `L` も壁か通路にする。どの印が1組かは `finish` が組ごとに出す)。はじめから入れないなら `--prefabs none`。ただし `--prefabs none` でも、あとで `fill` を流すと、標準のイベントを散らして置く(要らなければ manifest の `standard_events: false`)。
   - `worlds.json`(序文・階の名前・入ったときの文・主の文の雛形。`TODO:` で始まる)・`bosses.json`(最下層の主 `final` の形)・`text/ja.json`・`text/en.json`(空の文言)も作る。`TODO:` が残っていると `check` が注意を出す。要らない項目は消してよい(消すと、テーマの決まった文か「B 何階」だけになる)。
   - `flags.json` と `event_sets.json` は、エディタが使う説明のファイル(共通フラグの名前と説明、仕掛けの組の部品)。ゲームの動きは変えない。空の `[]` のままでよい。
   - 地図を直すときは `ScenarioCheck map <フォルダ> --floor N` で、座標つきの図を見る(座標を数えなくてよい)。
2. **表書き**: `manifest.json` の名前・説明・レベル帯を書き換える。形式は [reference/manifest.md](reference/manifest.md)。
3. **迷宮を直す**: `maps/*.txt` を、テーマに合わせて直す(部屋を足す、印を置く、仕掛けを置く)。形式は [reference/maps.md](reference/maps.md)、階の決まりは [gaps.md](gaps.md) の「階の決まり」。
4. **敵と宝**: `dungeons.json` の `rosters`(階ごとに出る敵の id)、`encounters.json`(レベル帯・部屋の戦い)、`bosses.json`(最下層の主は id `final`)、`named_monsters.json`、必要なら `monsters.json`・`items.json`。使える id は `ScenarioCheck list monsters` などで見る。決めきれない所は `ScenarioCheck fill` で埋めてから、テーマに合わない所を直す。
   - `fill` が選ぶ敵は、レベル帯に合う強さだけで選び、テーマは見ない(自由な迷宮と同じ選び方)。`rosters` を見て、テーマに合わない種類(書庫に獣、修道院に海の敵など)を、`list monsters` で同じくらいの CR の敵に差し替える。名のある敵(`named_monsters.json`)と主(`bosses.json` の `final`)も同じように見る。
5. **イベント**: `events.json`。形式は [reference/events.md](reference/events.md)、使える手順と条件の種類は `ScenarioCheck reference`。迷宮のマスに `mark x y event:<id>` で置くか、`scatter` で散らす。
6. **物語と文言**: `worlds.json`(序文・階の名前・入ったときの文)、`text/ja.json`・`text/en.json`(印 `message:<キー>` の文など)、`rumors.json`(酒場の噂)。
7. **クラスや種族を変えるなら**: [class-pack-guide.md](class-pack-guide.md)。
8. **確かめる**: 下の「確かめ方」。書く→確かめる→直す、を回す。`！` の注意は、わざとでなければ直す。
9. **仕上げる**: 最後に `ScenarioCheck finish <フォルダ>` を流す。できたものの一覧と、このシナリオで確かめること(レベル帯、階の名前と入ったときの文がどこから出るか、ボスの文、門、標準の部屋の印を組ごとに、使っていない地図、英日の文)が出る。項目を確かめて、出力を報告に貼る。
   - `finish` を流しただけでは、確かめたことにならない。報告では「データの照合」(`check`・`finish`・ファイルを読んで合わせたこと)、「ゲームで確かめた」(`--saves` 付きで実際に遊んで見たこと)、「まだ確かめていない」を分けて書く。

`new` の形のつまみを以下に示す。

| つまみ | 値 | 例 |
|---|---|---|
| `--rooms` | few / normal / many | 修道院や城は many、洞窟は few |
| `--doors` | few / normal / many | 建物は many、洞窟や遺跡は few |
| `--gimmicks` | none / few / normal / many | 一方通行・暗闇・霧・回転床・テレポーター・落とし穴・錠の多さ。初心者向けは none か few |
| `--prefabs` | standard / none | 土台のはめ込む部屋(標準イベントの印つき)を入れるか |

同じ指定とシード(`--seed`)なら、同じ迷宮になる。気に入らなければシードを変えて作り直す(フォルダは空でないと作らない)。

同梱のシナリオ(`Content/scenarios/gravekeeper` など)は、どのファイルも揃った手本。迷ったら同じ名前のファイルを開いて形を見る。

## 確かめ方

| やること | コマンド |
|---|---|
| 雛形と迷宮を作る | `ScenarioCheck new <フォルダ> [--floors N] [--size WxH] [--levels 1-5] [--rooms/--doors/--gimmicks ...]` |
| 使える id の一覧 | `ScenarioCheck list monsters\|items\|conditions\|skills\|abilities\|damage\|classes\|races\|sounds`(モンスターは `--cr 1/4-3 --type undead,construct` で絞れる) |
| 座標つきの地図 | `ScenarioCheck map <フォルダ> [--floor N]` |
| 仕上げの点検(最後に流して、報告に貼る) | `ScenarioCheck finish <フォルダ>` |
| まとめての検査(エディタの「検査」と同じ) | `ScenarioCheck check <フォルダか zip>` |
| ボットに通しで遊ばせる | `ScenarioCheck check <フォルダ> --bots 5 [--party fighter,cleric,wizard] [--level 5]`(既定は標準の6人・Lv 1。推奨レベルの下限で見るなら `--level` を付ける) |
| 決めていない中身を埋める | `ScenarioCheck fill <フォルダ> [--seed 1]`(フォルダを書き換える) |
| クラスのレベルごとのスロット・覚える数・使える呪文 | `ScenarioCheck spells <クラス> --scenario <フォルダ>` |
| その id を何が指しているか(消すと何が壊れるか) | `ScenarioCheck uses <id> --scenario <フォルダ>` |
| モンスターの id・名前・使う絵 | `ScenarioCheck art --scenario <フォルダ>` |
| 今の版が知っている部品の種類 | `ScenarioCheck reference` |
| 実際に遊んで見る | `GravewardSpiral.exe --saves <使い捨て> --start scenario:<フォルダ>[:階]`(使い方と画面の操作のこつは `GravewardSpiral.exe --help`。窓を開かずに出る) |
| 画面を保存して見る | 上に `--screenshot <png> --actions "U U R C" --frames 300` を足す |

- `check` の行の頭: `○` 通った、`×` 問題(直す)、`！` 注意(わざとならそのまま)、`・` 知らせ(無くても遊べる)。終了コードは 0 が問題なし、1 が問題あり。
- `--start scenario:` は、おまかせの6人で、その階の入口から始める(セーブしない)。`--actions` の文字: U/D/L/R 動く、C 決定、X 取消、M 地図、T メニュー、. 待つ(8 フレームに1つ押す)。`--actions` の値は空白で区切るので、必ず引用符で1つにまとめて渡す(`--actions "U U R C"`)。PowerShell の `Start-Process -ArgumentList` に配列で渡すと空白で割れるので、`&` でそのまま起こすか、引数を1つの文字列にして中で `\"U U R C\"` と囲む。
- 「読み込める」「検査に通る」「ボットがクリアできる」「遊んで面白い」は別のこと。最後は人が遊んで確かめる。

## シナリオでできること・できないこと

できること:

- 迷宮(階の数・大きさ・壁・扉・仕掛け・暗闇・魔法禁止・エレベーター・テレポーター)を自由に作る。
- イベント(会話・選択・戦闘・宝・フラグ・門・移動・エンディング)を部品で組む。
- 出る敵・ボス・名のある敵・品を足す。標準のモンスターや品を `extends` で写して名前や数値を変える。
- クラス・種族・技と能力・呪文の束・スロットの表を足す、標準のものを直す・外す([class-pack-guide.md](class-pack-guide.md))。
- 曲・タイトル画面の絵とロゴ・モンスターの絵を差し替える。

できないこと(今の版):

- **呪文の効果やレベルを変える、新しい呪文を作る**。シナリオの `spells.json` で変えられるのは名前だけ。クラスでは「どの呪文を、何レベルの呪文として、何レベルから使えるか」(`spell_access` の `spelllv`)と、スロットの数を変えられる。
- 1つのクラスに、別々のスロットの表を2つ持たせる(魔術師系と僧侶系を別に数える司教など)。
- クラスを変えてレベル 1 に戻る転職、クラスごとの経験値の表、性格による職業や階の制限。
- 新しい種類の部品(手順・条件・効果)。使えるのは `ScenarioCheck reference` に出るものだけ。

## もっと詳しく

- [gaps.md](gaps.md): 書き方の一覧には出てこない決まり(上書き・外す・写すの意味、省いたときの値、検査の文の読み方、よくある間違い)。
- [reference/README.md](reference/README.md): 各ファイルの項目の一覧(ゲームのソースから自動で書き出したもの)。
- [class-pack-guide.md](class-pack-guide.md): クラス・種族・技と能力・呪文の束。

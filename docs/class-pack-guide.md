# パックでクラスと種族を足す

シナリオのパック(またはコンテンツのパック)で、クラス・種族・技と能力・呪文の束・罠を足したり、SRD のものを直したりするときの書き方をまとめる。

## 置くファイル

パックに置けるファイルを以下に示す。どれも id を持つものの一覧で、同じ id はあとから読んだパックのものに置き換わる(`extends` で SRD のものを写して一部だけ変えられる)。

| ファイル | 中身 |
|---|---|
| `classes.json` | クラス(ヒットダイス・習熟・持ち物・伸び方・技と能力・回数・呪文・タグ) |
| `races.json` | 種族(能力値・技と能力・回数) |
| `features.json` | 技と能力の中身(`kind` が action か passive) |
| `spellsets.json` | 名前を付けた呪文の束(クラスの `spell_access` が指す) |
| `spell_progressions.json` | スロットの伸び方(full・half・third・pact など) |
| `spell_slots.json` | ふつうのスロットの表(術者のレベル 1〜20 ごとの数) |
| `shapes.json` | 変身先(モンスターと同じ書き方) |
| `traps.json` と `trapsets.json` | 罠と罠の束(宝箱は束 chest) |
| `text/ja.json` と `text/en.json` | 技・能力・回数の名前の文言 |

標準のクラスや種族を使わせたくないときは、`{"id": "bard", "disabled": true}` のように外す。外した物は一覧から消え、選べなくなる。外した物を指す標準の定義は、誤りにならない。誰も持たなくなった技や能力は使われないだけで、外したクラスや種族を条件にするイベントは、いつも当てはまらないだけになる。外した物も `extends` の元にはできる(ウィザードを写したクラスを作り、ウィザードは外す、など)。

シナリオのパック(`kind: scenario`)で変えられる呪文は、名前だけ。呪文の効果は変えられず、新しい呪文も作れない。クラスで変えられるのは、どの呪文を、そのクラスで何レベルの呪文として、クラスの何レベルから使えるか(呪文の束と `spell_access`)と、スロットの数(`spell_progressions` と `spell_slots`)。

## クラスの書き方

新しいクラスの例を以下に示す。3 分の 1 の術者で、L6 から2回攻撃し、セカンド・ウィンドと決闘のスタイルと、新しい能力を持つ。

```json
{
  "id": "spellblade", "name": { "en": "Spellblade", "ja": "魔剣士" }, "hit_die": 10, "tags": ["arcane"],
  "progression": { "attacks": { "by_level": { "1": 1, "6": 2 } } },
  "pools": [{ "id": "second_wind", "max": 1, "recharge": "short" }],
  "features": [{ "id": "second_wind", "level": 1 }, { "id": "style_dueling", "level": 1 }, { "id": "warding_steel", "level": 2 }],
  "primary": ["str", "int", "con"], "saves": ["str", "int"], "armor": ["light", "medium", "shield"], "weapons": ["simple", "martial"],
  "skill_choices": 2, "skills": ["arcana", "athletics", "history", "perception"], "start_gold": "4d4", "start_items": ["longsword", "scale_mail", "sack"],
  "spellcasting": { "ability": "int", "style": "known", "progression": "third", "from_level": 3,
    "cantrips": { "by_level": { "3": 2, "10": 3 } }, "known": { "by_level": { "3": 3, "7": 5, "13": 8 } },
    "spell_access": [{ "lv": 3, "spelllv": 0, "set": "wizard_cantrips" }, { "lv": 3, "spelllv": 1, "set": "wizard_1" }, { "lv": 7, "spelllv": 2, "set": "wizard_2" }] },
  "description": { "en": "...", "ja": "..." }
}
```

クラスの項目を以下に示す(SRD のクラスと同じ項目は省く)。

| 項目 | 中身 |
|---|---|
| `progression` | `attacks` は1回の攻撃で振る回数(数の書き方)、`ability_increases` は能力値上昇のレベル(省くと 4・8・12・16・19) |
| `features` | 技と能力の id と、覚えるクラスのレベル |
| `pools` | 回数の id・上限(数の書き方)・戻り方(short か long)・使い始めるレベル(`from_level`) |
| `tags` | 罠やイベントの `tag:<名前>` で当たる相手を決める(術者は arcane、信仰は divine) |
| `spellcasting` | 能力値・扱い(prepared・known・book・pact)・伸び方の id・使い始めるレベル・覚える数・準備の数・`spell_access` |

- `spell_access` の `lv` は、その束の呪文を覚えられるようになるクラスのレベルを書く。何レベルの呪文まで覚えられるかはスロットの表でも絞るので、早く書いても先には覚えない。
- `spell_access` の `spelllv` は、そのクラスでその束の呪文を何レベルの呪文として扱うか。呪文のもとのレベルと違ってよい(例: もとは 3 レベルのファイアーボールを 2 レベルの束に入れる)。そのクラスでは、要るスロット・覚えられるかどうか・呪文の一覧のレベルは `spelllv` で数える。効果は、`spelllv` のスロットで唱えるともとのレベルの強さで、そこから上のスロットの分だけ強くなる(2 に割り当てたファイアーボールを 3 で唱えると、もとの 4 レベルで唱えた強さ)。初級呪文は `spelllv` 0 の束だけ、ほかの呪文は 1〜9 の束だけに入れられる。巻物や杖など、クラスの束を通らない使い方は、もとのレベルのまま。
- 呪文(`spells.json`)の `level` は、その呪文の数値(上のスロットで増えるダメージの表や召喚の数など)を、何レベルを基準に書いたか。クラスから唱えるときのレベルは `spelllv` で決まり、`level` は効果の計算の基準と、巻物・杖・敵が使うときの既定のレベルにだけ使う。
- マルチクラスでは、どの能力もそのクラスのレベルで数える。同じ id の技や能力を2つのクラスから得ると1つにまとまり、回数の上限は大きいほうになる。

## 技と能力

技(`kind: action`)は、戦闘で選ぶもの。動きはコアの部品で、`part` で選ぶ(省くと id)。使える部品は second_wind・action_surge・rage・lay_on_hands・divine_smite・flurry_of_blows・patient_defense・stunning_strike・turn_undead・bardic_inspiration・wild_shape・breath_weapon。回数(`pool` と `cost`)、ダイス(`dice`)、数(`values`)、獣の姿で使えるか(`beast_form`)、変身先(`shapes`)はデータで変えられる。別の id で同じ部品を使えば、数や回数の違う派生の技になり、もとの技と一緒に持てる(例: `{ "id": "deep_breath", "extends": "second_wind", "dice": "2d8", "pool": "deep_breath" }`)。新しい動きの技は、コアに部品を足すまで作れない(読み込みの検査で落とす)。

勝手に効く能力(`kind: passive`)は、効く場面(`when`)と効果(`effects`)の組で書けば、コアを変えずに新しく作れる。

```json
{ "id": "warding_steel", "kind": "passive", "when": "wearing_armor",
  "effects": [{ "type": "ac_bonus", "value": 1 }, { "type": "save_bonus", "ability": "int", "value": 2 }] }
```

効く場面(`when`)を以下に示す。

| 値 | 効くとき |
|---|---|
| always | いつも(省いたとき) |
| no_armor / no_armor_no_shield / wearing_armor | 鎧なし / 鎧も盾もなし / 鎧を着ている |
| standing / raging / shaped / not_blinded / not_incapacitated | 立っている / 激怒のあいだ / 獣の姿のあいだ / 目が見える / 行動できる |
| grip:ranged / two_weapons / two_handed / one_handed / none_armored / none | 利き手の装備の形(ファイティングスタイル) |
| has_ranged_weapon | 飛び道具を持っている |

効果の種類を以下に示す。

| 種類 | 中身 |
|---|---|
| ac_ability / ac_bonus | 鎧なしの AC に足す能力値 / AC に足す数 |
| save_proficiency / save_bonus | セーヴの習熟(ability。省くと全部) / セーヴに足す数 |
| immune_condition / immune_magic_condition | その状態にならない / 魔法ではその状態にならない |
| immune_damage / resist_damage | その種類のダメージを受けない / 半分にする |
| advantage | 有利(roll は initiative・melee_attack・attacked・save。save は ability・condition・on: magic で絞れる) |
| no_advantage_against / evasion | 相手の有利を消す / 回避(敏捷力のセーヴで半分のものを成功 0・失敗半分) |
| attack_bonus / damage_bonus / reroll_damage / offhand_mod | 攻撃の上乗せ(on で効く攻撃を決める) |
| extra_damage / drain / crit_extra_dice / magical / spell_damage_bonus | 当たったときに足すもの |
| reaction_reduce / reaction_halve | 攻撃を受けたときの反応 |
| stay_up | 倒れかけたときに踏みとどまる(セーヴか、回数 pool を使う) |
| reroll_natural_one | d20 の 1 を振り直す(幸運) |
| no_cast / no_concentration | 唱えられない / 集中できない(集中できなくなったら、そのときの集中が切れる。集中の呪文を唱えても、集中はすぐ切れる)。unless に能力の id を書けば、その能力を持つ人には効かない |
| instant_kill | 攻撃で敵を一撃で倒す(Wizardry の忍者の首はね)。on は unarmed(素手)・melee(近接)・attack(どの攻撃でも)。value は倒す確率の百分率。1回の攻撃の行動で1度だけ、ダメージを1以上与えたときに振る。resist_base と resist_die を書くと、0〜resist_die−1 の乱数が resist_base + 敵の CR(切り捨て)を超えたときだけ倒れる。no_boss: true ならボスと名のある敵には効かない。文は message(省くと combat.instant_kill。首をはねる文は combat.decapitate) |
| resist_instant_kill | 一撃で倒されるのをこらえる確率(value の百分率)。敵の攻撃の kill の chance(首はね)に効く。効果ごとに別々に振り、どれか1つに当たればこらえる(Wizardry ではクラスの分 15% などと、レベル × 2%・上限 50% の分を別に振る) |

- `to: allies` を書くと、立っている仲間全員に届く(オーラ)。
- 重ねない能力は `group` でまとめ、`stacks` を first(先に取ったクラスのもの)か max(一番大きいもの)にする。
- 能力の一覧に出さないもの(数だけの能力など)は `listed: false` にする。
- 敵に首をはねさせるには、モンスターの行動の effects に `{ "type": "kill", "chance": 10, "message": "combat.decapitated" }` を書く(当たったとき 10% で、HP に関わらず倒す。くらった人は死ぬ。こらえるのは resist_instant_kill)。武器の on_hit にも書ける。
- Wizardry の #1〜3 の忍者と同じ首はねは `{ "type": "instant_kill", "on": "melee", "value": { "class_level": 2, "max": 50 }, "resist_base": 10, "resist_die": 35, "message": "combat.decapitate" }`(レベル × 2%、上限 50%。原作は素手でも武器でも同じ。素手だけにするなら on を unarmed に)。
- 激怒の制限は能力 rage_limits(raging のあいだ no_cast と no_concentration)、獣の姿の制限は能力 wild_shape_limits(shaped のあいだ no_cast。獣の呪文 beast_spells があれば外れる)。激怒や変身を持たせたクラスに、制限も持たせたいときは一緒に指す。

## 数の書き方

レベルで変わる数は、次のどれかで書く。

- 決まった数(`2`)。
- レベルごとの表(`{ "by_level": { "1": 2, "9": 3 } }`)。そのレベル以下の一番大きなキーの値。
- 組み合わせ(`{ "base": 8, "proficiency": 1, "ability": "wis" }`)。`class_level` と `total_level` に掛ける数、`round`(floor か ceil)、`min`・`max` も書ける。

クラスの数はそのクラスのレベルで、種族の数は合計のレベルで数える。種族では `class_level` は使えない。

## 種族

種族も `features` と `pools` で技と能力を持つ(覚える `level` は合計のレベル)。例えば、ドラゴンボーンは竜の息(技 breath_weapon と回数 breath_weapon)と火への抵抗(能力 draconic_resistance)を持つ。`traits` は名札で、ゲームの動きには使わない。

## 文言

クラスと種族が指す技・能力・回数には、日英の名前の文言が要る。足りなければ読み込みの検査で落とす。要るキーを以下に示す。

| もの | キー |
|---|---|
| 技 | `feature.<id>` と `feature.about.<id>` |
| 能力の一覧に出す能力 | `passive.<id>` |
| ファイティングスタイル | `passive.<id>` と `passive.about.<id>` |
| 回数 | `pool.<id>` |

【注意】クラスごとに技の呼び名を変える `features` の `name` は、まだ画面に出ない(技は `feature.<id>` の名前で出る)。

## 今の決まりと、まだできないこと

- クラス・技と能力・種族は、エディタの画面では作れない。パックのフォルダの JSON を直接書く(エディタで開いたシナリオのフォルダに置けば、保存や書き出しでそのまま入る)。
- シナリオで足したクラスの人は、そのクラスの無いシナリオへは連れて入れない(新しいゲームの画面で、無いクラスや呪文を出して止める)。クラスを足したシナリオでは、基本のタブの「クランを連れて入れる」を外しておく。
- 同じクラスを使うシナリオを続けて遊ばせたいとき(I → II → III)は、それぞれの manifest に同じ `series`(例 `wiz_classic`。エディタの基本のタブの「シリーズ」)を書く。「クランを連れて入れる」を外していても、そのシリーズのシナリオだけで遊んだクランを連れて入れる。順番の決まりは「始める条件」(クリアしたシナリオ)で書く。クラスや呪文の id は、シリーズの中で変えない。

## 確かめ方

- 読み込みの検査が、知らない項目・部品・場面、指す id が無いもの、数の形、文言の不足を拾う。エディタの「検査と書き出し」やゲームの読み込みで出る。文の終わりに、その物を書いたファイル(ファイル: …)と、イベントなら入れ子の場所(場所: steps[0].options[1].when[0] など)が付く。
- 試しに遊ぶときは、エディタの「試し遊び」でシナリオを開く。ボットの通しは `Sim --campaign --scenario <フォルダ> --parties <クラス,...>` で、足したクラスの仲間を通せる。

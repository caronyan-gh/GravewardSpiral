# SRD 5.1 の魔法のアイテムで magic_items.json に入れなかったもの

SRD(5e-database の 5e-SRD-Magic-Items.json、362 件)のうち、`tools/srd-source/magic_items.json` に変換しなかったものと、その理由。
`+N` の汎用品は宝の生成でふつうの品から作る。乗り物・飛行・水中・他の次元・召喚・変身・探索の便利道具など、このゲームの迷宮と戦闘で表せないものは入れていない。
「ぶんの派生」と書いたものは、個別の派生(色や種類ごと)の方を入れたので、まとめの項目だけ落としたもの。

| index | 理由 |
|---|---|
| adamantine-armor | クリティカルを通常の命中にする効果が表せない |
| ammunition / ammunition-1 / -2 / -3 | 矢弾は扱わない(+N の汎用品) |
| amulet-of-proof-against-detection-and-location | 占術よけ。ロールプレイ用 |
| amulet-of-the-planes | 次元移動 |
| animated-shield | 盾が勝手に浮く(手が空く)効果が表せない |
| apparatus-of-the-crab | 乗り物 |
| armor / armor-1 / -2 / -3 | +N の汎用品。宝の生成でふつうの鎧から作る |
| armor-of-resistance | まとめ項目。種類ごとの armor_of_resistance_* を入れた |
| arrow-of-slaying | 矢弾 |
| bag-of-beans | ランダムな出来事。表せない |
| bag-of-devouring | 中身を食べる袋。持ち物を壊す仕組みが無い |
| bag-of-tricks / -gray / -rust / -tan | 獣の召喚 |
| belt-of-dwarvenkind | 耐久力 +2(上限 20)は set_ability で表せない。他はロールプレイ用 |
| belt-of-giant-strength | まとめ項目。種類ごとの belt_of_giant_strength_* を入れた |
| boots-of-levitation | 浮遊 |
| boots-of-speed | 移動速度 2 倍。表せない |
| boots-of-striding-and-springing | 移動速度と跳躍 |
| bowl-of-commanding-water-elementals | 召喚 |
| brazier-of-commanding-fire-elementals | 召喚 |
| broom-of-flying | 飛行 |
| candle-of-invocation | 属性と呪文スロット。表せない |
| cape-of-the-mountebank | ディメンジョン・ドア(逃走)。迷宮では意味がない |
| carpet-of-flying / -3x5 / -4x6 / -5x7 / -6x9 | 飛行 |
| censer-of-controlling-air-elementals | 召喚 |
| chime-of-opening | 鍵を開ける。表せない |
| cloak-of-the-manta-ray | 水中 |
| crystal-ball / -of-mind-reading / -of-telepathy / -of-true-seeing | 遠見。ロールプレイ用 |
| cube-of-force | 力場の壁(複数のモード)。表せない |
| cubic-gate | 次元移動 |
| dancing-sword | 剣が勝手に攻撃する。表せない |
| decanter-of-endless-water | 水を出す |
| deck-of-illusions | 幻影 |
| deck-of-many-things | ランダムな運命。表せない |
| dimensional-shackles | 捕縛具。ロールプレイ用 |
| dragon-scale-mail | まとめ項目。色ごとの dragon_scale_mail_* を入れた |
| dust-of-dryness | 水を固める |
| efficient-quiver | 矢筒。矢弾を扱わない |
| efreeti-bottle | 召喚 |
| elemental-gem / -air / -earth / -fire / -water | 召喚 |
| eversmoking-bottle | 煙で視界を遮る。表せない |
| eyes-of-charming | チャーム・パーソンが呪文として未対応 |
| eyes-of-minute-seeing | 調査の判定。ロールプレイ用 |
| eyes-of-the-eagle | 知覚の判定に有利。表せない |
| feather-token / -anchor / -bird / -fan / -swan-boat / -tree / -whip | 船・鳥・木などの召喚、水上用 |
| figurine-of-wondrous-power(と 9 種の派生) | 獣の召喚 |
| folding-boat | 船 |
| gem-of-seeing | 真の視覚。表せない |
| gloves-of-missile-snaring | 遠隔攻撃のダメージをリアクションで減らす。表せない |
| gloves-of-swimming-and-climbing | 水泳・登攀 |
| goggles-of-night | 暗視。表せない |
| hat-of-disguise | 変装 |
| helm-of-comprehending-languages | 言語 |
| helm-of-telepathy | 読心 |
| helm-of-teleportation | テレポート |
| horn-of-valhalla / -silver / -brass / -bronze / -iron | 戦士の霊の召喚 |
| horseshoes-of-a-zephyr / horseshoes-of-speed | 馬用 |
| immovable-rod | 固定される棒。表せない |
| instant-fortress | 砦を出す |
| ioun-stone | まとめ項目(ioun_stone_of_protection だけ入れた) |
| ioun-stone-of-absorption / -of-greater-absorption | 呪文の吸収。表せない |
| ioun-stone-of-agility / -fortitude / -insight / -intellect / -leadership / -strength | 能力値 +2(上限 20)は set_ability で表せない |
| ioun-stone-of-awareness | 不意打ちされない。表せない |
| ioun-stone-of-mastery | 習熟ボーナス +1。表せない |
| ioun-stone-of-regeneration | 時間での HP 回復。表せない |
| ioun-stone-of-reserve | 呪文の保存 |
| ioun-stone-of-sustenance | 飲食不要 |
| iron-flask | 魔物の封印 |
| lantern-of-revealing | 透明なものを見る明かり |
| manual-of-bodily-health / -gainful-exercise / -quickness-of-action | 能力値を永久に +2。表せない |
| manual-of-golems / -clay / -flesh / -iron / -stone | ゴーレムの作成 |
| marvelous-pigments | 絵を実体にする |
| medallion-of-thoughts | 読心 |
| mirror-of-life-trapping | 生き物を閉じ込める鏡 |
| oil-of-etherealness | エーテル化 |
| oil-of-slipperiness | 滑る油(フリーダム・オヴ・ムーヴメント)。表せない |
| pearl-of-power | 呪文スロットの回復。表せない |
| periapt-of-health | 病気よけ。病気が無い |
| periapt-of-wound-closure | 自動で容体が落ち着く・ヒットダイス 2 倍。passive で表せない |
| philter-of-love | 魅了される水薬。ロールプレイ用 |
| pipes-of-the-sewers | ネズミの召喚 |
| plate-armor-of-etherealness | エーテル化 |
| potion-of-animal-friendship | 獣と仲良くなる |
| potion-of-clairvoyance | 遠見 |
| potion-of-climbing | 登攀 |
| potion-of-diminution / potion-of-growth | 体の大きさを変える(エンラージ/リデュース)。表せない |
| potion-of-flying | 飛行 |
| potion-of-gaseous-form | ガス化 |
| potion-of-giant-strength | まとめ項目。種類ごとの potion_of_giant_strength_* を入れた |
| potion-of-healing / potion-of-healing-common | items_extra.json の potion_of_healing と同じ |
| potion-of-mind-reading | 読心 |
| potion-of-resistance | まとめ項目。種類ごとの potion_of_resistance_* を入れた |
| potion-of-water-breathing | 水中 |
| ring-of-animal-influence | 獣への呪文 |
| ring-of-djinni-summoning | 召喚 |
| ring-of-elemental-command / -air / -earth / -fire / -water | 精霊への支配と多数の呪文。表せない |
| ring-of-feather-falling | 落下 |
| ring-of-jumping | 跳躍 |
| ring-of-mind-shielding | 読心よけ |
| ring-of-regeneration | 時間での HP 回復。表せない |
| ring-of-resistance | まとめ項目。種類ごとの ring_of_resistance_* を入れた |
| ring-of-spell-storing | 呪文の保存 |
| ring-of-swimming / ring-of-water-walking | 水上・水中 |
| ring-of-telekinesis | 物を動かす |
| ring-of-x-ray-vision | 透視 |
| robe-of-eyes | 全方位の視覚・暗視。表せない |
| robe-of-useful-items | 道具を取り出す |
| rod-of-absorption | 呪文の吸収 |
| rod-of-alertness | 知覚とイニシアチブ、多数の呪文。表せない |
| rod-of-rulership | 8 時間の魅了。戦闘用ではない |
| rod-of-security | 別の次元の楽園 |
| rope-of-climbing | 登攀 |
| slippers-of-spider-climbing | 壁を登る |
| sovereign-glue / universal-solvent | 接着剤と溶剤 |
| spell-scroll / spell-scroll-cantrip / -1st 〜 -9th | 汎用の巻物。代わりに呪文ごとの scroll_* を 22 種入れた |
| sphere-of-annihilation / talisman-of-the-sphere | 消滅の球とその制御 |
| staff-of-the-python | 大蛇に変身させる |
| stone-of-controlling-earth-elementals | 召喚 |
| talisman-of-pure-good / talisman-of-ultimate-evil | 属性が要る。属性が無い |
| tome-of-clear-thought / -leadership-and-influence / -understanding | 能力値を永久に +2。表せない |
| trident-of-fish-command | 獣の支配 |
| wand-of-enemy-detection / wand-of-magic-detection / wand-of-secrets | 探知。表せない |
| wand-of-polymorph | 変身 |
| wand-of-the-war-mage | まとめ項目。+1/+2/+3 の wand_of_the_war_mage_* を入れた |
| wand-of-wonder | ランダムな効果。表せない |
| weapon / weapon-1 / -2 / -3 | +N の汎用品。宝の生成でふつうの武器から作る |
| well-of-many-worlds | 次元の門 |
| wind-fan | ガスト・オヴ・ウィンドが呪文として未対応 |
| winged-boots / wings-of-flying | 飛行 |
| orb-of-dragonkind | アーティファクト。竜の支配と呪文。表せない |

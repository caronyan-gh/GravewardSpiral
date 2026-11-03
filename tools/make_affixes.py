"""装備の追加効果(Content/srd/affixes.json)を書き出す。数字はここで直して流し直す。

段(T1〜T20)は宝の目安のレベルで上がる。段の間は 3 レベルを4回、4 レベルを4回、5 レベルを4回…と、4段ごとに1レベルずつ伸びる
(L1・4・7・10・13・17・21・25 …、L94 で T20)。1周目の最下層(L13)で T5。段ごとの数字は、追加効果ごとの式で決める。
品に付くときは、目安のレベルで出る一番上の段から、下へ5段の中で均等に選ぶ(treasure.json の affix_tier_window)。
使い方: python tools/make_affixes.py
"""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Content" / "srd" / "affixes.json"

TIERS = 20


def tier_levels():
    levels, lv = [], 1
    for t in range(1, TIERS + 1):
        levels.append(lv)
        lv += 3 + (t - 1) // 4
    return levels


LEVELS = tier_levels()
affixes = []


def add(id, ja, en, position, kinds, tiers, group=None, weight=10, rare_only=False):
    a = {"id": id, "name": {"en": en, "ja": ja}, "position": position, "kinds": kinds, "weight": weight, "tiers": tiers}
    if group:
        a["group"] = group
    if rare_only:
        a["rare_only"] = True   # マジックには付かない
    affixes.append(a)


def price(base, t):
    """段 t の値段(段の 1.8 乗で上がる)。"""
    return int(round(base * t ** 1.8, -1)) or 10


def dice(avg):
    """平均がだいたい avg になるダイス。面の数は 2 から好きな数(デジタルなので d5 や d7 も使う)。1個で平均 6 くらいまで、そこからは個数を増やす。"""
    n = max(1, math.ceil(avg / 6))
    side = max(2, math.floor(2 * avg / n - 1 + 0.5))
    return f"{n}d{side}"


def tiers(first_level, make, base_price):
    """first_level から出る段の列。make(t) は段 t の中身(passive / on_hit の辞書)。"""
    out = []
    for t, lv in enumerate(LEVELS, start=1):
        if lv < first_level:
            continue
        out.append({"min_level": lv, **make(t), "price": price(base_price, t)})
    return out


# 付く場所。修正値は重なりすぎないように、付く場所を絞る(ダメージと命中は武器と指輪と手、AC は鎧・盾・外套)
WEAPON = ["weapon"]
HIT = ["weapon", "ring", "hands"]
DAMAGE = ["weapon", "ring"]
AC = ["armor", "shield", "cloak"]
SAVE = ["armor", "shield", "ring", "neck", "cloak", "waist"]
RESIST = ["armor", "shield", "ring", "neck", "cloak"]
IMMUNE = ["armor", "neck", "head", "feet"]

# ---- 武器: 属性の追加ダメージ(1d2 から始まる弱めの分、いくつ付いてもよい。同じ属性は1つまで) ----
ELEMENTS = [
    ("fire", "燃える", "Flaming"), ("cold", "凍てつく", "Freezing"), ("lightning", "雷の", "Shocking"),
    ("acid", "溶かす", "Corrosive"), ("poison", "毒の", "Venomous"), ("thunder", "轟く", "Thundering"),
    ("necrotic", "呪われし", "Vile"), ("radiant", "聖なる", "Holy"),
]
for dtype, ja, en in ELEMENTS:
    add(f"elemental_{dtype}", ja, en, "prefix", WEAPON, weight=6, tiers=tiers(
        1, lambda t, d=dtype: {"on_hit": [{"type": "damage", "dice": dice(1.5 + 0.8 * (t - 1)), "damage_type": d}]}, 300))

# ---- 武器: 種類への特効 ----
BANES = [
    ("undead", "死者祓いの", "of Undead Bane"), ("dragon", "竜殺しの", "of Dragon Slaying"),
    ("fiend", "悪魔祓いの", "of Fiend Bane"), ("giant", "巨人殺しの", "of Giant Slaying"),
    ("aberration", "異形狩りの", "of Aberration Hunting"), ("monstrosity", "怪物狩りの", "of Monster Hunting"),
]
for ctype, ja, en in BANES:
    add(f"bane_{ctype}", ja, en, "suffix", WEAPON, group="bane", weight=6, tiers=tiers(
        1, lambda t, c=ctype: {"on_hit": [{"type": "damage", "dice": dice(3.5 + 1.5 * (t - 1)), "damage_type": "force", "only": c}]}, 300))

# ---- 命中とダメージ(段3つで +1。ダメージは段2つで +1) ----
add("accurate", "正確な", "Accurate", "prefix", HIT, group="accuracy", tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "attack", "value": (t + 2) // 3}]}, 500))
add("brutal", "残忍な", "Brutal", "prefix", DAMAGE, group="damage", tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "damage", "value": (t + 1) // 2}]}, 300))

# ---- 武器: 吸う(当てたダメージの何割かが、自分の HP に戻る) ----
add("life_stealing", "吸命の", "of Life Stealing", "suffix", WEAPON, group="drain", weight=5, tiers=tiers(
    1, lambda t: {"on_hit": [{"type": "drain", "value": min(50, 8 + 2 * t)}]}, 600))

# ---- 武器: 当たると状態(セーヴ) ----
STATUS = [
    ("stunning", "打ちのめす", "of Stunning", "con", "stunned", 1, 6, 3),
    ("venom", "蛇毒の", "of Venom", "con", "poisoned", 3, 1, 6),
    ("terror", "恐怖の", "of Terror", "wis", "frightened", 2, 1, 5),
    ("binding", "縛めの", "of Binding", "str", "restrained", 2, 6, 4),
]
for id, ja, en, ab, cond, rounds, first, weight in STATUS:
    add(f"onhit_{id}", ja, en, "suffix", WEAPON, group="status", weight=weight, tiers=tiers(
        first, lambda t, ab=ab, cond=cond, rounds=rounds: {"on_hit": [{"type": "save", "ability": ab, "dc": 10 + t,
            "fail": [{"type": "condition", "condition": cond, "rounds": rounds}]}]}, 800))

# ---- 守り(段3つで +1) ----
add("warding", "守りの", "Warding", "prefix", AC, group="ac", tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "ac", "value": (t + 2) // 3}]}, 600))
add("resolute", "不屈の", "Resolute", "prefix", SAVE, group="save", tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "save", "value": (t + 2) // 3}]}, 800))

# ---- 属性への抵抗(1つの品に1つまで。有る無しなので段は1つ) ----
RESISTS = [
    ("fire", "耐火の", "of Fire Resistance"), ("cold", "耐寒の", "of Cold Resistance"),
    ("lightning", "絶縁の", "of Lightning Resistance"), ("acid", "耐酸の", "of Acid Resistance"),
    ("poison", "解毒の", "of Poison Resistance"), ("necrotic", "生命の", "of Necrotic Resistance"),
    ("thunder", "静寂の", "of Thunder Resistance"), ("psychic", "明晰の", "of Psychic Resistance"),
]
for dtype, ja, en in RESISTS:
    add(f"resist_{dtype}", ja, en, "suffix", RESIST, group="resist", weight=6, tiers=[
        {"min_level": 1, "passive": [{"type": "resist", "damage_type": dtype}], "price": 2000}])

# ---- 状態への備え(その状態にするセーヴと、解くセーヴに +N。「ならない」は強すぎるので作らない) ----
IMMUNES = [
    ("free_action", "自由の", "of Free Action", ["paralyzed", "restrained"], 6),
    ("clarity", "澄んだ心の", "of Clarity", ["charmed", "frightened"], 1),
    ("vigor", "頑健の", "of Vigor", ["poisoned"], 1),
    ("sight", "見通す", "of Sight", ["blinded"], 6),
]
for id, ja, en, conds, first in IMMUNES:
    add(f"immune_{id}", ja, en, "suffix", IMMUNE, group="cond_save", weight=4, tiers=tiers(
        first, lambda t, conds=conds: {"passive": [{"type": "modifier", "stat": f"save_vs:{c}", "value": 1 + (t + 1) // 2} for c in conds]}, 700))

# ---- 水増し: 技能と、罠外し・逃げる(弱め。強い組み合わせが揃いにくくなるように候補を増やす。同じ種類の制限は無い) ----
PHYS = ["armor", "cloak", "hands", "feet", "waist"]
MIND = ["ring", "neck", "head"]
SOCIAL = ["ring", "neck", "cloak", "head"]
WILD = ["neck", "cloak", "feet"]
SKILLS = [
    ("acrobatics", "身軽な", "Nimble", "prefix", PHYS),
    ("athletics", "剛力の", "Mighty", "prefix", PHYS + ["weapon"]),
    ("stealth", "忍びの", "Stealthy", "prefix", PHYS),
    ("sleight_of_hand", "器用な", "Deft", "prefix", ["hands", "ring", "cloak"]),
    ("arcana", "秘術の", "of Arcana", "suffix", MIND),
    ("history", "古老の", "of Lore", "suffix", MIND),
    ("religion", "敬虔な", "Pious", "prefix", MIND),
    ("investigation", "探索者の", "of the Seeker", "suffix", MIND),
    ("insight", "慧眼の", "Discerning", "prefix", MIND),
    ("perception", "鋭い目の", "Keen-eyed", "prefix", MIND + ["cloak"]),
    ("medicine", "癒し手の", "of the Healer", "suffix", MIND),
    ("animal_handling", "獣使いの", "of the Beastmaster", "suffix", WILD),
    ("nature", "森の", "of the Wilds", "suffix", WILD),
    ("survival", "旅人の", "of the Wanderer", "suffix", WILD),
    ("deception", "欺きの", "of Deceit", "suffix", SOCIAL),
    ("persuasion", "弁舌の", "of Eloquence", "suffix", SOCIAL),
    ("intimidation", "威圧の", "Menacing", "prefix", SOCIAL + ["weapon"]),
    ("performance", "華やかな", "Dazzling", "prefix", SOCIAL),
]
for skill, ja, en, pos, kinds in SKILLS:
    add(f"skill_{skill}", ja, en, pos, kinds, weight=8, tiers=tiers(
        1, lambda t, sk=skill: {"passive": [{"type": "modifier", "stat": sk, "value": 1 + (t + 1) // 2}]}, 200))
add("trapper", "罠師の", "of the Trapper", "suffix", ["hands", "ring"], weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "disarm", "value": 1 + (t + 1) // 2}]}, 250))
add("escape", "逃げ足の", "of Escape", "suffix", ["feet", "cloak"], weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "flee", "value": 1 + (t + 1) // 2}]}, 250))

# ---- 追い打ち(段と同じ % の確率で、攻撃をもう1回。5か所に付き、付けた品の合計) ----
add("flurry", "連撃の", "of Flurry", "suffix", ["weapon", "ring", "hands", "neck", "waist"], group="flurry", weight=6, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "extra_attack", "value": t}]}, 600))

# ---- 薙ぎ(武器だけ。当たった攻撃が、同じグループのほかの敵にも行く。確率は (T〜T+4) × 1.5 %、何体までは 1〜T/4 体を品ごとに振る) ----
add("cleave", "薙ぎの", "Sweeping", "prefix", WEAPON, group="cleave", weight=6, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "cleave", "value": math.floor(t * 1.5), "value_max": math.floor((t + 4) * 1.5)},
                              {"type": "modifier", "stat": "cleave_targets", "value": 1, "value_max": max(1, t // 4)}]}, 600))

# ---- 最大 HP(段 T で 4T−3〜4T を品ごとに振る。T1 は 1〜4、T2 は 5〜8) ----
add("sturdy", "頑丈な", "Sturdy", "prefix", ["armor", "shield", "neck", "waist", "head"], group="hp", weight=10, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "max_hp", "value": 4 * t - 3, "value_max": 4 * t}]}, 300))

# ---- 属性のダメージ軽減(受けるたびに −N。1つの品に1つまで) ----
REDUCES = [
    ("fire", "炎避けの", "Flameguard"), ("cold", "霜避けの", "Frostguard"), ("lightning", "雷避けの", "Stormguard"),
    ("acid", "酸避けの", "Acidguard"), ("poison", "毒避けの", "Venomguard"), ("necrotic", "死避けの", "Deathguard"),
    ("radiant", "光避けの", "Lightguard"), ("thunder", "音避けの", "Soundguard"), ("psychic", "心避けの", "Mindguard"),
    ("force", "力場避けの", "Forceguard"),
]
for dtype, ja, en in REDUCES:
    add(f"reduce_{dtype}", ja, en, "prefix", ["armor", "shield", "cloak", "ring", "neck"], group="reduce", weight=5, tiers=tiers(
        1, lambda t, d=dtype: {"passive": [{"type": "modifier", "stat": f"reduce:{d}", "value": 1 + (t + 1) // 2}]}, 400))
add("reduce_physical", "鉄壁の", "Ironclad", "prefix", ["armor", "shield"], group="reduce", weight=5, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "reduce:physical", "value": (t + 2) // 3}]}, 900))

# ---- 棘(近接で殴ってきた敵に、そのダメージを返す) ----
add("thorns", "棘の", "Thorned", "prefix", ["armor", "shield", "cloak"], group="thorns", weight=6, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "thorns", "value": 1 + t}]}, 500))

# ---- クラスの能力の回数(+段/5 切り上げ。そのクラスでなければ効かない) ----
USES = [
    ("rage", "憤怒の", "Furious", "prefix"), ("bardic_inspiration", "詩人の", "of the Bard", "suffix"),
    ("channel_divinity", "神威の", "of Divinity", "suffix"), ("wild_shape", "変化の", "of Shifting", "suffix"),
    ("second_wind", "息吹の", "of Second Wind", "suffix"), ("action_surge", "奮迅の", "Surging", "prefix"),
    ("indomitable", "不撓の", "of Indomitability", "suffix"), ("ki", "気功の", "of Ki", "suffix"),
    ("sorcery_points", "魔源の", "of Sorcery", "suffix"),
]
for pool, ja, en, pos in USES:
    add(f"uses_{pool}", ja, en, pos, ["ring", "neck", "head", "waist"], weight=4, tiers=tiers(
        1, lambda t, pl=pool: {"passive": [{"type": "modifier", "stat": f"uses:{pl}", "value": math.ceil(t / 5)}]}, 500))

# ---- ほかのゲームから持ってきたもの(Diablo・PoE・Grim Dawn の定番。クラスの能力の代わりにならないもの) ----
add("swift", "先手の", "Swift", "prefix", ["feet", "head", "neck", "ring"], group="initiative", weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "initiative", "value": 1 + (t + 1) // 2}]}, 300))
add("golden", "黄金の", "of Greed", "suffix", ["ring", "neck", "waist", "hands"], group="gold", weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "gold_find", "value": 5 * t, "value_max": 10 * t}]}, 200))
add("feeding", "糧の", "of Feeding", "suffix", ["weapon", "ring", "hands"], group="on_kill", weight=6, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "life_on_kill", "value": t, "value_max": 2 * t + 1}]}, 400))
add("regenerating", "再生の", "of Regeneration", "suffix", ["armor", "ring", "neck"], group="regen", weight=5, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "regen", "value": (t + 3) // 4}]}, 800))
add("bleeding", "裂傷の", "of Wounding", "suffix", ["weapon"], group="bleed", weight=6, tiers=tiers(
    1, lambda t: {"on_hit": [{"type": "dot", "dice": dice(1.5 + 0.75 * (t - 1)), "damage_type": "slashing", "rounds": 3}]}, 500))
add("potent", "秘力の", "of Power", "suffix", ["weapon", "ring", "neck", "head"], group="spell_damage", weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "spell_damage", "value": 1 + (t + 1) // 2}]}, 600))
add("blocking", "盾受けの", "of Blocking", "suffix", ["shield"], group="block", weight=8, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "block", "value": t, "value_max": t + 4}]}, 600))

# ---- 武器だけ: 呪文スロット(レベルごとに別の追加効果なので、違うレベルなら複数付く。同じレベルは付かない)。
# レベル k のスロットは段 Tk から付く(段で一番上のレベルを絞る)。数は +1、そこから5段ごとに +1。もともとそのレベルのスロットがある人だけに効く
CIRCLES = ["一", "二", "三", "四", "五", "六", "七", "八", "九"]
ORDINALS = ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth"]
for k in range(1, 10):
    add(f"slot_{k}", f"第{CIRCLES[k - 1]}環の", f"of the {ORDINALS[k - 1]} Circle", "suffix", WEAPON, weight=4, tiers=tiers(
        LEVELS[k - 1], lambda t, k=k: {"passive": [{"type": "modifier", "stat": f"slots:{k}", "value": 1 + (t - k) // 5}]}, 300 * k))

# ---- 呪文の難易度と命中(段3つで +1、+3 まで。全部の装備を足しても +3 まで。エンジンで止める) ----
add("commanding", "威令の", "Commanding", "prefix", ["weapon", "ring", "neck", "head"], group="spell_dc", weight=5, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "spell_dc", "value": min(3, (t + 2) // 3)}]}, 1500))
add("unerring", "必中の", "Unerring", "prefix", ["weapon", "ring", "neck", "head"], group="spell_attack", weight=5, tiers=tiers(
    1, lambda t: {"passive": [{"type": "modifier", "stat": "spell_attack", "value": min(3, (t + 2) // 3)}]}, 1200))

# ---- 属性の呪文の強化(その属性の呪文のダメージ +N。違う属性ならいくつ付いてもよい) ----
SPELL_ELEMENTS = [
    ("fire", "火術の", "of Pyromancy"), ("cold", "氷術の", "of Frost Magic"), ("lightning", "雷術の", "of Storm Magic"),
    ("acid", "酸術の", "of Acid Magic"), ("poison", "毒術の", "of Venom Magic"), ("thunder", "轟術の", "of Thunder Magic"),
    ("necrotic", "死術の", "of Necromancy"), ("radiant", "光術の", "of Radiance"), ("force", "力術の", "of Force Magic"),
    ("psychic", "心術の", "of Mind Magic"),
]
for dtype, ja, en in SPELL_ELEMENTS:
    add(f"spell_{dtype}", ja, en, "suffix", ["weapon", "ring", "neck", "head"], weight=3, tiers=tiers(
        1, lambda t, d=dtype: {"passive": [{"type": "modifier", "stat": f"spell_damage:{d}", "value": t + 1}]}, 300))

# ---- 武器にも付ける弱めの分(武器の候補を水増しして、強い組み合わせが揃いにくくする) ----
WEAPON_FILLERS = ["skill_acrobatics", "skill_sleight_of_hand", "skill_perception", "skill_stealth", "swift",
                  "golden", "skill_survival", "skill_animal_handling", "trapper", "escape"]
for a in affixes:
    if a["id"] in WEAPON_FILLERS and "weapon" not in a["kinds"]:
        a["kinds"] = a["kinds"] + ["weapon"]

# ---- 補助の追加効果(軍用でない近接武器には、これだけが付く。強い攻めの効果は軍用の武器に) ----
SUPPORT = {"swift", "golden", "trapper", "escape", "commanding", "unerring", "potent"}
for a in affixes:
    if a["id"] in SUPPORT or a["id"].startswith(("skill_", "spell_", "slot_")):
        a["support"] = True

OUT.write_text(json.dumps(affixes, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(f"{len(affixes)} 個の追加効果を書き出しました: {OUT.relative_to(ROOT)}")

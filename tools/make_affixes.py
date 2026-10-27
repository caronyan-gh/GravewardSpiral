"""装備の追加効果(Content/srd/affixes.json)を書き出す。数字はここで直して流し直す。

段階は宝の目安のレベル(min_level)で上がる。1周目の最下層は L12〜14 くらい。
使い方: python tools/make_affixes.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Content" / "srd" / "affixes.json"

LEVELS = [1, 6, 11, 16]
affixes = []


def add(id, ja, en, position, kinds, tiers, group=None, weight=10):
    a = {"id": id, "name": {"en": en, "ja": ja}, "position": position, "kinds": kinds, "weight": weight, "tiers": tiers}
    if group:
        a["group"] = group
    affixes.append(a)


# ---- 武器: 属性の追加ダメージ(1つの品に1つまで) ----
ELEMENTS = [
    ("fire", "燃える", "Flaming"), ("cold", "凍てつく", "Freezing"), ("lightning", "雷の", "Shocking"),
    ("acid", "溶かす", "Corrosive"), ("poison", "毒の", "Venomous"), ("thunder", "轟く", "Thundering"),
    ("necrotic", "呪われし", "Vile"), ("radiant", "聖なる", "Holy"),
]
for dtype, ja, en in ELEMENTS:
    add(f"elemental_{dtype}", ja, en, "prefix", ["weapon"], group="element", weight=8, tiers=[
        {"min_level": lv, "on_hit": [{"type": "damage", "dice": d, "damage_type": dtype}], "price": p}
        for lv, d, p in zip(LEVELS, ["1d4", "1d6", "2d6", "3d6"], [400, 2000, 8000, 30000])])

# ---- 武器: 種類への特効 ----
BANES = [
    ("undead", "死者祓いの", "of Undead Bane"), ("dragon", "竜殺しの", "of Dragon Slaying"),
    ("fiend", "悪魔祓いの", "of Fiend Bane"), ("giant", "巨人殺しの", "of Giant Slaying"),
    ("aberration", "異形狩りの", "of Aberration Hunting"), ("monstrosity", "怪物狩りの", "of Monster Hunting"),
]
for ctype, ja, en in BANES:
    add(f"bane_{ctype}", ja, en, "suffix", ["weapon"], group="bane", weight=6, tiers=[
        {"min_level": lv, "on_hit": [{"type": "damage", "dice": d, "damage_type": "force", "only": ctype}], "price": p}
        for lv, d, p in zip(LEVELS, ["1d6", "2d6", "3d6", "4d6"], [300, 1500, 6000, 20000])])

# ---- 武器: 命中とダメージ ----
add("accurate", "正確な", "Accurate", "prefix", ["weapon"], group="accuracy", tiers=[
    {"min_level": lv, "passive": [{"type": "modifier", "stat": "attack", "value": v}], "price": p}
    for lv, v, p in zip(LEVELS, [1, 1, 2, 3], [500, 500, 4000, 15000])])
add("brutal", "残忍な", "Brutal", "prefix", ["weapon"], group="damage", tiers=[
    {"min_level": lv, "passive": [{"type": "modifier", "stat": "damage", "value": v}], "price": p}
    for lv, v, p in zip(LEVELS, [1, 2, 3, 5], [300, 1500, 5000, 15000])])

# ---- 武器: 吸う(当てたダメージの何割かが、自分の HP に戻る) ----
add("life_stealing", "吸命の", "of Life Stealing", "suffix", ["weapon"], group="drain", weight=5, tiers=[
    {"min_level": lv, "on_hit": [{"type": "drain", "value": v}], "price": p}
    for lv, v, p in zip(LEVELS, [10, 15, 20, 30], [600, 2500, 9000, 30000])])

# ---- 武器: 当たると状態(セーヴ) ----
STATUS = [
    ("stunning", "打ちのめす", "of Stunning", "con", "stunned", 1, 6, 3),
    ("venom", "蛇毒の", "of Venom", "con", "poisoned", 3, 1, 6),
    ("terror", "恐怖の", "of Terror", "wis", "frightened", 2, 1, 5),
    ("binding", "縛めの", "of Binding", "str", "restrained", 2, 6, 4),
]
for id, ja, en, ab, cond, rounds, first, weight in STATUS:
    tiers = []
    for lv, dc, p in zip(LEVELS, [11, 13, 15, 17], [800, 3000, 10000, 30000]):
        if lv < first:
            continue
        tiers.append({"min_level": lv, "price": p, "on_hit": [{"type": "save", "ability": ab, "dc": dc,
                      "fail": [{"type": "condition", "condition": cond, "rounds": rounds}]}]})
    add(f"onhit_{id}", ja, en, "suffix", ["weapon"], group="status", weight=weight, tiers=tiers)

# ---- 鎧・盾: 守り ----
add("warding", "守りの", "Warding", "prefix", ["armor", "shield"], group="ac", tiers=[
    {"min_level": lv, "passive": [{"type": "modifier", "stat": "ac", "value": v}], "price": p}
    for lv, v, p in zip(LEVELS, [1, 1, 2, 3], [600, 600, 5000, 20000])])
add("resolute", "不屈の", "Resolute", "prefix", ["armor", "shield"], group="save", tiers=[
    {"min_level": lv, "passive": [{"type": "modifier", "stat": "save", "value": v}], "price": p}
    for lv, v, p in zip(LEVELS, [1, 1, 2, 3], [800, 800, 6000, 20000])])

# ---- 鎧・盾: 属性への抵抗(1つの品に1つまで) ----
RESISTS = [
    ("fire", "耐火の", "of Fire Resistance"), ("cold", "耐寒の", "of Cold Resistance"),
    ("lightning", "絶縁の", "of Lightning Resistance"), ("acid", "耐酸の", "of Acid Resistance"),
    ("poison", "解毒の", "of Poison Resistance"), ("necrotic", "生命の", "of Necrotic Resistance"),
    ("thunder", "静寂の", "of Thunder Resistance"), ("psychic", "明晰の", "of Psychic Resistance"),
]
for dtype, ja, en in RESISTS:
    add(f"resist_{dtype}", ja, en, "suffix", ["armor", "shield"], group="resist", weight=6, tiers=[
        {"min_level": 1, "passive": [{"type": "resist", "damage_type": dtype}], "price": 2000}])

# ---- 鎧: 状態にならない ----
IMMUNE = [
    ("free_action", "自由の", "of Free Action", ["paralyzed", "restrained"], 6),
    ("clarity", "澄んだ心の", "of Clarity", ["charmed", "frightened"], 1),
    ("vigor", "頑健の", "of Vigor", ["poisoned"], 1),
    ("sight", "見通す", "of Sight", ["blinded"], 6),
]
for id, ja, en, conds, first in IMMUNE:
    add(f"immune_{id}", ja, en, "suffix", ["armor"], group="immune", weight=4, tiers=[
        {"min_level": first, "passive": [{"type": "immune_condition", "condition": c} for c in conds], "price": 4000}])

OUT.write_text(json.dumps(affixes, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(f"{len(affixes)} 個の追加効果を書き出しました: {OUT.relative_to(ROOT)}")

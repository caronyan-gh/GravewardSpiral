"""敵の落とし物の表(Content/srd/drops.json)を書き出す。数字はここで直して流し直す。

表は CR の帯ごと(どの種類にも合う)と、種類と CR の帯ごと(固有のレア品が混ざる)と、ボスごと。
種類の表は CR の帯の表より細かいので、そちらが使われる(等級の宝の候補も同じものを入れる)。
使い方: python tools/make_drops.py
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Content" / "srd" / "drops.json"

# CR の帯: (下限, 上限, 1体が落とす確率, 等級の候補と重み, どの種類でも混ざる固有の品)
BANDS = [
    (0, 1, 0.02, {"common": 6, "uncommon": 3}, {}),
    (2, 4, 0.04, {"uncommon": 6, "rare": 1}, {}),
    (5, 10, 0.06, {"uncommon": 4, "rare": 4, "very_rare": 1}, {"vigil_amulet": 0.5}),
    (11, 16, 0.08, {"rare": 4, "very_rare": 4, "legendary": 1}, {"spiral_ward": 0.5, "vigil_amulet": 0.5}),
    (17, 30, 0.12, {"very_rare": 4, "legendary": 3}, {"spiral_ward": 0.5}),
]

# 種類ごとの固有の品: 種類 -> [(CR の下限, CR の上限, 品, 重み)]
UNIQUES = {
    "undead": [(1, 6, "gravekeepers_pick", 1), (1, 10, "coffin_lid", 1), (3, 12, "bone_reaver", 1),
               (5, 30, "ninth_grave_ring", 1), (13, 30, "bloodthirst", 0.5)],
    "humanoid": [(1, 8, "whisperblade", 1), (1, 8, "shadowweave", 1)],
    "aberration": [(0, 30, "mindsplitter", 1)],
    "dragon": [(5, 30, "wyrmfang", 1), (5, 30, "dragonscale_mail", 1)],
    "fiend": [(5, 30, "hellbrand", 1), (13, 30, "bloodthirst", 0.5)],
    "giant": [(0, 30, "giantsbane_gauntlets", 1), (5, 30, "stormcaller", 1)],
    "elemental": [(5, 30, "stormcaller", 1)],
}

# 素材(ほかの落とし物とは別に、1体ごとに振る): (種類, CR の下限, CR の上限, 確率, 品)
EXTRA = [
    ("undead", 3, 30, 0.02, "consecrated_bell"),
    ("dragon", 5, 30, 0.3, "ancient_dragon_scale"),
]

# ボスごと(戦い1回で1回)
BOSSES = [
    ("boss_final", "final", 1.0, {"gravekeepers_pick": 2, "ninth_grave_ring": 2, "vigil_amulet": 2, "coffin_lid": 1}),
]


def entries(rarities, items):
    return [{"rarity": r, "weight": w} for r, w in rarities.items()] + [{"item": i, "weight": w} for i, w in items.items()]


tables = []
for lo, hi, chance, rarities, common in BANDS:
    tables.append({"id": f"cr_{lo}_{hi}", "cr_min": lo, "cr_max": hi, "chance": chance, "entries": entries(rarities, common)})
    for ctype, uniques in UNIQUES.items():
        items = {i: w for a, b, i, w in uniques if a <= hi and b >= lo}
        if items:
            tables.append({"id": f"{ctype}_{lo}_{hi}", "type": ctype, "cr_min": lo, "cr_max": hi, "chance": chance,
                           "entries": entries(rarities, {**common, **items})})
for ctype, lo, hi, chance, item in EXTRA:
    tables.append({"id": f"extra_{item}", "type": ctype, "cr_min": lo, "cr_max": hi, "chance": chance, "extra": True,
                   "entries": entries({}, {item: 1})})
for id, boss, chance, items in BOSSES:
    tables.append({"id": id, "boss": boss, "chance": chance, "entries": entries({}, items)})

OUT.write_text(json.dumps(tables, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(f"{len(tables)} 個の表を書き出しました: {OUT.relative_to(ROOT)}")

"""アイコン(呪文の種類・術者のクラス・状態)の一覧を出す、置かれているかを確かめる。

- 呪文: 効果の種類ごとに1枚。呪文の種類は呪文のデータの icon(tools/srd_import.py が効果から決める。
  spells_overrides.json の icon で上書きできる)。同じ種類の呪文は同じアイコンを使う。
- クラス: 呪文を使うクラスごとに1枚。マルチクラスの呪文の一覧で、どのクラスの呪文かの印に使う。
- 状態: 5版の状態と、死亡・灰。隊列の欄に出す。

使い方:
  python tools/icon_list.py           docs/icons.csv を書き出す(AI に渡す文の例つき)
  python tools/icon_list.py --check   アイコンが置かれているかを数えて、足りないものを出す

置き場所: Content/<パック>/art/<spells|classes|conditions>/<id>.png
仕様: 256x256 の正方形、背景は透過、文字なし。
"""
import csv
import json
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTENT = ROOT / "Content"
OUT = ROOT / "docs" / "icons.csv"

# 呪文の種類: (日本語, 英語, 分類, 絵の題材(英語), 色)
SPELLS = {
    "fire": ("火", "Fire", "攻撃", "a burst of roaring flame", "fiery orange and red"),
    "cold": ("冷気", "Cold", "攻撃", "jagged ice crystals and frost", "icy cyan and white"),
    "lightning": ("電撃", "Lightning", "攻撃", "a forked bolt of lightning", "electric blue and violet"),
    "thunder": ("雷鳴", "Thunder", "攻撃", "a concussive shockwave ring of sound", "steel blue and grey"),
    "acid": ("酸", "Acid", "攻撃", "a splash of corrosive bubbling acid", "toxic yellow-green"),
    "poison": ("毒", "Poison", "攻撃", "a cloud of poison with a skull shape", "sickly green and purple"),
    "necrotic": ("死霊", "Necrotic", "攻撃", "a withering black hand draining life", "deathly black and green"),
    "radiant": ("光輝", "Radiant", "攻撃", "a beam of holy light from above", "radiant gold and white"),
    "force": ("力場", "Force", "攻撃", "glowing arcane missiles of pure force", "bright magenta-orange"),
    "psychic": ("精神", "Psychic", "攻撃", "a cracked mind with psychic waves", "pink and deep purple"),
    "physical": ("物理", "Physical", "攻撃", "a crushing fist and spectral blades", "earthy brown and steel"),
    "heal": ("回復", "Healing", "支援", "glowing hands with a healing light", "soft green and white"),
    "revive": ("蘇生", "Revival", "支援", "a soul rising from a grave in light", "warm gold and white"),
    "cure": ("浄化", "Cleansing", "支援", "a purifying droplet washing away darkness", "clear blue and white"),
    "buff": ("強化", "Blessing", "支援", "an upward arrow surrounded by a heroic aura", "bright gold"),
    "protect": ("防御", "Protection", "支援", "a shimmering magic shield", "cool blue and silver"),
    "debuff": ("弱体", "Hex", "妨害", "binding chains and a downward spiral curse", "dark purple"),
    "death": ("即死", "Death", "妨害", "a single word of doom with a skull", "black and blood red"),
    "light": ("明かり", "Light", "便利", "a floating orb of warm light", "warm yellow"),
    "identify": ("鑑定", "Identify", "便利", "a magnifying eye over a glowing item", "pale silver"),
    "unlock": ("解錠", "Unlock", "便利", "a key turning in a glowing lock", "brass and gold"),
    "teleport": ("転移", "Teleport", "便利", "a swirling portal", "deep blue and violet"),
    "float": ("浮遊", "Feather Fall", "便利", "a feather drifting slowly down", "pale sky blue"),
    "wish": ("願い", "Wish", "便利", "a shining star with radiating wishes", "iridescent rainbow gold"),
}

# 術者のクラス: (日本語, 英語, 絵の題材, 色)
CLASSES = {
    "wizard": ("ウィザード", "Wizard", "an open spellbook with arcane runes", "deep blue"),
    "sorcerer": ("ソーサラー", "Sorcerer", "a hand wreathed in innate wild magic", "crimson and violet"),
    "warlock": ("ウォーロック", "Warlock", "an eldritch eye inside a pact sigil", "sickly green and black"),
    "bard": ("バード", "Bard", "a lute with magical musical notes", "warm magenta"),
    "cleric": ("クレリック", "Cleric", "a holy symbol radiating divine light", "white and gold"),
    "druid": ("ドルイド", "Druid", "an oak leaf and a wooden staff", "forest green"),
    "paladin": ("パラディン", "Paladin", "a sword before a sacred shield", "silver and gold"),
    "ranger": ("レンジャー", "Ranger", "a bow and arrow with a leaf", "olive green and brown"),
}

# 状態: (日本語, 英語, 絵の題材, 色)
CONDITIONS = {
    "blinded": ("盲目", "Blinded", "a crossed-out eye", "grey"),
    "charmed": ("魅了", "Charmed", "a heart with swirling enchantment", "pink"),
    "deafened": ("聴覚喪失", "Deafened", "a crossed-out ear", "grey"),
    "frightened": ("恐怖", "Frightened", "a terrified face with a looming shadow", "pale purple"),
    "grappled": ("組みつかれ", "Grappled", "a grabbing hand", "brown"),
    "incapacitated": ("無力", "Incapacitated", "a limp figure with spirals", "dull grey"),
    "invisible": ("不可視", "Invisible", "a faint dotted silhouette", "pale cyan"),
    "paralyzed": ("麻痺", "Paralyzed", "a figure frozen with crackling sparks", "yellow"),
    "petrified": ("石化", "Petrified", "a cracked stone face", "stone grey"),
    "poisoned": ("毒", "Poisoned", "a dripping poison drop", "green"),
    "prone": ("転倒", "Prone", "a figure knocked flat", "brown"),
    "restrained": ("拘束", "Restrained", "ropes and chains wrapping", "iron grey"),
    "stunned": ("朦朧", "Stunned", "stars circling a head", "yellow and white"),
    "unconscious": ("意識不明", "Unconscious", "a closed eye with Z marks of sleep", "dark blue"),
    "dead": ("死亡", "Dead", "a skull", "bone white and dark red"),
    "ashes": ("灰", "Ashes", "a small pile of ashes with a fading ember", "ash grey"),
}

STYLE = ("Painted in a dark fantasy style with a soft glow, {color} color scheme. One clear symbolic emblem centered, "
         "readable at small size. Square 256x256, transparent background, no text, no letters, no border frame.")


def spells():
    out = []
    for pack in sorted(CONTENT.iterdir()):
        f = pack / "spells.json"
        if f.exists():
            out.extend(json.loads(f.read_text(encoding="utf-8")))
    return [s for s in out if s.get("status") == "ok"]


def exists(folder, id):
    return any((p / "art" / folder / f"{id}.png").exists() for p in CONTENT.iterdir() if p.is_dir())


def rows():
    by_icon = defaultdict(list)
    by_class = defaultdict(list)
    for s in spells():
        by_icon[s.get("icon", "buff")].append(s)
        for c in s.get("classes", []):
            by_class[c].append(s)
    unknown = [k for k in by_icon if k not in SPELLS]
    if unknown:
        print("SPELLS に無い種類:", ", ".join(unknown))

    def examples(members):
        return "、".join(s["name"].get("ja", s["id"]) for s in sorted(members, key=lambda s: (s["level"], s["id"]))[:6])

    out = []
    for id, (ja, en, cat, motif, color) in SPELLS.items():
        out.append(("spells", id, ja, en, cat, motif, color, len(by_icon[id]), examples(by_icon[id]), "spell category icon"))
    for id, (ja, en, motif, color) in CLASSES.items():
        out.append(("classes", id, ja, en, "クラス", motif, color, len(by_class[id]), "", "character class emblem"))
    for id, (ja, en, motif, color) in CONDITIONS.items():
        out.append(("conditions", id, ja, en, "状態", motif, color, "", "", "status condition icon"))
    return out


def write_list():
    data = []
    for i, (folder, id, ja, en, cat, motif, color, count, ex, kind) in enumerate(rows(), 1):
        data.append({
            "no": i, "folder": folder, "id": id, "file": f"{folder}/{id}.png", "name_ja": ja, "name_en": en,
            "category": cat, "color": color, "motif": motif, "spells": count, "examples": ex,
            "done": "済" if exists(folder, id) else "",
            "prompt": f"Fantasy RPG {kind} \"{en}\": {motif}. " + STYLE.format(color=color),
        })
    OUT.parent.mkdir(exist_ok=True)
    with OUT.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(data[0].keys()))
        w.writeheader()
        w.writerows(data)
    print(f"{len(data)} 枚を書き出しました(呪文 {len(SPELLS)}、クラス {len(CLASSES)}、状態 {len(CONDITIONS)}): {OUT.relative_to(ROOT)}")


def check():
    all_rows = rows()
    missing = [(f, id, ja) for f, id, ja, *_ in all_rows if not exists(f, id)]
    print(f"アイコンあり {len(all_rows) - len(missing)} / {len(all_rows)} 枚")
    for f, id, ja in missing:
        print(f"  無い: {f}/{id}.png ({ja})")


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) >= 2 and sys.argv[1] == "--check":
        check()
    else:
        write_list()

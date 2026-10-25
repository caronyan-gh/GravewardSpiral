"""モンスターの画像の一覧を出す、置かれているかを確かめる、背景を抜いて透過 PNG にする。

使い方:
  python tools/art_list.py                 docs/art_list.csv を書き出す(CR の低い順。画像を作るときに渡す文の例つき)
  python tools/art_list.py --check         画像が置かれているかを数えて、足りないものを CR の低い順に出す
  python tools/art_list.py --key IN OUT    IN の背景色(左上の色。マゼンタか緑)を抜いて、透過 PNG を OUT に書く

画像の置き場所: Content/<パック>/art/monsters/<モンスターID>.png
仕様: 512x512(大きな敵は 768x512)、背景は単色のマゼンタ #FF00FF(紫や赤紫の敵は緑 #00FF00)、影や床は描かない。
読み込むときにゲームが背景を抜くので、透過 PNG にしなくてもよい。
"""
import csv
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTENT = ROOT / "Content"
OUT = ROOT / "docs" / "art_list.csv"

TYPE_JA = {
    "aberration": "異形", "beast": "獣", "celestial": "天上の存在", "construct": "人造の存在", "dragon": "竜",
    "elemental": "精霊", "fey": "妖精", "fiend": "魔物", "giant": "巨人", "humanoid": "人型", "monstrosity": "怪物",
    "ooze": "不定形", "plant": "植物", "undead": "アンデッド", "swarm": "群れ",
}
SIZE_JA = {"tiny": "とても小さい", "small": "小さい", "medium": "中くらい", "large": "大きい", "huge": "巨大", "gargantuan": "途方もなく巨大"}

# 体の色がマゼンタに近く、緑の背景にした方がよいもの
GREEN_BACK = {"purple_worm", "violet_fungus", "gibbering_mouther", "otyugh", "quasit", "nalfeshnee", "marilith", "succubus_incubus"}


def monsters():
    out = {}
    for pack in sorted(CONTENT.iterdir()):
        f = pack / "monsters.json"
        if f.exists():
            for m in json.loads(f.read_text(encoding="utf-8")):
                out[m["id"]] = (pack.name, m)
    return out


def art_path(pack, mid):
    return CONTENT / pack / "art" / "monsters" / f"{mid}.png"


def exists(mid):
    return any(art_path(p.name, mid).exists() for p in CONTENT.iterdir() if p.is_dir())


def visuals():
    f = ROOT / "tools" / "srd-source" / "monster_visuals.json"
    return json.loads(f.read_text(encoding="utf-8")) if f.exists() else {}


def priority(cr):
    """発注の優先度。1 は浅い階で出る CR 2 まで、2 は CR 8 まで、3 はそれより上。"""
    return 1 if cr <= 2 else 2 if cr <= 8 else 3


def write_list():
    vis = visuals()
    rows = []
    for mid, (pack, m) in sorted(monsters().items(), key=lambda kv: (kv[1][1]["cr"], kv[0])):
        if m.get("needs"):
            continue
        wide = m.get("art") == "wide"
        back = "#00FF00" if mid in GREEN_BACK else "#FF00FF"
        back_en = "solid green (#00FF00)" if mid in GREEN_BACK else "solid magenta (#FF00FF)"
        en = m["name"]["en"]
        ja = m["name"].get("ja", en)
        note = vis.get(mid, {})
        look = note.get("en", f"a {m['size']} {m['creature_type']}")
        prompt = (f"Classic dungeon RPG monster illustration, full color, dark fantasy. {en}: {look} "
                  f"Single creature, full body, front three-quarter view, centered with a little margin. "
                  f"Plain {back_en} background, no shadow, no floor, no text, no frame. {'768x512' if wide else '512x512'}.")
        rows.append({
            "no": len(rows) + 1,
            "priority": priority(m["cr"]),
            "id": mid,
            "file": f"{mid}.png",
            "name_ja": ja,
            "name_en": en,
            "type": TYPE_JA.get(m["creature_type"], m["creature_type"]),
            "size": SIZE_JA.get(m["size"], m["size"]),
            "cr": m["cr"],
            "image_size": "768x512" if wide else "512x512",
            "background": back,
            "notes_ja": note.get("ja", ""),
            "notes_en": note.get("en", ""),
            "done": "済" if exists(mid) else "",
            "prompt": prompt,
        })
    OUT.parent.mkdir(exist_ok=True)
    with OUT.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    missing = sum(1 for r in rows if not r["notes_ja"])
    print(f"{len(rows)} 体を書き出しました(見た目の説明が無いもの {missing} 体): {OUT.relative_to(ROOT)}")


def check():
    ms = [(m["cr"], mid, m["name"].get("ja", mid)) for mid, (_, m) in monsters().items() if not m.get("needs")]
    have = [x for x in ms if exists(x[1])]
    missing = sorted(x for x in ms if not exists(x[1]))
    print(f"画像あり {len(have)} / {len(ms)} 体")
    for cr, mid, ja in missing[:40]:
        print(f"  無い: CR {cr:<5} {mid} ({ja})")
    if len(missing) > 40:
        print(f"  ほか {len(missing) - 40} 体")


def key(src, dst):
    from PIL import Image
    im = Image.open(src).convert("RGBA")
    px = im.load()
    kr, kg, kb, ka = px[0, 0]
    magenta = kr > 180 and kb > 180 and kg < 90
    green = kg > 180 and kr < 90 and kb < 90
    if ka < 255 or not (magenta or green):
        print("背景が透過済みか、マゼンタ・緑ではありません。そのまま写します")
        im.save(dst)
        return
    inner, outer = 70.0, 140.0
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            d = ((r - kr) ** 2 + (g - kg) ** 2 + (b - kb) ** 2) ** 0.5
            t = min(1.0, max(0.0, (d - inner) / (outer - inner)))
            if t < 1.0:
                if magenta:
                    lim = max(g, int((r + b) / 2 * t))
                    r, b = min(r, lim), min(b, lim)
                else:
                    lim = max(r, b, int(g * t))
                    g = min(g, lim)
            px[x, y] = (r, g, b, int(a * t))
    im.save(dst)
    print(f"書き出しました: {dst}")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "--check":
        check()
    elif len(sys.argv) >= 4 and sys.argv[1] == "--key":
        key(sys.argv[2], sys.argv[3])
    else:
        write_list()

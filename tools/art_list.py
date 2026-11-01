"""モンスターの画像の一覧を出す、置かれているかを確かめる、背景を抜いて透過 PNG にする。

使い方:
  python tools/art_list.py                 docs/art_list.csv を書き出す(CR の低い順。画像を作るときに渡す文の例つき)
  python tools/art_list.py --check         画像が置かれているかを数えて、足りないものを CR の低い順に出す
  python tools/art_list.py --key IN OUT    IN の背景色(左上の色。マゼンタか緑)を抜いて、透過 PNG を OUT に書く
  python tools/art_list.py --unknown       正体が分かる前の敵の絵(種類ごとの 15 枚と、人型のうち亜人の 1 枚)の一覧 docs/unknown_art_list.csv と、
                                           発注書 docs/unknown_art_order.md を書き出す(--check はこの絵も数える)

画像の置き場所: Content/<パック>/art/monsters/<モンスターID>.png(正体が分かる前の絵は art/unknown/<種類>.png)
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
    "ooze": "不定形", "plant": "植物", "undead": "アンデッド", "swarm": "群れ", "demi_humanoid": "亜人",
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


# 正体が分かる前の敵の絵(種類ごと。ゲームは敵の大きさで描く大きさを変える): 種類 -> (見た目の英文, 見た目の日本語, 横長か, 緑の背景か)
UNKNOWN = {
    "humanoid": ("a person-shaped figure in a hooded cloak, a weapon half-hidden at its side; could be a bandit, a priest or a mage",
                 "フードと外套の人影。武器を半ば隠して持つ。山賊とも僧とも魔術師ともつかない", False, False),
    "beast": ("a four-legged animal crouching in the gloom, bristling fur and two glinting eyes; species impossible to tell",
              "暗がりに身を低くした四つ足の獣。逆立つ毛と光る二つの目。何の獣かは分からない", False, False),
    "monstrosity": ("a hunched unnatural creature with too many or oddly jointed limbs, mixing animal and something else",
                    "関節や手足の数がおかしい、獣ともつかない異様なものが背を丸めている", False, False),
    "undead": ("a gaunt, pale, shambling figure in rotting wrappings, a faint cold glow where the eyes should be",
               "朽ちた布をまとった、やせて青白い、よろめく人影。目のあるはずの所に冷たい光", False, False),
    "dragon": ("a great winged reptilian shape with wings half spread, long neck and tail, two ember-like eyes in the dark",
               "翼を半ば広げた大きな爬虫類の影。長い首と尾、闇に熾火のような二つの目", True, False),
    "swarm": ("a churning cloud of countless small dark shapes, wings, legs and tails blurring together",
              "無数の小さな影が渦を巻く塊。翼とも脚とも尾ともつかない", False, False),
    "fiend": ("a horned figure wreathed in smoke, faint red embers glowing through it, claws and a tail half seen",
              "煙に包まれた角のある影。赤い残り火が透け、爪と尾が半ば見える", False, True),
    "elemental": ("a swirling mass of raw element, part stone, part flame, part mist and water, roughly in the shape of a body",
                  "石とも炎とも霧とも水ともつかない、むき出しの元素が渦巻いて、なんとなく体の形をとる", False, False),
    "giant": ("a towering humanoid figure, broad shoulders and a huge club, its head lost in darkness above",
              "見上げるほど大きな人の形。広い肩と大きな棍棒。頭は上の闇に消えている", False, False),
    "fey": ("a slender, graceful figure with a few glimmering motes of light around it, its face hidden",
            "ほっそりとしなやかな人影。まわりに小さな光の粒がきらめく。顔は見えない", False, False),
    "construct": ("a stiff, heavy figure of stone or metal with seams and rivets, a blank featureless face",
                  "石か金属のこわばった重い人形。継ぎ目と鋲があり、顔には何もない", False, False),
    "celestial": ("a tall figure in pale haze with the suggestion of great wings, its face lost in a soft glare",
                  "淡いもやの中の背の高い人影。大きな翼があるようにも見え、顔はやわらかな光でかき消える", False, False),
    "aberration": ("a bulbous mass with writhing tentacles and many small eyes glinting in the dark",
                   "ふくれた塊。のたうつ触手と、闇に光るいくつもの小さな目", False, True),
    "ooze": ("an amorphous glistening blob, half-transparent, with shapes of things dissolving inside it",
             "形の定まらない、ぬらぬら光る塊。半ば透けて、中で何かが溶けている", False, False),
    "plant": ("a tangled mass of vines, roots, branches and fungus in a rough upright shape",
              "つるや根や枝やきのこがもつれて、なんとなく立った形になった塊", False, False),
    "demi_humanoid": ("a hunched, not-quite-human figure with a heavy brow, pointed ears or jutting tusks, crude scraps of armor and a jagged blade; "
                      "could be a goblin, an orc, a gnoll or a lizard-man",
                      "人に似ているが人ではない、背を丸めた人影。張り出した眉、とがった耳か突き出た牙、粗末な鎧の切れ端とぎざぎざの刃。"
                      "ゴブリンともオークともノールともトカゲ人ともつかない", False, False),
}


def unknown_rows():
    rows = []
    for ctype, (en, ja, wide, green) in UNKNOWN.items():
        back = "#00FF00" if green else "#FF00FF"
        back_en = "solid green (#00FF00)" if green else "solid magenta (#FF00FF)"
        prompt = (f"Dark fantasy dungeon RPG illustration of an unidentified creature: {en}. "
                  f"Draw it as a near-black silhouette with almost no inner detail, only faint texture, wrapped in a thin blue-gray haze "
                  f"that makes the outline glow softly; one or two small accents (glowing eyes, embers or motes) are fine. "
                  f"Only the overall shape and posture tell what kind of creature it is; its exact identity must stay hidden. "
                  f"Single figure, full body, centered with a small margin. Transparent background (or plain {back_en} to be keyed out), "
                  f"no floor, no cast shadow, no text, no frame. {'768x512' if wide else '512x512'}.")
        rows.append({
            "no": len(rows) + 1, "id": ctype, "file": f"{ctype}.png", "type": TYPE_JA.get(ctype, ctype),
            "image_size": "768x512" if wide else "512x512", "background": back, "look_ja": ja, "look_en": en,
            "done": "済" if unknown_exists(ctype) else "", "prompt": prompt,
        })
    return rows


def unknown_exists(ctype):
    return any((p / "art" / "unknown" / f"{ctype}.png").exists() for p in CONTENT.iterdir() if p.is_dir())


def write_unknown():
    rows = unknown_rows()
    out = ROOT / "docs" / "unknown_art_list.csv"
    with out.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    lines = [
        "# 正体が分かる前の敵の絵の発注書",
        "",
        "このファイルは `python tools/art_list.py --unknown` が書き出したもの。直すときは tools/art_list.py の UNKNOWN を直す。",
        "",
        "## 何に使う絵か",
        "",
        "戦闘で、敵の正体が分からないあいだ(「中くらいの人型」「大きな怪物」のような名前で出ているあいだ)は、本物の絵の代わりにこの絵を出す。種類ごとに 1 枚で、同じ種類の敵はみな同じ絵を使う(Wizardry の使い回しの絵のように、絵を見ても正体までは分からない)。正体が分かると本物の絵に変わる。ゲームが敵の大きさで描く大きさを変えるので、大きさ違いの絵は要らない。",
        "",
        "## 共通の決まり",
        "",
        "- ほぼ真っ黒の影絵にする。中の描き込みはほとんど無く、うっすら質感が分かる程度。まわりに薄い青灰色のもやをまとわせて、輪郭がほんのり光るようにする(ゲームの戦闘の枠はほぼ黒なので、縁が光らないと溶けてしまう)。",
        "- 光る目・残り火・光の粒のような小さな差し色は 1〜2 か所までならよい。",
        "- 形と姿勢で種類だけが分かり、何の敵かまでは分からないこと。明るさ・もやの濃さ・描き込みの量を、ほかの絵とそろえる。",
        "- 見本は、最初に届いた 15 枚(Content/srd/art/unknown/)。足すときは、これと並べて違和感が無いようにする。",
        "- 1 体だけ、全身、真ん中に少し余白を残して描く。",
        "- 背景は透過にする。単色で描くなら、マゼンタ(#FF00FF)で、魔物と異形だけは緑(#00FF00)。`python tools/art_list.py --key` で抜ける。床・落ちる影・文字・枠は描かない。",
        "- 大きさは 512x512(竜だけ 768x512)。",
        "- 置き場所は `Content/srd/art/unknown/<ファイル名>`。",
        "",
        "## 頼む絵の一覧",
        "",
        "頼む絵を以下に示す。",
        "",
        "| No | ファイル名 | 種類 | 大きさ | 背景 |",
        "|---|---|---|---|---|",
    ]
    for r in rows:
        lines.append(f"| {r['no']} | `{r['file']}` | {r['type']} | {r['image_size']} | {r['background']} |")
    lines += ["", "## それぞれの絵の頼み方", "", "そのまま貼って頼める英文を、絵ごとに付ける。"]
    for r in rows:
        lines += ["", f"### {r['type']}(`{r['file']}`)", "", f"見た目: {r['look_ja']}。", "", r["prompt"]]
    (ROOT / "docs" / "unknown_art_order.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    have = sum(1 for r in rows if r["done"])
    print(f"正体が分かる前の絵 {len(rows)} 枚(置いてあるもの {have} 枚): docs/unknown_art_list.csv、docs/unknown_art_order.md")


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
    lacking = [c for c in UNKNOWN if not unknown_exists(c)]
    print(f"正体が分かる前の絵 {len(UNKNOWN) - len(lacking)} / {len(UNKNOWN)} 枚" + (f"(無い: {', '.join(lacking)})" if lacking else ""))


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
    elif len(sys.argv) >= 2 and sys.argv[1] == "--unknown":
        write_unknown()
    elif len(sys.argv) >= 4 and sys.argv[1] == "--key":
        key(sys.argv[2], sys.argv[3])
    else:
        write_list()

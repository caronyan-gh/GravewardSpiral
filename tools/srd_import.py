"""SRD 5.1 のモンスター・呪文・武器と鎧を、5e-database の JSON から標準のパックの monsters.json・spells.json・items.json に変換する。

使い方: python tools/srd_import.py
  入力: tools/srd-source/5e-SRD-Monsters.json、5e-SRD-Spells.json(5e-bits/5e-database、版は README の通り)
        tools/srd-source/monsters_ja.json、spells_ja.json(日本語名。自分で訳したもの)
        tools/srd-source/spells_overrides.json(自動で変換できない呪文を手で書いたもの。変換の結果に上書きする)
        tools/srd-source/5e-SRD-Equipment.json、items_ja.json(武器・鎧・盾と日本語名)
        tools/srd-source/items_extra.json、magic_items.json、uniques.json、event_items.json(袋・道具・水薬・魔法のアイテム・固有のレア品・イベントでしか手に入らない品。手で書いたもの。同じ id は後ろが勝つ)
  出力: Content/srd/monsters.json、Content/srd/spells.json、Content/srd/items.json

説明文 (desc) は写さない。命中したときの追加のセーヴや状態は、説明文の決まった言い回しから読み取る。
読み取れなかった行動は unsupported に名前だけ残し、戦闘では使わない。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "tools" / "srd-source"
OUT = ROOT / "Content" / "srd" / "monsters.json"
SPELLS_OUT = ROOT / "Content" / "srd" / "spells.json"
ITEMS_OUT = ROOT / "Content" / "srd" / "items.json"

ABILITY = {"strength": "str", "dexterity": "dex", "constitution": "con",
           "intelligence": "int", "wisdom": "wis", "charisma": "cha"}

CONDITIONS = ["blinded", "charmed", "deafened", "frightened", "grappled", "incapacitated", "invisible",
              "paralyzed", "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious"]

# 行動の名前の日本語(よく出るものだけ。無ければ英語のまま)
ACTION_JA = {
    "Bite": "噛みつき", "Bites": "噛みつき", "Claws": "かぎ爪", "Claw": "かぎ爪", "Hooves": "蹄", "Beak": "くちばし",
    "Slam": "叩きつけ", "Shortsword": "ショートソード", "Spear": "槍", "Longbow": "ロングボウ", "Scimitar": "シミター",
    "Greataxe": "グレートアックス", "Talons": "鉤爪", "Gore": "角の突き", "Ram": "体当たり", "Longsword": "ロングソード",
    "Pseudopod": "偽足", "Javelin": "投げ槍", "Club": "棍棒", "Dagger": "ダガー", "Tentacles": "触手", "Tentacle": "触手",
    "Hand Crossbow": "ハンドクロスボウ", "Sting": "針", "Shortbow": "ショートボウ", "Heavy Crossbow": "ヘビークロスボウ",
    "Light Crossbow": "ライトクロスボウ", "Fire Breath": "炎の息", "Cold Breath": "冷気の息", "Acid Breath": "酸の息",
    "Lightning Breath": "雷の息", "Poison Breath": "毒の息", "Steam Breath": "蒸気の息", "Frost Breath": "霜の息",
    "Tail": "尾", "Morningstar": "モーニングスター", "Constrict": "締めつけ", "Life Drain": "生命吸収", "Fist": "拳",
    "Rock": "投石", "Tusk": "牙", "Tusks": "牙", "War Pick": "ウォーピック", "Stomp": "踏みつけ", "Web": "蜘蛛の糸",
    "Touch": "接触", "Greatclub": "大棍棒", "Mace": "メイス", "Maul": "モール", "Rake": "引っかき", "Warhammer": "ウォーハンマー",
    "Glaive": "グレイヴ", "Pike": "パイク", "Pincer": "はさみ", "Quarterstaff": "クォータースタッフ", "Battleaxe": "バトルアックス",
    "Greatsword": "グレートソード", "Sling": "スリング", "Rapier": "レイピア", "Horn": "角", "Beaks": "くちばし",
    "Halberd": "ハルバード", "Trident": "トライデント", "Lance": "ランス", "Wing": "翼", "Wing Attack": "翼の一撃",
    "Acid Spray": "酸のしぶき", "Withering Touch": "枯らす接触", "Rotting Fist": "腐る拳", "Rotting Touch": "腐る接触",
    "Strength Drain": "筋力吸収", "Blood Drain": "吸血", "Shock": "電撃", "Engulf": "飲み込み", "Swallow": "丸呑み",
    "Harpoon": "銛", "Shield Bash": "盾の打撃", "Spiked Shield": "棘の盾", "Tail Spike": "尾の棘", "Antennae": "触角",
    "Tendril": "蔓", "Poisoned Dart": "毒矢", "Breath Weapons": "息", "Petrifying Breath": "石化の息",
    "Blinding Breath": "目くらましの息", "Paralyzing Breath": "麻痺の息", "Hurl Flame": "炎を投げる", "Crush": "押しつぶし", "Smother": "窒息",
}

# 正体が分かる前の名前(種類ごと)
UNKNOWN_JA = {
    "aberration": "異形", "beast": "獣", "celestial": "輝く者", "construct": "動く像", "dragon": "竜",
    "elemental": "精霊", "fey": "妖精", "fiend": "魔物", "giant": "巨人", "humanoid": "人影", "monstrosity": "怪物",
    "ooze": "不定形の物", "plant": "植物", "undead": "不死の者", "swarm": "群れ",
}
SIZE_JA = {"tiny": "ちいさな", "small": "小さな", "medium": "", "large": "大きな", "huge": "巨大な", "gargantuan": "途方もない"}


def snake(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def feet(text, default=0):
    m = re.match(r"(\d+)", text or "")
    return int(m.group(1)) if m else default


def damage_list(entries):
    out = []
    for d in entries or []:
        if "choose" in d:   # 形ごとに違うダメージなど。最初のものを使う
            opts = d.get("from", {}).get("options", [])
            d = opts[0] if opts else {}
            if "items" in d:
                d = d["items"][0]
        dice = d.get("damage_dice")
        dtype = d.get("damage_type", {}).get("index")
        if dice and dtype:
            out.append({"type": "damage", "dice": dice.replace(" ", ""), "damage_type": dtype})
    return out


DURATION = [
    (r"until the end of (?:the target's|its) next turn", 1),
    (r"for 1 minute", 10), (r"for 1 hour", 600), (r"for 24 hours", 14400),
]


def duration(text):
    for pat, rounds in DURATION:
        if re.search(pat, text):
            return rounds
    return None


def conditions_in(text):
    """「or be poisoned」「or be knocked prone」など、失敗したときの状態。"""
    found = []
    for m in re.finditer(r"(?:or be|or become|is|be) (?:knocked )?(" + "|".join(CONDITIONS) + r")\b", text):
        c = m.group(1)
        if c not in found:
            found.append(c)
    return found


def condition_parts(conds, text):
    parts = []
    rounds = duration(text)
    repeat = bool(re.search(r"repeat the saving throw at the end of each of its turns", text))
    for c in conds:
        p = {"type": "condition", "condition": c}
        if c == "prone":
            pass   # 次の自分の番で起き上がる(エンジンが処理する)
        elif rounds:
            p["rounds"] = rounds
        if repeat:
            p["repeat_save"] = True
        esc = re.search(r"escape DC (\d+)", text)
        if esc and c in ("grappled", "restrained"):
            p["escape_dc"] = int(esc.group(1))
        parts.append(p)
    return parts


SAVE_RE = re.compile(r"DC (\d+) (Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) saving throw")
FAIL_DMG_RE = re.compile(r"taking (?:\d+ )?\((\d+d\d+(?: ?[+-] ?\d+)?)\) (\w+) damage")


def save_part(text, dc_value=None, ability=None, half=None, base_damage=()):
    m = SAVE_RE.search(text)
    if not m and dc_value is None:
        return None
    dc = dc_value if dc_value is not None else int(m.group(1))
    ab = ability or ABILITY[m.group(2).lower()]
    fail = list(base_damage)
    if not fail:
        for dm in FAIL_DMG_RE.finditer(text):
            fail.append({"type": "damage", "dice": dm.group(1).replace(" ", ""), "damage_type": dm.group(2).lower()})
    tail = text[m.end():] if m else text
    # 「HP が 0 になったら麻痺する」のような条件つきの文は読まない
    tail = " ".join(x for x in re.split(r"(?<=\.) ", tail) if "0 hit points" not in x)
    fail += condition_parts(conditions_in(tail), tail)
    if not fail:
        return None
    if half is None:
        half = "half as much" in text
    p = {"type": "save", "ability": ab, "dc": dc, "fail": fail}
    if half and any(f["type"] == "damage" for f in fail):
        p["half"] = True
    return p


def area_target(text):
    m = re.search(r"(\d+)-foot(?:-radius)? (cone|line|cube|sphere|radius)", text)
    if not m:
        return "one"
    return "row" if int(m.group(1)) <= 30 else "party"


def convert_action(a):
    desc = a.get("desc", "")
    name = a["name"]
    act = {"id": snake(name), "name": {"en": name}}
    if name in ACTION_JA:
        act["name"]["ja"] = ACTION_JA[name]
    usage = a.get("usage") or {}
    # 金属の竜の「Breath Weapons」は2つの息から選ぶ形。読める息を、再使用を分け合う組(breath)の行動にする
    # (麻痺させる息は読める。弱らせる・眠らせる・押し返す・遅くする息は、このゲームの状態で表せないので読まない)
    breaths = (a.get("options") or {}).get("from", {}).get("options", [])
    if breaths and all(o.get("option_type") == "breath" for o in breaths):
        out = []
        for opt in breaths:
            line = next((l for l in desc.split("\n") if l.startswith(opt["name"] + ".")), desc)
            got = convert_action({"name": opt["name"], "desc": line, "usage": usage, "dc": opt["dc"], "damage": opt.get("damage")})
            if got:
                got["recharge_group"] = "breath"
                out.append(got)
        return out or None
    if usage.get("type") == "recharge on roll":
        act["recharge"] = usage.get("min_value", 6)
    elif usage.get("type") == "per day":
        act["uses"] = usage.get("times", 1)
    elif usage.get("type") == "recharge after rest":
        act["uses"] = 1

    if "attack_bonus" in a:
        if desc.startswith("Melee or Ranged"):
            act["range"] = "any"
        elif desc.startswith("Ranged"):
            act["range"] = "ranged"
        else:
            act["range"] = "melee"
        act["to_hit"] = a["attack_bonus"]
        hit = desc.split("Hit:", 1)[1] if "Hit:" in desc else ""
        effects = damage_list(a.get("damage"))
        s = save_part(hit) if SAVE_RE.search(hit) else None
        if s:
            effects.append(s)
        else:
            # 命中で必ずかかる状態(つかまれる、糸で拘束される、倒される)
            m = re.search(r"the target is (grappled|restrained|knocked prone|blinded|poisoned)", hit)
            if m:
                c = m.group(1).replace("knocked ", "")
                effects += condition_parts([c], hit)
        if not effects:
            return None
        act["target"] = "one"
        act["effects"] = effects
        return act

    if "dc" in a:
        dc = a["dc"]
        base = damage_list(a.get("damage"))
        s = save_part(desc, dc["dc_value"], dc["dc_type"]["index"], dc.get("success_type") == "half", base)
        if not s:
            return None
        act["range"] = "any"
        act["target"] = area_target(desc)
        act["effects"] = [s]
        return act
    return None


def multiattack(a, ids):
    def piece(items):
        out = []
        for it in items:
            aid = snake(it["action_name"])
            if aid in ids:
                n = str(it.get("count", 1))
                out.append({"action": aid, "count": int(n) if n.isdigit() else 5})   # ヒドラの「頭の数」は最初の 5
        return out

    opts = []
    if a.get("multiattack_type") == "actions":
        opts.append(piece(a.get("actions", [])))
    elif a.get("multiattack_type") == "action_options":
        for o in a["action_options"]["from"]["options"]:
            opts.append(piece(o["items"] if o.get("option_type") == "multiple" else [o]))
    return [o for o in opts if o]


def resistances(lst):
    out = []
    for r in lst:
        if "nonmagical" in r:
            out.append("nonmagical_physical")
        elif r in ("damage from spells",):
            continue
        else:
            out += [x.strip() for x in re.split(r",|and", r) if x.strip()]
    return sorted(set(out))


def appearing(cr):
    if cr <= 0.125:
        return "2d4"
    if cr <= 0.5:
        return "1d6"
    if cr <= 2:
        return "1d4"
    if cr <= 5:
        return "1d2"
    return "1"


def convert(m, ja):
    cr = m["challenge_rating"]
    mon = {
        "id": m["index"].replace("-", "_"),
        "name": {"en": m["name"], "ja": ja.get(m["index"], m["name"])},
        "size": m["size"].lower(),
        "creature_type": m["type"].split()[0].lower() if m["type"] else "monstrosity",
        "cr": cr,
        "xp": m["xp"],
        "proficiency": m.get("proficiency_bonus", 2),
        "ac": m["armor_class"][0]["value"],
        "hp": m.get("hit_points_roll") or m["hit_dice"],
        "hp_average": m["hit_points"],
        "speed": feet(m["speed"].get("walk")),
        "flies": "fly" in m["speed"],
        "abilities": {ABILITY[k]: m[k] for k in ABILITY},
        "passive_perception": m["senses"].get("passive_perception", 10),
        "appearing": appearing(cr),
    }
    if "swarm" in m["type"]:
        mon["creature_type"] = "swarm"
    t = mon["creature_type"]
    mon["unknown_name"] = {"en": f"{m['size'].lower()} {t}", "ja": SIZE_JA[mon["size"]] + UNKNOWN_JA.get(t, "怪物")}
    saves, skills = {}, {}
    for p in m.get("proficiencies", []):
        idx = p["proficiency"]["index"]
        if idx.startswith("saving-throw-"):
            saves[idx.removeprefix("saving-throw-")] = p["value"]
        elif idx.startswith("skill-"):
            skills[idx.removeprefix("skill-").replace("-", "_")] = p["value"]
    if saves:
        mon["saves"] = saves
    if skills:
        mon["skills"] = skills
    for key, src in (("resist", "damage_resistances"), ("vulnerable", "damage_vulnerabilities"), ("immune", "damage_immunities")):
        r = resistances(m.get(src, []))
        if r:
            mon[key] = r
    ci = [c["index"] for c in m.get("condition_immunities", []) if c["index"] in CONDITIONS]   # 消耗は状態の一覧に無い
    if ci:
        mon["condition_immune"] = ci
    traits = [snake(s["name"]) for s in m.get("special_abilities", [])]
    if traits:
        mon["traits"] = traits
    # 呪文を使う(スロットのある Spellcasting だけ。生まれつきの呪文は、まだ入れない)
    for sa in m.get("special_abilities", []):
        sc = sa.get("spellcasting")
        if sc and sc.get("slots"):
            ab = sc["ability"]["index"]
            mon["spellcasting"] = {
                "level": sc.get("level", 1), "ability": ab, "dc": sc.get("dc", 10), "attack": sc.get("modifier", 0),
                "slots": {k: v for k, v in sc["slots"].items()},
                "spells": [x["url"].rsplit("/", 1)[-1].replace("-", "_") for x in sc.get("spells", [])],
            }
            break

    actions, unsupported, multi = [], [], None
    for a in m.get("actions", []):
        if a["name"] == "Multiattack":
            multi = a
            continue
        c = convert_action(a)
        if isinstance(c, list):   # 選べる息は、いくつかの行動になる
            actions.extend(c)
        elif c:
            actions.append(c)
        else:
            unsupported.append(snake(a["name"]))
    mon["actions"] = actions
    if multi:
        ma = multiattack(multi, {x["id"] for x in actions})
        if ma:
            mon["multiattack"] = ma
    if unsupported:
        mon["unsupported"] = unsupported
    needs = []
    if not actions:
        needs.append("spells" if any("Spellcasting" in s["name"] for s in m.get("special_abilities", [])) else "special")
    if m.get("legendary_actions"):
        mon["legendary"] = True
    if needs:
        mon["needs"] = needs
    big = mon["size"] in ("huge", "gargantuan") or mon["creature_type"] == "dragon" and mon["size"] != "medium"
    mon["art"] = "wide" if big else "square"
    return mon


def main():
    src = json.loads((SRC / "5e-SRD-Monsters.json").read_text(encoding="utf-8"))
    ja = json.loads((SRC / "monsters_ja.json").read_text(encoding="utf-8"))
    missing = [m["index"] for m in src if m["index"] not in ja]
    if missing:
        print("日本語名がありません:", ", ".join(missing))
        return 1
    mons = sorted((convert(m, ja) for m in src), key=lambda x: (x["cr"], x["id"]))
    OUT.write_text(json.dumps(mons, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    usable = [m for m in mons if not m.get("needs")]
    uns = sum(len(m.get("unsupported", [])) for m in mons)
    print(f"{len(mons)} 体を書き出しました(戦闘で使える {len(usable)} 体、使えない行動 {uns} 個): {OUT.relative_to(ROOT)}")
    for m in mons:
        if m.get("needs"):
            print(f"  使えない: {m['id']} ({', '.join(m['needs'])})")
    r = convert_spells()
    return r or convert_items()


# ---------------- 武器・鎧・魔法のアイテム ----------------

ITEM_ID = {"crossbow-light": "light_crossbow", "crossbow-hand": "hand_crossbow", "crossbow-heavy": "heavy_crossbow",
           "studded-leather-armor": "studded_leather"}
UNIT = {"cp": 0.01, "sp": 0.1, "ep": 0.5, "gp": 1, "pp": 10}


def item_id(index):
    return ITEM_ID.get(index, index.replace("-", "_"))


def convert_equipment(x, ja):
    iid = item_id(x["index"])
    cost = x.get("cost") or {}
    out = {"id": iid, "name": {"en": x["name"], "ja": ja.get(iid, x["name"])},
           "price": round(cost.get("quantity", 0) * UNIT.get(cost.get("unit", "gp"), 1), 2)}
    cat = x["equipment_category"]["index"]
    if cat == "weapon":
        out["kind"] = "weapon"
        out["category"] = x["weapon_category"].lower()
        if x.get("weapon_range") == "Ranged":
            out["ranged"] = True
        dmg = x.get("damage") or {}
        if dmg:
            out["damage"] = dmg["damage_dice"]
            out["damage_type"] = dmg["damage_type"]["index"]
        if x.get("two_handed_damage"):
            out["versatile_damage"] = x["two_handed_damage"]["damage_dice"]
        out["properties"] = [p["index"].replace("-", "_") for p in x.get("properties", []) if p["index"] != "monk"]
        if "thrown" in out["properties"]:
            out["stack"] = 10   # 投げる武器は重ねて持てる(1戦闘で投げられるのは持っている本数まで)
    elif x["index"] == "shield":
        out["kind"] = "shield"
        out["armor_class"] = x["armor_class"]["base"]
    else:
        out["kind"] = "armor"
        out["armor_type"] = x["armor_category"].lower()
        ac = x["armor_class"]
        out["armor_class"] = ac["base"]
        if ac.get("dex_bonus") is False:
            out["dex_cap"] = 0
        elif ac.get("max_bonus") is not None:
            out["dex_cap"] = ac["max_bonus"]
        if x.get("str_minimum"):
            out["strength_required"] = x["str_minimum"]
        if x.get("stealth_disadvantage"):
            out["stealth_disadvantage"] = True
    return out


def convert_items():
    src = json.loads((SRC / "5e-SRD-Equipment.json").read_text(encoding="utf-8"))
    ja = json.loads((SRC / "items_ja.json").read_text(encoding="utf-8"))
    items = {}
    for x in src:
        if x["equipment_category"]["index"] in ("weapon", "armor") and x["index"] not in ("net",):
            it = convert_equipment(x, ja)
            items[it["id"]] = it
    missing = [k for k in items if k not in ja]
    if missing:
        print("武器・鎧の日本語名がありません:", ", ".join(missing))
        return 1
    for name in ("items_extra.json", "magic_items.json", "uniques.json", "event_items.json"):
        f = SRC / name
        if f.exists():
            for it in json.loads(f.read_text(encoding="utf-8")):
                items[it["id"]] = it
    order = {"weapon": 0, "armor": 1, "shield": 2, "accessory": 3, "backpack": 4, "consumable": 5, "gear": 6, "key": 7}
    out = sorted(items.values(), key=lambda x: (order.get(x["kind"], 9), x.get("rarity", "mundane") != "mundane", x["id"]))
    ITEMS_OUT.write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    magic = sum(1 for x in out if x.get("rarity", "mundane") != "mundane")
    print(f"アイテム {len(out)} 個を書き出しました(魔法のアイテム {magic} 個): {ITEMS_OUT.relative_to(ROOT)}")
    return 0


# ---------------- 呪文 ----------------

TIME = {"1 action": "action", "1 bonus action": "bonus", "1 reaction": "reaction"}
DURATION_ROUNDS = {
    "Instantaneous": 0, "1 round": 1, "1 minute": 10, "Up to 1 minute": 10, "10 minutes": 100, "Up to 10 minutes": 100,
    "1 hour": 600, "Up to 1 hour": 600, "8 hours": 4800, "Up to 8 hours": 4800, "24 hours": 14400, "Up to 24 hours": 14400,
}


def by_level(table):
    """{"1": "3d4 + 3", ...} を {"1": "3d4+3"} に。"""
    # 「4d6 OR 5d6」のような書き方は、後ろ(上のスロットで増える方)を使う
    return {k: v.replace(" ", "").split("OR")[-1] for k, v in table.items()}


def first(table):
    return table[min(table, key=int)]


def convert_spell(sp, ja, overrides):
    sid = sp["index"]
    out = {
        "id": sid.replace("-", "_"),
        "name": {"en": sp["name"], "ja": ja.get(sid, sp["name"])},
        "level": sp["level"],
        "school": sp["school"]["index"],
        "classes": [c["index"] for c in sp.get("classes", [])],
        "ritual": sp.get("ritual", False),
        "concentration": sp.get("concentration", False),
        "time": TIME.get(sp["casting_time"], "long"),
        "rounds": DURATION_ROUNDS.get(sp["duration"], 600),
    }
    status = "unsupported"
    effects = []
    target = None
    attack = sp.get("attack_type")
    dmg = sp.get("damage") or []
    if dmg and "damage_type" in dmg[0]:
        d = dmg[0]
        if "damage_at_slot_level" in d:
            table, out["scale"] = by_level(d["damage_at_slot_level"]), "slot"
        elif "damage_at_character_level" in d:
            table, out["scale"] = by_level(d["damage_at_character_level"]), "character"
        else:
            table = None
        if table:
            part = {"type": "damage", "dice": first(table), "damage_type": d["damage_type"]["index"], "by_level": table}
            area = (sp.get("area_of_effect") or {}).get("type")
            if "dc" in sp:
                save = {"type": "save", "ability": sp["dc"]["dc_type"]["index"], "fail": [part]}
                if sp["dc"].get("dc_success") == "half":
                    save["half"] = True
                effects = [save]
                target = "two_groups" if area == "line" else "group" if area else "enemy"
            else:
                effects = [part]
                target = "group" if area else "enemy"
            status = "ok"
    elif "heal_at_slot_level" in sp:
        table = by_level(sp["heal_at_slot_level"])
        out["scale"] = "slot"
        effects = [{"type": "heal", "dice": first(table), "by_level": table}]
        target = "allies" if sp["name"].startswith("Mass") or "prayer" in sid else "ally"
        status = "ok"
        out["use"] = ["combat", "camp"]
    if attack:
        out["attack"] = attack
    if target:
        out["target"] = target
    if out["time"] == "long":   # 1分以上かかる呪文は、戦闘では唱えない
        out["use"] = ["camp"]
    out["effects"] = effects
    out["status"] = status
    if sid in overrides:
        ov = dict(overrides[sid])
        out.update(ov)
    out.setdefault("use", ["combat"])
    if out["status"] == "ok" and not out.get("target"):
        out["status"] = "unsupported"
    out.setdefault("icon", spell_icon(out))
    return out


ENEMY_TARGETS = {"enemy", "group", "two_groups", "all_enemies", "front_row"}
PHYSICAL = {"slashing", "piercing", "bludgeoning"}


def spell_icon(sp):
    """呪文のアイコンの種類(ジャンル)。上書き(spells_overrides.json の icon)が無ければ効果から決める。
    攻撃: 火・冷気・電撃・雷鳴・酸・毒・死霊・光輝・力場・精神・物理。支援: 回復・蘇生・浄化・強化・防御。
    妨害: 弱体・即死。便利: 明かり・鑑定・解錠・転移・浮遊・願い。"""
    def flat(parts):
        for p in parts:
            yield p
            for k in ("fail", "success"):
                if isinstance(p.get(k), list):
                    yield from flat(p[k])
    parts = list(flat(sp.get("effects", [])))
    types = {p.get("type") for p in parts}
    for t, icon in (("wish", "wish"), ("revive", "revive"), ("kill", "death"), ("light", "light"), ("identify", "identify"),
                    ("knock", "unlock"), ("recall", "teleport"), ("feather_fall", "float")):
        if t in types:
            return icon
    if "heal" in types or "stabilize" in types:
        return "heal"
    dmg = [p.get("damage_type") for p in parts if p.get("type") == "damage"]
    if dmg:
        return "physical" if dmg[0] in PHYSICAL else dmg[0]
    if sp.get("target") in ENEMY_TARGETS:
        return "debuff"
    if types & {"remove_condition", "remove_curse"}:
        return "cure"
    if "resist" in types or any(p.get("type") == "modifier" and p.get("stat") == "ac" for p in parts):
        return "protect"
    return "buff"


def convert_spells():
    src = json.loads((SRC / "5e-SRD-Spells.json").read_text(encoding="utf-8"))
    ja = json.loads((SRC / "spells_ja.json").read_text(encoding="utf-8"))
    overrides = {k: v for k, v in json.loads((SRC / "spells_overrides.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}
    missing = [x["index"] for x in src if x["index"] not in ja]
    if missing:
        print("呪文の日本語名がありません:", ", ".join(missing))
        return 1
    unknown = [k for k in overrides if k not in {x["index"] for x in src}]
    if unknown:
        print("上書きの対象が SRD にありません:", ", ".join(unknown))
    spells = sorted((convert_spell(x, ja, overrides) for x in src), key=lambda x: (x["level"], x["id"]))
    SPELLS_OUT.write_text(json.dumps(spells, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    from collections import Counter
    c = Counter(x["status"] for x in spells)
    print(f"呪文 {len(spells)} 個を書き出しました({dict(c)}): {SPELLS_OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

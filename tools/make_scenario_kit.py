"""シナリオを作る人(とそのエージェント)向けの scenario-kit を組み立てる。

使い方: python tools/make_scenario_kit.py <出力フォルダ>
  - scenario-kit/ の手で書いた文書(README.md など)と docs/class-pack-guide.md を写す。
  - reference/ に、ソースの説明(/// のコメント)から JSON の項目の一覧を書き出す。ソースが正本なので、手で写さない
    (インストーラーを作るたびに作り直すので、ゲームの版とずれない)。
  - 部品の種類の一覧(イベントの手順・条件、効果、能力の効果)は、ScenarioCheck.exe reference が今の版から出す。
make_installer.py が out/publish/scenario-kit に作る。
"""
import html
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KIT = ROOT / "scenario-kit"
CORE = ROOT / "src" / "GravewardSpiral.Core"

# 書き出す形式: (出力の名前, 題, ソース, 載せるクラス(None なら全部))。
# 中の項目の型が別のファイルにあるときは、EXTRA でそのファイルも同じページに足す(worlds.json の text など)
REFERENCES = [
    ("manifest", "manifest.json(パックの表書き)", "Content/ContentLibrary.cs", ["PackManifest"]),
    ("maps", "maps/*.txt(手作りの迷宮の階)", "Maze/MazeTextFormat.cs", ["MazeTextFormat"]),
    ("dungeons", "dungeons.json(迷宮の階の並び・エレベーター)", "Maze/DungeonLoader.cs", None),
    ("events", "events.json(イベント)", "Events/EventDef.cs", None),
    ("rumors", "rumors.json(酒場の噂)", "Events/RumorDef.cs", None),
    ("encounters", "encounters.json(出る敵・レベル帯・部屋の戦い)", "Combat/Encounters.cs", None),
    ("bosses", "bosses.json(ボス)", "Monsters/BossDef.cs", None),
    ("named_monsters", "named_monsters.json(名のある敵)", "Monsters/NamedMonsterDef.cs", None),
    ("monsters", "monsters.json(モンスター)", "Monsters/MonsterDef.cs", None),
    ("effects", "効果(呪文・品・モンスターの行動の effects)", "Effects/Effects.cs", None),
    ("spells", "spells.json(呪文。シナリオでは名前だけ変えられる)", "Magic/Spells.cs", None),
    ("classes", "classes.json・races.json・features.json など(クラスと種族)", "Characters/GameData.cs", None),
    ("treasure", "treasure.json(宝)", "Items/Treasure.cs", None),
    ("traps", "traps.json・trapsets.json(罠)", "Items/Traps.cs", None),
    ("mazegen", "mazegen.json・prefabs.json(迷宮の生成とはめ込む部屋)", "MazeGen/MazeGenSettings.cs", None),
    ("worlds", "worlds.json(世界の文: 序文・階の名前など)", "Save/ScenarioExport.cs", ["WorldPackDef"]),
    ("items", "items.json(品)", "Items/Items.cs", ["ItemKind", "ArmorType", "ItemDef", "LooksLike"]),
]

# ページに足すほかのファイル: 出力の名前 → [(ソース, 載せるクラス)]
EXTRA = {
    "worlds": [("Ai/WorldText.cs", None), ("Ai/WorldParts.cs", ["WorldOrigin"])],
}

CLASS_RE = re.compile(r"^public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*(class|record|enum)\s+(\w+)")
PROP_RE = re.compile(r"^public\s+(?!static|const|override)([\w<>\[\],?.\s]+?)\s+(\w+)\s*\{\s*get;\s*(?:set|init);")
CONST_RE = re.compile(r"^public\s+(?:const|static\s+readonly)\s+([\w<>\[\],?.]+)\s+(\w+)\s*=")
DEFAULT_RE = re.compile(r"\}\s*=\s*(.+?);\s*(?://.*)?$")
ENUM_MEMBER_RE = re.compile(r"^(\w+)\s*(?:=\s*[^,]+)?,?\s*(?://.*)?$")


def snake(name: str) -> str:
    """JsonNamingPolicy.SnakeCaseLower と同じ形(MinDepth → min_depth)。"""
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", name)
    s = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", s)
    return s.lower()


def clean(doc_lines):
    """/// の説明を Markdown にする(<code> は囲み、<c> と <see cref> は名前、実体参照は戻す)。"""
    text = "\n".join(doc_lines)
    text = re.sub(r"</?summary>", "", text)
    text = re.sub(r"<param name=\"(\w+)\">", r"\1: ", text).replace("</param>", "")
    text = re.sub(r"<see cref=\"([^\"]+)\"\s*/>", r"\1", text)
    text = re.sub(r"<c>(.*?)</c>", r"`\1`", text)
    text = re.sub(r"</?para>", "", text)
    text = text.replace("<code>", "\n```text\n").replace("</code>", "\n```\n")
    return html.unescape(text).strip()


def extract(source: Path, only):
    lines = source.read_text(encoding="utf-8").splitlines()
    out = []
    doc = []
    ignore_next = False
    current = None
    in_enum = False
    depth = 0
    class_depth = None
    for raw in lines:
        line = raw.strip()
        if line.startswith("///"):
            doc.append(line[3:].strip() if line[3:4] == " " else line[3:])
            continue
        if line.startswith("[") and "JsonIgnore" in line:
            ignore_next = True
            continue
        if line.startswith("["):
            continue
        m = CLASS_RE.match(line)
        if m and depth <= 1:
            current = m.group(2)
            in_enum = m.group(1) == "enum"
            class_depth = depth
            if only is None or current in only:
                out.append(f"\n## {current}" + ("(enum: 値は小文字のスネークケースで書く)" if in_enum else ""))
                if doc:
                    out.append("\n" + clean(doc) + "\n")
                inline = re.search(r"\{([^}]*)\}", line) if in_enum else None
                if inline:
                    out.append(", ".join(f"`{snake(v.strip().split('=')[0].strip())}`" for v in inline.group(1).split(",") if v.strip()))
        elif current and (only is None or current in only) and not ignore_next:
            p = PROP_RE.match(line)
            c = CONST_RE.match(line)
            if p and not in_enum:
                desc = clean(doc).replace("\n", " ")
                d = DEFAULT_RE.search(line)
                value = d.group(1).strip() if d else ""
                default = f" 省くと `{value}`" if value and value not in ("[]", "new()", '""', 'LocalizedText.Of("")') and "(" not in value else ""
                out.append(f"- `{snake(p.group(2))}` ({p.group(1).strip()}){default}" + (f": {desc}" if desc else ""))
            elif c and doc:
                out.append(f"- 定数 `{c.group(2)}`: {clean(doc).replace(chr(10), ' ')}")
            elif in_enum and depth == class_depth + 1:
                e = ENUM_MEMBER_RE.match(line)
                if e and e.group(1) not in ("{", "}"):
                    desc = clean(doc).replace("\n", " ")
                    out.append(f"- `{snake(e.group(1))}`" + (f": {desc}" if desc else ""))
        if line and not line.startswith("["):
            doc = []
            ignore_next = False
        depth += raw.count("{") - raw.count("}")
        if class_depth is not None and depth <= class_depth and "}" in raw:
            current = None
            in_enum = False
            class_depth = None
    return out


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    dest = Path(sys.argv[1])
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(KIT, dest)
    shutil.copy2(ROOT / "docs" / "class-pack-guide.md", dest / "class-pack-guide.md")
    ref = dest / "reference"
    ref.mkdir(exist_ok=True)
    index = ["# 形式の一覧(自動で書き出したもの)", "",
             "ゲームのソースの説明から、JSON の項目を書き出したもの。項目の名前は JSON に書く形(小文字のスネークケース)。",
             "型の `?` は省ける項目。省いたときの値は、説明に書いてあればそれ、無ければ型の既定(数は 0、真偽は false、一覧は空)。", ""]
    for name, title, src, only in REFERENCES:
        sources = [(src, only)] + EXTRA.get(name, [])
        body = []
        for s_src, s_only in sources:
            body += extract(CORE / s_src, s_only)
        names = "、".join(f"`src/GravewardSpiral.Core/{x}`" for x, _ in sources)
        (ref / f"{name}.md").write_text(f"# {title}\n\nソース: {names} から自動で書き出し。\n" + "\n".join(body) + "\n", encoding="utf-8")
        index.append(f"- [{title}]({name}.md)")
    (ref / "README.md").write_text("\n".join(index) + "\n", encoding="utf-8")
    print(f"scenario-kit を作りました: {dest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""画面の文言の言語ファイル(Content/<パック>/text/<言語>.json)を、日本語の文言と比べて確かめる。

使い方:
  python tools/text_check.py en            Content/srd/text/en.json を ja.json と比べる
  python tools/text_check.py en --pack srd

見るもの:
  - 日本語にあって、その言語に無いキー(無ければ英語 → 日本語の順に出るので動きはするが、抜け)
  - 日本語に無いキー(書き間違いの疑い)
  - {0} {1} などの差し込みの数と番号が日本語と合わない(合わないと落ちるか、値が出ない)
  - 日本語以外の言語なのに、かなや漢字が残っている(訳し忘れ)
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
JA = re.compile(r"[぀-ヿ一-鿿]")
SLOT = re.compile(r"\{(\d+)(?::[^}]*)?\}")
# 日本語のままでよいキー(名前入力のかなの鍵など)
KEEP_JA = {"entry.key.hiragana", "entry.key.katakana"}


def main(args):
    lang = args[0] if args else "en"
    pack = args[args.index("--pack") + 1] if "--pack" in args else "srd"
    base = json.loads((ROOT / "Content" / pack / "text" / "ja.json").read_text(encoding="utf-8"))
    path = ROOT / "Content" / pack / "text" / f"{lang}.json"
    other = json.loads(path.read_text(encoding="utf-8"))
    problems = 0
    for k, v in base.items():
        if k not in other:
            print(f"無いキー: {k}")
            problems += 1
            continue
        a = sorted(set(SLOT.findall(v)))
        b = sorted(set(SLOT.findall(other[k])))
        if a != b:
            print(f"差し込みが合わない: {k}  ja {a} / {lang} {b}")
            problems += 1
        if lang != "ja" and k not in KEEP_JA and JA.search(other[k]):
            print(f"日本語が残っている: {k}: {other[k][:40]}")
            problems += 1
    for k in other:
        if k not in base:
            print(f"日本語に無いキー: {k}")
            problems += 1
    print(f"{path.relative_to(ROOT)}: {len(other)} 個、気になる所 {problems} 件")
    return 1 if problems else 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))

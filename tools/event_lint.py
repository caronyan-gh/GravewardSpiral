"""下書きのイベントの文の見直し(--check-events が見ない、文の書き方の食い違い)。

使い方:
  python tools/event_lint.py tools/event-drafts/maze_events_4a.json [...]          気になる所を出す
  python tools/event_lint.py --fix tools/event-drafts/maze_events_4a.json [...]    直せるものは直す

見るもの:
  - 話者(speaker)があるのに、日本語の文が「」で始まらない(地の文に話者が付いている)→ --fix で話者を外す。
    ただし町のイベントは、話者がいるのが当たり前なので、文を「」で囲む方で直す
  - 日本語の文に、かなと漢字が1つも無い(英語のまま)
  - 英語の文に、かなと漢字が入っている
  - 同じ日本語の文が、別のイベントにそのまま出てくる(写しの疑い)
  - 町のイベントの題が、ほかのイベントと同じ
"""
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

JA = re.compile(r"[぀-ヿ一-鿿]")


def texts(steps):
    for s in steps:
        if s.get("type") in ("text", "choice") and s.get("text"):
            yield s
        for k in ("then", "else", "success", "fail", "win", "lose"):
            yield from texts(s.get(k, []))
        for o in s.get("options", []):
            if o.get("label"):
                yield {"type": "label", "text": o["label"], "_owner": o}
            yield from texts(o.get("steps", []))


def main(args):
    fix = "--fix" in args
    files = [a for a in args if a != "--fix"]
    seen = defaultdict(list)
    titles = defaultdict(list)
    problems = 0
    for f in files:
        events = json.loads(Path(f).read_text(encoding="utf-8"))
        changed = 0
        for e in events:
            if e.get("title"):
                titles[e["title"].get("ja", "")].append(e["id"])
            for s in texts(e.get("steps", [])):
                t = s["text"]
                ja, en = t.get("ja", ""), t.get("en", "")
                if not JA.search(ja):
                    print(f"{f} {e['id']}: 日本語が無い: {ja[:40]}")
                    problems += 1
                if JA.search(en):
                    print(f"{f} {e['id']}: 英語に日本語が入っている: {en[:40]}")
                    problems += 1
                if s["type"] == "text" and s.get("speaker") and not ja.lstrip().startswith("「"):
                    if e.get("place") == "town":
                        if fix:
                            t["ja"] = "「" + ja.strip() + "」"
                            if not en.lstrip().startswith('"'):
                                t["en"] = '"' + en.strip() + '"'
                            changed += 1
                        else:
                            print(f"{f} {e['id']}: 町の話者の文が「」で囲まれていない: {ja[:30]}")
                            problems += 1
                    elif fix:
                        del s["speaker"]
                        changed += 1
                    else:
                        print(f"{f} {e['id']}: 地の文に話者がある: {ja[:30]}")
                        problems += 1
                if s["type"] == "text" and len(ja) >= 12:
                    seen[ja].append(e["id"])
        if fix and changed:
            Path(f).write_text(json.dumps(events, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"{f}: {changed} 箇所を直しました")
    for ja, ids in seen.items():
        if len(set(ids)) > 1:
            print(f"同じ文が {len(set(ids))} 個のイベントにある({', '.join(sorted(set(ids))[:4])}): {ja[:40]}")
            problems += 1
    for t, ids in titles.items():
        if len(ids) > 1:
            print(f"町の題が重なっている: {t}({', '.join(ids)})")
            problems += 1
    print(f"気になる所 {problems} 件")
    return 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))

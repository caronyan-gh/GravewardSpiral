"""下書きのイベント(tools/event-drafts/*.json)を検査して、パックの events.json に足す。

使い方:
  python tools/event_merge.py tools/event-drafts/maze_events.json [...]          検査だけ(足さない)
  python tools/event_merge.py --apply tools/event-drafts/maze_events.json [...]  検査を通ったら Content/srd/events.json の終わりに足す
  python tools/event_merge.py --replace tools/event-drafts/maze_events.json      同じ id のものは置き換え、無いものは足す(手直しした下書き)

検査は `dotnet run --project tools/GravewardSpiral.Sim -- --check-events` で行う(部品、知らない項目名、id の重なり)。
--apply では同じ id が既にあれば、足さずに止まる。--replace では、置き換える(検査も events.json と重なる id は見ない)。events.json は元の書き方(インデント 2)のまま書く。
"""
import json
import os
import subprocess
import time
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EVENTS = ROOT / "Content" / "srd" / "events.json"


def main(args):
    replace = "--replace" in args
    apply = "--apply" in args or replace
    files = [a for a in args if a not in ("--apply", "--replace")]
    if not files:
        print(__doc__)
        return 2
    check = subprocess.run(["dotnet", "run", "--project", str(ROOT / "tools" / "GravewardSpiral.Sim"), "--", *(["--allow-existing"] if replace else []), "--check-events", *files],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(check.stdout.strip().splitlines()[-1] if check.stdout.strip() else check.stderr)
    if check.returncode != 0:
        print(check.stdout)
        return 1
    if not apply:
        return 0
    events = json.loads(EVENTS.read_text(encoding="utf-8"))
    index = {e["id"]: i for i, e in enumerate(events)}
    added = replaced = 0
    for f in files:
        for e in json.loads(Path(f).read_text(encoding="utf-8")):
            if e["id"] in index:
                if not replace:
                    print(f"id が重なっています: {e['id']}")
                    return 1
                events[index[e["id"]]] = e
                replaced += 1
                continue
            index[e["id"]] = len(events)
            events.append(e)
            added += 1
    # 一時ファイルに書いてから置き換える(ほかのプログラムが一瞬つかんでいても壊さないよう、少し待って試し直す)
    tmp = EVENTS.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(events, ensure_ascii=False, indent=2), encoding="utf-8")
    for attempt in range(10):
        try:
            os.replace(tmp, EVENTS)
            break
        except OSError:
            if attempt == 9:
                raise
            time.sleep(0.5)
    print(f"{added} 個を足し、{replaced} 個を置き換えました: {EVENTS.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))

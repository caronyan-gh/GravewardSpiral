"""選んだ効果音を、ゲームが読める WAV(16bit、44.1kHz)にして Content/srd/audio/se/<id>.wav に置く。

使い方: python tools/se_import.py --from <選んだ音のフォルダ>
  出どころと加工(音量・くり返し・フェード)は tools/se-source/se_sources.json に書く。
  音の大きさは、鳴っているあいだの大きさ(50ms ごとの RMS の上位 10%)を level(dB)にそろえる。
  level を書かない音は default_level。ピークが -1dB を超えないように、上げすぎない。
  limit: true の音は、目安まで上げてからピークを丸める(短いクリック音など、ピークだけが高い音を大きくするとき)。
  ogg・wav・mp3 を読める(soundfile を使う。pip install soundfile)。
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
SOURCES = ROOT / "tools" / "se-source" / "se_sources.json"
OUT = ROOT / "Content" / "srd" / "audio" / "se"
RATE = 44100


def resample(data, rate):
    if rate == RATE:
        return data
    n = int(round(len(data) * RATE / rate))
    x_old = np.linspace(0, 1, len(data), endpoint=False)
    x_new = np.linspace(0, 1, n, endpoint=False)
    return np.stack([np.interp(x_new, x_old, data[:, c]) for c in range(data.shape[1])], axis=1)


def process(item, src):
    data, rate = sf.read(src, dtype="float32", always_2d=True)
    data = resample(data, rate)
    repeat = item.get("repeat", 1)
    if repeat > 1:
        gap = np.zeros((int(item.get("gap", 0.15) * RATE), data.shape[1]), dtype=np.float32)
        data = np.concatenate([np.concatenate([data, gap]) for _ in range(repeat)])
    fade = item.get("fade_out", 0)
    if fade > 0:
        n = min(len(data), int(fade * RATE))
        data[-n:] *= np.linspace(1, 0, n)[:, None]
    return data


PEAK_LIMIT = 10 ** (-1 / 20)


def loudness(data):
    """鳴っているあいだの大きさ(dB)。50ms ごとの RMS の上位 10%。"""
    m = data.mean(axis=1)
    win = RATE // 20
    n = len(m) // win
    if n == 0:
        return 20 * np.log10(np.sqrt(np.mean(m ** 2)) + 1e-9)
    rms = np.sqrt((m[: n * win].reshape(n, win) ** 2).mean(axis=1))
    return 20 * np.log10(np.percentile(rms, 90) + 1e-9)


def level(data, target_db, limit=False):
    gain = 10 ** ((target_db - loudness(data)) / 20)
    if limit:
        # ピークを丸める(tanh で -1dB に寄せる)。丸めると少し小さくなるので、何度か合わせ直す
        out = data
        for _ in range(4):
            out = PEAK_LIMIT * np.tanh(data * gain / PEAK_LIMIT)
            gain *= 10 ** ((target_db - loudness(out)) / 20)
        return out
    peak = float(np.max(np.abs(data))) * gain
    if peak > PEAK_LIMIT:   # 割れないように、上げすぎない
        gain *= PEAK_LIMIT / peak
    return data * gain


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from", dest="src", required=True, help="選んだ音のフォルダ")
    args = ap.parse_args()
    base = Path(args.src)
    spec = json.loads(SOURCES.read_text(encoding="utf-8"))
    OUT.mkdir(parents=True, exist_ok=True)
    missing = []
    for item in spec["sounds"]:
        src = base / item["file"]
        if not src.exists():
            missing.append(item["id"])
            continue
        data = level(process(item, src), item.get("level", spec.get("default_level", -18)), item.get("limit", False))
        sf.write(OUT / f"{item['id']}.wav", data, RATE, subtype="PCM_16")
        print(f"{item['id']:<14} {len(data) / RATE:5.2f} 秒  {loudness(data):6.1f} dB  ← {item['file']}")
    if missing:
        print("見つからない:", ", ".join(missing))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""配布用のビルドをして、Inno Setup でインストーラーを作る。

使い方: python tools/make_installer.py [--version 0.3.3] [--skip-publish]
  1. dotnet publish で、.NET を入れていない PC でも動く形(win-x64、self-contained)を out/publish に作る。
     ゲームとシナリオエディタと検査の道具(ScenarioCheck)を同じフォルダに入れる(エディタと道具は横の Content を使う。.NET の部品は同じ版なので共用する)。
     シナリオを作る人とエージェント向けの scenario-kit(手引きと、ソースから書き出した形式の一覧)も入れる(tools/make_scenario_kit.py)。
  2. Inno Setup 6 の ISCC で tools/installer/GravewardSpiral.iss を通し、out/installer/GravewardSpiral-Setup-<版>.exe を作る。
  Inno Setup 6 が要る(winget install JRSoftware.InnoSetup)。ISCC.exe はいつもの場所から探す。
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PUBLISH = ROOT / "out" / "publish"
OUT = ROOT / "out" / "installer"
ISS = ROOT / "tools" / "installer" / "GravewardSpiral.iss"
PROJECT = ROOT / "src" / "GravewardSpiral.Game"
EDITOR = ROOT / "tools" / "GravewardSpiral.Editor"
CHECK = ROOT / "tools" / "GravewardSpiral.ScenarioCheck"


def find_iscc():
    candidates = [shutil.which("iscc"), shutil.which("ISCC")]
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles"), os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs")):
        if base:
            candidates.append(os.path.join(base, "Inno Setup 6", "ISCC.exe"))
    return next((c for c in candidates if c and os.path.exists(c)), None)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.3.3", help="インストーラーに付ける版")
    ap.add_argument("--skip-publish", action="store_true", help="out/publish をそのまま使う")
    args = ap.parse_args()

    iscc = find_iscc()
    if iscc is None:
        print("Inno Setup 6 の ISCC.exe が見つかりません。入れてから流し直してください: winget install JRSoftware.InnoSetup")
        return 1

    if not args.skip_publish:
        if PUBLISH.exists():
            shutil.rmtree(PUBLISH)
        for project in (PROJECT, EDITOR, CHECK):
            subprocess.run(["dotnet", "publish", str(project), "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                            f"-p:Version={args.version}", "-o", str(PUBLISH)], check=True)

    subprocess.run([sys.executable, str(ROOT / "tools" / "make_scenario_kit.py"), str(PUBLISH / "scenario-kit")], check=True)

    OUT.mkdir(parents=True, exist_ok=True)
    subprocess.run([iscc, f"/DAppVersion={args.version}", f"/DSourceDir={PUBLISH}", f"/DOutputDir={OUT}", f"/DRepoDir={ROOT}", str(ISS)], check=True)
    made = OUT / f"GravewardSpiral-Setup-{args.version}.exe"
    print(f"インストーラーを作りました: {made.relative_to(ROOT)}({made.stat().st_size / 1024 / 1024:.0f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

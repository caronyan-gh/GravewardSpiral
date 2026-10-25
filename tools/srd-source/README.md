# SRD の元データ

- `5e-SRD-Monsters.json`: [5e-bits/5e-database](https://github.com/5e-bits/5e-database) の `src/2014/en/5e-SRD-Monsters.json`(commit a6212beb4b278917c2cff41c2b04c4ef4f3e0d6f)。リポジトリは MIT、中身は SRD 5.1(CC-BY-4.0)。
- `5e-SRD-Spells.json`: 同じリポジトリ・同じ commit の `src/2014/en/5e-SRD-Spells.json`。
- `monsters_ja.json`、`spells_ja.json`: 日本語名(このプロジェクトで訳したもの)。
- `spells_overrides.json`: 自動で変換できない呪文を、効果の部品で手で書いたもの。

`python tools/srd_import.py` で `Content/srd/monsters.json` と `spells.json` に変換する。
- `5e-SRD-Equipment.json`、`5e-SRD-Equipment-Categories.json`、`5e-SRD-Magic-Items.json`: 同じ commit の武器・鎧・道具と魔法のアイテム。
- `items_ja.json`: 武器・鎧・盾の日本語名。`items_extra.json`: 袋・道具・水薬など手で書いた品。
- `magic_items.json`: 魔法のアイテムを効果の部品で手で書いたもの(説明文は写さない)。`magic_items_skipped.md`: 入れなかったものと理由。

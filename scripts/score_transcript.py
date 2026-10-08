"""算「講稿追得到的比例」：講稿裡有多少字，能在辨識結果裡依序找到（無聲調拼音相同就算）。

用法：
    python scripts/score_transcript.py <講稿.md> <bench 結果.json> [更多 json ...]

為什麼不用字錯率（CER）：瓦基錄 Podcast 時是看著文章「講」，不是逐字「念」，
講出來的字數大約是文章的兩倍（2026-10-08 用一集看稿講的 Podcast 實測）。拿文章當標準答案算 CER，
多講的字全部算成錯，量不出模型好壞。提詞機真正要的是「講稿的字有沒有被聽到、順序對不對」，
也就是講稿與辨識結果的最長共同子序列（LCS）佔講稿的比例。多講的話不扣分。

比對單位（計畫書第 6.1 節的簡化版）：
- 漢字：所有讀音的集合（data/pinyin/unihan-pinyin.tsv），有交集就算同一個字，簡繁、同音字都吸收
- 英文與數字：NFKC 正規化後轉小寫，一個單字一個單位
- 標點與空白：跳過
- 國字數字與阿拉伯數字的互換還沒做（第 3.3 步），兩邊寫法不同時會算沒追到
"""

from __future__ import annotations

import json
import re
import sys
import unicodedata
from pathlib import Path

TABLE = Path(__file__).resolve().parent.parent / "data" / "pinyin" / "unihan-pinyin.tsv"
TOKEN = re.compile(r"[a-z0-9]+|[㐀-鿿\U00020000-\U0003ffff]")


def load_pinyin() -> dict[str, list[str]]:
    table = {}
    for line in TABLE.read_text(encoding="utf-8").splitlines():
        if line.startswith("#"):
            continue
        char, readings = line.split("\t")
        table[char] = readings.split()
    return table


def tokenize(text: str, pinyin: dict[str, list[str]], by_sound: bool) -> list[list[str]]:
    """每個單位回傳一組比對鍵，兩個單位只要有一個鍵相同就算相符。"""
    text = re.sub(r"^#+\s*", "", text, flags=re.M)  # Markdown 標題符號
    text = unicodedata.normalize("NFKC", text).lower()
    units = []
    for t in TOKEN.findall(text):
        if t[0].isascii():
            units.append(["w:" + t])
        elif by_sound and t in pinyin:
            units.append(["p:" + r for r in pinyin[t]])
        else:
            units.append(["c:" + t])
    return units


def lcs_length(script: list[list[str]], heard: list[list[str]]) -> int:
    """位元平行的 LCS（Hyyrö 2004）：講稿每個位置一個位元，整集 Podcast 幾秒內算完。"""
    masks: dict[str, int] = {}
    for i, keys in enumerate(script):
        for k in keys:
            masks[k] = masks.get(k, 0) | (1 << i)
    full = (1 << len(script)) - 1
    v = full
    for keys in heard:
        m = 0
        for k in keys:
            m |= masks.get(k, 0)
        u = v & m
        v = ((v + u) | (v - u)) & full
    return len(script) - bin(v).count("1")


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    if len(argv) < 2:
        print(__doc__)
        return 2
    pinyin = load_pinyin()
    script_text = Path(argv[0]).read_text(encoding="utf-8")
    script_sound = tokenize(script_text, pinyin, by_sound=True)
    script_char = tokenize(script_text, pinyin, by_sound=False)
    print(f"講稿：{argv[0]}（{len(script_sound)} 個比對單位）")
    print()
    print("模型  辨識單位  追到（拼音）  追到（直接比字）")
    for path in argv[1:]:
        report = json.loads(Path(path).read_text(encoding="utf-8"))
        transcript = report["transcript"]
        heard_sound = tokenize(transcript, pinyin, by_sound=True)
        heard_char = tokenize(transcript, pinyin, by_sound=False)
        by_sound = lcs_length(script_sound, heard_sound) / len(script_sound)
        by_char = lcs_length(script_char, heard_char) / len(script_char)
        print(f"{report['model']:<4}  {len(heard_sound):>8}  {by_sound:>11.1%}  {by_char:>15.1%}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

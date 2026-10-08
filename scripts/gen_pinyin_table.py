"""從 Unicode Unihan 資料庫產生無聲調拼音表 data/pinyin/unihan-pinyin.tsv。

用法：
    python scripts/gen_pinyin_table.py <Unihan.zip 的路徑>

Unihan.zip 從 https://www.unicode.org/Public/UCD/latest/ucd/Unihan.zip 下載（約 8 MB），
放在暫存區就好，不進 repo；進 repo 的只有產生出來的純文字表。

輸出格式：每行「字<TAB>讀音（以空白分隔、去聲調、ü 寫成 v、依字母排序）」。
一個字的讀音合併四個欄位，讓多音字的每個讀音都在（計畫書第 6 節：有交集就算對）：
  kMandarin（最常用讀音，綠這類常用字只有這欄）、kHanyuPinyin（漢語大字典）、
  kTGHZ2013（通用規範漢字字典）、kXHC1983（現代漢語詞典）。
"""

from __future__ import annotations

import hashlib
import re
import sys
import unicodedata
import zipfile
from pathlib import Path

FIELDS = ("kMandarin", "kHanyuPinyin", "kTGHZ2013", "kXHC1983")
OUT = Path(__file__).resolve().parent.parent / "data" / "pinyin" / "unihan-pinyin.tsv"


def strip_tone(syllable: str) -> str:
    # 先拆成「字母＋附加符號」：ǜ 是單一字元，要拆開才看得到 ü 的兩點
    s = unicodedata.normalize("NFD", syllable.lower()).replace("u\u0308", "v")
    return "".join(c for c in s if not unicodedata.combining(c))


def readings_of(field: str, value: str) -> list[str]:
    if field == "kMandarin":
        return value.split()
    # 其餘三個欄位的格式是「頁碼:讀音,讀音 頁碼:讀音」
    out = []
    for entry in value.split():
        _, _, syllables = entry.partition(":")
        out.extend(syllables.split(","))
    return out


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    if len(argv) != 1:
        print(__doc__)
        return 2
    zip_path = Path(argv[0])
    raw = zip_path.read_bytes()
    with zipfile.ZipFile(zip_path) as z:
        text = z.read("Unihan_Readings.txt").decode("utf-8")
    version = re.search(r"Unicode Version (\d+\.\d+\.\d+)", text)

    table: dict[int, set[str]] = {}
    for line in text.splitlines():
        if not line.startswith("U+"):
            continue
        code, field, value = line.split("\t", 2)
        if field not in FIELDS:
            continue
        cp = int(code[2:], 16)
        for r in readings_of(field, value):
            syllable = strip_tone(r)
            if not re.fullmatch(r"[a-z]+", syllable):
                raise ValueError(f"{code} {field} 讀音格式不認得：{r!r}")
            table.setdefault(cp, set()).add(syllable)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8", newline="\n") as f:
        f.write(f"# 由 scripts/gen_pinyin_table.py 產生，不要手改。\n")
        f.write(f"# 來源：Unicode Unihan {version.group(1) if version else '（版本不明）'}，Unihan.zip SHA256 {hashlib.sha256(raw).hexdigest()}\n")
        f.write(f"# 欄位：{'、'.join(FIELDS)}；去聲調、ü 寫成 v\n")
        for cp in sorted(table):
            f.write(f"{chr(cp)}\t{' '.join(sorted(table[cp]))}\n")

    print(f"已寫入 {OUT}：{len(table)} 個字")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

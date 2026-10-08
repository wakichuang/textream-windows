"""下載 sherpa-onnx 串流模型、驗證完整性、解壓到模型資料夾。

用法：
    python scripts/fetch_models.py            # 下載預設的 A、B、C 三個模型
    python scripts/fetch_models.py C          # 只下載 C
    python scripts/fetch_models.py --list     # 列出候選模型與目前狀態

模型放在 %LOCALAPPDATA%\\Textream\\models\\（可用 --dest 或環境變數 TEXTREAM_MODELS_DIR 改）。
不要放 %LOCALAPPDATA%\\Temp，那裡會被 Windows 清掉。

做法：一邊下載一邊解壓，壓縮檔本身不落地，所以不留下要另外刪掉的大檔。
下載過程同時算 SHA256，跟下面表格鎖定的值比對；大小也要對上。
解壓完成且驗證通過，才在模型資料夾寫入 .fetched.json。沒有這個檔的資料夾一律視為不完整。
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import sys
import tarfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path

URL_BASE = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"
MARKER = ".fetched.json"


@dataclass(frozen=True)
class Model:
    code: str
    name: str
    size: int  # 壓縮檔位元組數，GitHub 發佈頁的值
    sha256: str  # 壓縮檔的 SHA256
    note: str


# 計畫書第 5.1 節的候選模型。
# C、D 的 SHA256 取自 GitHub 發佈頁 API 的 digest 欄位；
# A、B 上架得早，API 沒有 digest，是 2026-10-08 第一次下載時算出來再鎖定的。
MODELS: dict[str, Model] = {
    m.code: m
    for m in [
        Model(
            "A",
            "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
            511_274_346,
            "27ffbd9ee24ad186d99acc2f6354d7992b27bcab490812510665fa8f9389c5f8",
            "zipformer 中英雙語",
        ),
        Model(
            "B",
            "sherpa-onnx-streaming-paraformer-bilingual-zh-en",
            1_047_319_737,
            "5462a1fce42693deae572af1e8c4687124b12aa85fe61ff4d3168bb5280e205f",
            "paraformer 中英雙語",
        ),
        Model(
            "C",
            "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30",
            132_634_597,
            "5a2832047ea1f97dd0dc595b816c230c4bafad65cfc0341fa57517cadc50afd0",
            "zipformer 中文 2025",
        ),
        Model(
            "D",
            "sherpa-onnx-streaming-zipformer-zh-xlarge-int8-2025-06-30",
            597_755_927,
            "30437d84dc4861740d40166212ab05f3728945443d53691620075c4031fb88e1",
            "zipformer 中文 xlarge 2025（選配）",
        ),
    ]
}
DEFAULT_CODES = ["A", "B", "C"]


def default_dest() -> Path:
    env = os.environ.get("TEXTREAM_MODELS_DIR")
    if env:
        return Path(env)
    local = os.environ.get("LOCALAPPDATA")
    if local:
        return Path(local) / "Textream" / "models"
    return Path.home() / ".local" / "share" / "Textream" / "models"


class HashingReader:
    """包住 HTTP 回應：讀到的每個位元組都算進 SHA256，順便印進度。"""

    def __init__(self, raw, total: int) -> None:
        self.raw = raw
        self.total = total
        self.read_bytes = 0
        self.hash = hashlib.sha256()
        self._next_report = 0.0

    def read(self, n: int = -1) -> bytes:
        chunk = self.raw.read(n)
        self.hash.update(chunk)
        self.read_bytes += len(chunk)
        if self.total and self.read_bytes / self.total >= self._next_report:
            print(f"  {self.read_bytes / 1e6:8.1f} / {self.total / 1e6:.1f} MB", flush=True)
            self._next_report += 0.1
        return chunk

    def drain(self) -> None:
        # tarfile 讀到結尾標記就停了，後面的補白也要算進雜湊才對得上。
        while self.read(1 << 20):
            pass


def status(model: Model, dest: Path) -> str:
    folder = dest / model.name
    if (folder / MARKER).is_file():
        return "已下載"
    if folder.exists():
        return "不完整（資料夾在，但沒有 .fetched.json）"
    return "未下載"


def fetch(model: Model, dest: Path, url_base: str) -> bool:
    folder = dest / model.name
    state = status(model, dest)
    if state == "已下載":
        print(f"[{model.code}] {model.name}：已下載，跳過")
        return True
    if folder.exists():
        print(
            f"[{model.code}] {folder} 已存在但不完整。"
            "請先把它丟進資源回收桶再重跑，腳本不會自己刪。",
            file=sys.stderr,
        )
        return False

    dest.mkdir(parents=True, exist_ok=True)
    url = f"{url_base}/{model.name}.tar.bz2"
    print(f"[{model.code}] 下載並解壓 {url}")
    with urllib.request.urlopen(url) as resp:
        reader = HashingReader(resp, model.size)
        with tarfile.open(fileobj=reader, mode="r|bz2") as tar:
            for member in tar:
                top = Path(member.name).parts[0] if member.name else ""
                if top != model.name:
                    raise RuntimeError(f"壓縮檔裡有不在 {model.name}/ 底下的項目：{member.name}")
                tar.extract(member, dest, filter="data")
        reader.drain()

    digest = reader.hash.hexdigest()
    problems = []
    if reader.read_bytes != model.size:
        problems.append(f"大小 {reader.read_bytes} ≠ 預期 {model.size}")
    if model.sha256 and digest != model.sha256:
        problems.append(f"SHA256 {digest} ≠ 預期 {model.sha256}")
    if problems:
        print(f"[{model.code}] 驗證失敗：{'；'.join(problems)}", file=sys.stderr)
        print(f"[{model.code}] {folder} 不完整，請丟進資源回收桶後重跑。", file=sys.stderr)
        return False

    if not model.sha256:
        print(f"[{model.code}] 注意：這個模型沒有鎖定的 SHA256，本次算出 {digest}，請寫回 MODELS 表。")

    marker = {
        "code": model.code,
        "name": model.name,
        "url": url,
        "size": reader.read_bytes,
        "sha256": digest,
        "sha256_pinned": bool(model.sha256),
        "fetched_at": dt.datetime.now().astimezone().isoformat(timespec="seconds"),
    }
    (folder / MARKER).write_text(json.dumps(marker, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"[{model.code}] 完成：{folder}（SHA256 {digest}）")
    return True


def main(argv: list[str] | None = None) -> int:
    # 輸出被管線或 AI 工具接走時，PowerShell 5.1 預設用 cp950，中文會變亂碼。
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="下載 Textream for Windows 用的 sherpa-onnx 串流模型")
    parser.add_argument("codes", nargs="*", help=f"模型代號，預設 {' '.join(DEFAULT_CODES)}")
    parser.add_argument("--dest", type=Path, default=None, help="模型資料夾")
    parser.add_argument("--list", action="store_true", help="列出候選模型與狀態")
    parser.add_argument("--url-base", default=URL_BASE, help=argparse.SUPPRESS)  # 測試用
    args = parser.parse_args(argv)

    dest = args.dest or default_dest()
    if args.list:
        print(f"模型資料夾：{dest}")
        for m in MODELS.values():
            print(f"  {m.code}  {m.size / 1e6:7.1f} MB  {status(m, dest):<6}  {m.name}（{m.note}）")
        return 0

    codes = [c.upper() for c in args.codes] or DEFAULT_CODES
    unknown = [c for c in codes if c not in MODELS]
    if unknown:
        parser.error(f"不認得的模型代號：{' '.join(unknown)}，可用 {' '.join(MODELS)}")

    ok = all([fetch(MODELS[c], dest, args.url_base) for c in codes])
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

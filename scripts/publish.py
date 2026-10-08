"""打包 Textream for Windows 免安裝版（計畫書第 7.1 步；瓦基 2026-10-08：要公開給一般人，下載解壓縮就能用）。

做的事：
1. dotnet publish 成單一執行檔（自帶 .NET，不用另外裝）
2. 把語音模型 A（fp32，約 360 MB）放在執行檔旁邊的 models 資料夾，使用者不必另外下載
3. 附上授權檔、第三方授權聲明、給一般人看的使用說明
4. 壓成一個 zip，可以直接放到 GitHub Releases

用法：
    python scripts/publish.py              打包到 publish/
    python scripts/publish.py --install    另外複製一份到 $HOME/Apps/Textream for Windows（瓦基自己用）

模型來源：scripts/fetch_models.py 下載的位置（%LOCALAPPDATA%/Textream/models），或環境變數 TEXTREAM_MODELS_DIR。
"""

import argparse
import os
import pathlib
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "TextreamWindows.App" / "TextreamWindows.App.csproj"
OUT = ROOT / "publish"
FOLDER_NAME = "Textream for Windows"
MODEL_A = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20"
# 只帶程式實際載入的 fp32 檔（int8 版會口吃，見計畫書第 1 節），再加上模型自己的說明（含 Apache-2.0 授權標記）
MODEL_FILES = ["tokens.txt", "encoder-epoch-99-avg-1.onnx", "decoder-epoch-99-avg-1.onnx", "joiner-epoch-99-avg-1.onnx", "README.md"]

GUIDE = """Textream for Windows 使用說明
================================

Textream for Windows 是一個會「聽你講到哪裡」的提詞機：
講稿浮在螢幕上方，你照稿念，黃色高亮就跟著你的聲音往下走。
語音辨識完全在你的電腦上進行，不用網路、不會上傳任何聲音。

一、第一次打開
1. 把下載的 zip 檔「解壓縮」（在 zip 上按右鍵 →「解壓縮全部」）。
   不要直接在 zip 裡面點兩下執行，會找不到語音模型。
2. 打開解壓縮出來的「Textream for Windows」資料夾，雙擊 TextreamWindows.exe。
3. 如果跳出藍色的「Windows 已保護您的電腦」：按「其他資訊」→「仍要執行」。
   這是因為這個程式是個人分享、沒有買數位簽章，不是病毒。
4. 想以後從開始功能表打開：在 TextreamWindows.exe 上按右鍵 →「釘選到開始畫面」。

二、開始用
1. 把講稿貼進中間的大框框（或按「開啟…」選 .txt、.md 檔）。
2. 模式選「逐字追蹤」，按「開始」（或按 Ctrl+Alt+L）。
   整份英文的講稿，「辨識語言」改選 English；中文講稿（夾一些英文也可以）維持繁體中文。
3. 照稿念，黃色會跟著你走；講錯、跳句、插話都會自己接回來。
4. 按 Esc 停止。停下來的地方會記住，回到編輯畫面再按「開始」就從那裡接著念。

三、常用快捷鍵（在任何程式裡都能按）
  Ctrl+Alt+L       開始／暫停（讀完時是「重來」）
  Ctrl+Alt+↑ ↓     跳到上一句／下一句
  Esc              停止（浮層在畫面上時）
  浮層上點任何一個字：從那裡開始；滾輪轉一格：跳一行

四、錄影、分享畫面
  右上角「錄影／分享畫面」預設是「看不到浮層」：錄影、截圖、Google Meet 分享畫面都看不到提詞，只有你看得到。
  想錄示範影片給別人看時，改成「錄得到浮層」。

五、沒有聲音反應？
  到 Windows「設定 → 隱私權與安全性 → 麥克風」，確認「讓桌面應用程式存取您的麥克風」是開的，
  再到 Textream 的「麥克風」選單選對你的麥克風。

六、資料夾裡有什麼
  TextreamWindows.exe     程式本體（已內含執行需要的一切，不用另外安裝 .NET）
  models\\                 語音辨識模型（不要刪、不要搬走，要跟 exe 放在一起）
  LICENSE、THIRD_PARTY_NOTICES.md   授權說明
"""


def run(cmd: list[str]) -> None:
    print("> " + " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=ROOT)


def model_source() -> pathlib.Path:
    roots = []
    if os.environ.get("TEXTREAM_MODELS_DIR"):
        roots.append(pathlib.Path(os.environ["TEXTREAM_MODELS_DIR"]))
    roots.append(pathlib.Path(os.environ["LOCALAPPDATA"]) / "Textream" / "models")
    for root in roots:
        if (root / MODEL_A / "tokens.txt").exists():
            return root / MODEL_A
    sys.exit(f"找不到模型 A，找過：{', '.join(map(str, roots))}。先執行 python scripts/fetch_models.py A")


def version() -> str:
    text = APP.read_text(encoding="utf-8-sig")
    start = text.find("<Version>")
    return text[start + 9:text.find("</Version>")] if start >= 0 else "0.0.0"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--install", action="store_true", help="另外複製到 $HOME/Apps/Textream for Windows")
    args = parser.parse_args()

    folder = OUT / FOLDER_NAME
    if folder.exists():
        # 舊的打包結果：改名移開，不直接刪（專案規則：不用永久刪除）
        backup = OUT / f"{FOLDER_NAME}.old"
        if backup.exists():
            sys.exit(f"{backup} 已經存在，請先把它丟到資源回收桶再打包")
        folder.rename(backup)
        print(f"舊的打包結果移到 {backup}")
    folder.mkdir(parents=True)

    run([
        "dotnet", "publish", str(APP), "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=none",
        "-o", str(folder),
    ])

    source = model_source()
    target = folder / "models" / MODEL_A
    target.mkdir(parents=True)
    for name in MODEL_FILES:
        shutil.copy2(source / name, target / name)
    print(f"模型 A 複製自 {source}")

    for name in ["LICENSE", "THIRD_PARTY_NOTICES.md"]:
        if (ROOT / name).exists():
            shutil.copy2(ROOT / name, folder / name)
        else:
            print(f"注意：repo 根目錄沒有 {name}，免安裝版裡也不會有")
    (folder / "使用說明.txt").write_text(GUIDE, encoding="utf-8-sig")

    zip_path = OUT / f"Textream-for-Windows-{version()}-win-x64.zip"
    if zip_path.exists():
        zip_path.rename(zip_path.with_suffix(".zip.old"))
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path in sorted(folder.rglob("*")):
            if path.is_file():
                z.write(path, pathlib.Path(FOLDER_NAME) / path.relative_to(folder))

    size_mb = lambda p: p.stat().st_size / 1024 / 1024  # noqa: E731
    total = sum(size_mb(p) for p in folder.rglob("*") if p.is_file())
    print(f"資料夾：{folder}（{total:.0f} MB）")
    print(f"zip：{zip_path}（{size_mb(zip_path):.0f} MB）")

    if args.install:
        dest = pathlib.Path.home() / "Apps" / FOLDER_NAME
        if dest.exists():
            sys.exit(f"{dest} 已經存在，請先把它丟到資源回收桶（或關掉正在跑的程式）再安裝")
        shutil.copytree(folder, dest)
        print(f"已安裝到 {dest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

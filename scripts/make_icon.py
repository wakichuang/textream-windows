"""產生 Textream for Windows 的程式圖示（瓦基 2026-10-08）。

原版 Textream 的圖示（MIT 授權，$HOME/textream-ref，commit 2c02f3e）右下角加一個「四格視窗」小徽章，
讓人一看就知道是從 Textream 來、改成 Windows 版。徽章用閱讀前哨站色票：米白底 --bg、亮藍 --blue-bright。

用法：python scripts/make_icon.py
輸出：src/TextreamWindows.App/Assets/textream-windows.ico（16～256 各尺寸）與 textream-windows.png（256，給視窗用）
"""

import pathlib
import sys

from PIL import Image, ImageDraw

REF = pathlib.Path.home() / "textream-ref" / "Textream" / "Textream" / "Assets.xcassets" / "AppIcon.appiconset" / "icon_512x512@2x.png"
OUT = pathlib.Path(__file__).resolve().parent.parent / "src" / "TextreamWindows.App" / "Assets"

CREAM = (0xFB, 0xF8, 0xF4, 255)  # DESIGN.md --bg
BLUE = (0x1B, 0x9D, 0xCE, 255)  # DESIGN.md --blue-bright（只當底色、圖示）
RING = (0x2F, 0x28, 0x20, 255)  # DESIGN.md --heading


def badge(size: int) -> Image.Image:
    """米白圓角方塊，裡面四格視窗。畫在 4 倍大再縮小，邊緣才平滑。"""
    scale = 4
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ring = max(1, s // 20)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s // 4, fill=RING)
    d.rounded_rectangle([ring, ring, s - 1 - ring, s - 1 - ring], radius=s // 4 - ring, fill=CREAM)
    pad = s * 0.24
    gap = s * 0.05
    cell = (s - 2 * pad - gap) / 2
    for row in range(2):
        for col in range(2):
            x0 = pad + col * (cell + gap)
            y0 = pad + row * (cell + gap)
            d.rounded_rectangle([x0, y0, x0 + cell, y0 + cell], radius=cell * 0.12, fill=BLUE)
    return img.resize((size, size), Image.LANCZOS)


def main() -> int:
    if not REF.exists():
        print(f"找不到原版圖示：{REF}（要先 clone Textream 到 $HOME/textream-ref）", file=sys.stderr)
        return 1
    base = Image.open(REF).convert("RGBA")  # 1024×1024，圖示本體約佔中間 80%
    size = base.width
    mark = badge(int(size * 0.36))
    # 右下角，稍微壓在原圖的圓角方塊上
    base.alpha_composite(mark, (int(size * 0.60), int(size * 0.60)))

    OUT.mkdir(parents=True, exist_ok=True)
    base.resize((256, 256), Image.LANCZOS).save(OUT / "textream-windows.png")
    base.save(OUT / "textream-windows.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"已輸出 {OUT / 'textream-windows.ico'} 與 .png")
    return 0


if __name__ == "__main__":
    sys.exit(main())

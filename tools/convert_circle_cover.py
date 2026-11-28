"""
Muse Dash 1:1 Circular Cover Converter
SkiaSharp 기반 muse dash cover tool과 동일한 중앙 정사각형 크롭 및 안티앨리어싱 원형 마스크 적용
"""

import os
import shutil
from PIL import Image, ImageDraw

def convert_cover_to_circle(folder_path):
    cover_path = os.path.join(folder_path, "cover.png")
    orig_backup = os.path.join(folder_path, "cover_orig.png")

    if not os.path.exists(cover_path):
        print(f"cover.png not found in {folder_path}")
        return False

    if not os.path.exists(orig_backup):
        shutil.copy2(cover_path, orig_backup)
        print(f"Backed up original cover to {orig_backup}")

    src_img = Image.open(orig_backup).convert("RGBA")
    w, h = src_img.size
    size = min(w, h)

    left = (w - size) // 2
    top = (h - size) // 2
    cropped = src_img.crop((left, top, left + size, top + size))

    scale = 4
    mask_size = size * scale
    mask = Image.new('L', (mask_size, mask_size), 0)
    draw = ImageDraw.Draw(mask)
    draw.ellipse((0, 0, mask_size, mask_size), fill=255)
    mask = mask.resize((size, size), Image.Resampling.LANCZOS)

    result = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    result.paste(cropped, (0, 0), mask=mask)
    result.save(cover_path, format="PNG")
    print(f"[{os.path.basename(folder_path)}] 1:1 Circle Cover Generated ({size}x{size} RGBA)")
    return True

if __name__ == "__main__":
    targets = [
        r"H:\muse dash hwa\hwa\하얀 그리움",
        r"H:\muse dash hwa\hwa\미소의 세상 너에게만"
    ]
    for t in targets:
        if os.path.exists(t):
            convert_cover_to_circle(t)

"""Create contact sheets from actual WPF panel captures; requires Pillow."""
from pathlib import Path
import argparse
import math
from PIL import Image, ImageDraw, ImageFont, ImageOps


def gallery(source, destination, names, columns, width, height):
    margin = 28
    header = 70
    canvas = Image.new("RGB", (columns * width + 2 * margin,
                               math.ceil(len(names) / columns) * height + header + margin), "#efefef")
    draw = ImageDraw.Draw(canvas)
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    title = ImageFont.truetype(str(font_path), 25)
    caption = ImageFont.truetype(str(font_path), 15)
    draw.text((margin + 12, 20), "Tangerine for Windows · Tool panels", font=title, fill="#383838")
    for i, name in enumerate(names):
        image = Image.open(source / f"{name}.png").convert("RGBA")
        image = ImageOps.contain(image, (width - 12, height - 46), Image.Resampling.LANCZOS)
        x = margin + i % columns * width + (width - image.width) // 2
        y = header + i // columns * height
        canvas.paste(image, (x, y), image)
        text = name.replace("-", " ").title()
        draw.text((margin + i % columns * width + width // 2,
                   y + height - 26), text, font=caption, fill="#737373", anchor="mm")
    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, optimize=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    gallery(args.source, args.destination / "panels-overview.png",
            ["image-compress", "background", "crop-image", "trim-video", "channels", "organize-pdf"], 3, 430, 760)
    gallery(args.source, args.destination / "panels-all.png",
            [p.stem for p in sorted(args.source.glob("*.png"))], 5, 260, 480)

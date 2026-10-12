from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1] / "artifacts" / "backtest" / "regression"
sources = sorted(root.glob("*-2026-129.ppm"))
if not sources:
    raise SystemExit("No regression PPM files found.")

tile_size = 516
header = 56
tiles = []
for source in sources:
    image = Image.open(source).convert("RGB").resize((tile_size, tile_size), Image.Resampling.NEAREST)
    tile = Image.new("RGB", (tile_size, tile_size + header), (15, 20, 24))
    tile.paste(image, (0, header))
    draw = ImageDraw.Draw(tile)
    label = source.stem.split("-2026")[0].replace("rollingplains", "Rolling Plains").title()
    draw.text((18, 17), f"{label}  ·  seed 2026  ·  129x129", fill=(235, 242, 245), font=ImageFont.load_default(size=18))
    output = root / f"{source.stem}.png"
    tile.save(output, optimize=True)
    tiles.append(tile)

columns = 3
rows = (len(tiles) + columns - 1) // columns
gap = 16
title_height = 72
canvas = Image.new("RGB", (columns * tile_size + (columns + 1) * gap, title_height + rows * (tile_size + header) + (rows + 1) * gap), (7, 11, 14))
draw = ImageDraw.Draw(canvas)
draw.text((gap, 20), "PROCEDURAL WORLD · GEOLOGICAL PROFILES", fill=(240, 247, 250), font=ImageFont.load_default(size=28))
for index, tile in enumerate(tiles):
    x = gap + (index % columns) * (tile_size + gap)
    y = title_height + gap + (index // columns) * (tile_size + header + gap)
    canvas.paste(tile, (x, y))

comparison = root / "profiles-2026-comparison.png"
canvas.save(comparison, optimize=True)
print(comparison)

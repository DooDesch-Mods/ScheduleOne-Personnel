# Puts the screenshots of a foreign_npc_spots.py run into one labelled grid, so a whole run can be looked at at once:
#     python -I tests/contact_sheet.py <folder> <tag> <out.png>
# Rows are times of day (morning first), columns are spots (sorted by name).
import os
import sys

from PIL import Image, ImageDraw

folder, tag, out = sys.argv[1], sys.argv[2], sys.argv[3]
shots = sorted(f for f in os.listdir(folder) if f.startswith(tag + "-") and f.endswith(".png"))
if not shots:
    raise SystemExit(f"no shots for tag {tag} in {folder}")
keys = [f[len(tag) + 1:-4].split("-", 1) for f in shots]          # time, spot
rows = sorted({k[0] for k in keys}, key=lambda t: (int(t) + 1800) % 2400)
cols = sorted({k[1] for k in keys})
w, h = 640, 360
sheet = Image.new("RGB", (w * len(cols), (h + 24) * len(rows)), "black")
draw = ImageDraw.Draw(sheet)
for f, (t, spot) in zip(shots, keys):
    r, c = rows.index(t), cols.index(spot)
    img = Image.open(os.path.join(folder, f)).convert("RGB").resize((w, h))
    sheet.paste(img, (c * w, r * (h + 24) + 24))
    draw.text((c * w + 6, r * (h + 24) + 5), f"{tag} {t} {spot}", fill="yellow")
sheet.save(out)
print(f"{len(shots)} shots -> {out}")

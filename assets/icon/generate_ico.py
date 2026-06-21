"""Regenerates app.ico from icon.svg. Requires Inkscape on PATH (or update INKSCAPE below)."""
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

INKSCAPE = shutil.which("inkscape") or r"D:\Inkscape\bin\inkscape.com"
HERE = Path(__file__).parent
SVG = HERE / "icon.svg"
OUT_ICO = HERE.parent.parent / "src" / "Partition2MuseScore" / "Resources" / "app.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]

with tempfile.TemporaryDirectory() as tmp:
    tmp = Path(tmp)
    png_bytes = []
    for s in SIZES:
        png_path = tmp / f"{s}.png"
        subprocess.run(
            [INKSCAPE, str(SVG), "--export-type=png",
             f"--export-filename={png_path}", "-w", str(s), "-h", str(s)],
            check=True,
        )
        png_bytes.append(png_path.read_bytes())

entries = []
offset = 6 + 16 * len(SIZES)
for s, data in zip(SIZES, png_bytes):
    dim = 0 if s == 256 else s
    entries.append(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset))
    offset += len(data)

OUT_ICO.parent.mkdir(parents=True, exist_ok=True)
with open(OUT_ICO, "wb") as f:
    f.write(struct.pack("<HHH", 0, 1, len(SIZES)))
    for e in entries:
        f.write(e)
    for data in png_bytes:
        f.write(data)

print(f"wrote {OUT_ICO} with {len(SIZES)} frames")

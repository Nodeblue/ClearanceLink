# Builds src/ClearanceLink.Host/Assets/ClearanceLink.ico (the exe and window icon) from ClearanceLink.svg.
# Each size is drawn from the SVG, not scaled down from 256 px, so small sizes stay sharp.
# Usage: python3 -m pip install cairosvg pillow && python3 scripts/make-icon.py
import io
from pathlib import Path

import cairosvg
from PIL import Image

assets = Path(__file__).resolve().parent.parent / "src" / "ClearanceLink.Host" / "Assets"
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [Image.open(io.BytesIO(cairosvg.svg2png(url=str(assets / "ClearanceLink.svg"), output_width=s, output_height=s))).convert("RGBA")
          for s in sizes]
images[-1].save(assets / "ClearanceLink.ico", sizes=[(s, s) for s in sizes], append_images=images[:-1])
print(f"wrote {assets / 'ClearanceLink.ico'}")

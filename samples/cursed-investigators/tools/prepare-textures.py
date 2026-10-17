"""Build runtime copies from the preserved scans; requires Pillow, never edits originals."""
import hashlib
import json
from pathlib import Path

from PIL import Image, __version__

assets = Path(__file__).resolve().parents[1] / "assets"
manifest = json.loads((assets / "manifest.json").read_text(encoding="utf-8"))
target = assets / "runtime"
target.mkdir(exist_ok=True)
records = []
for asset in manifest["assets"]:
    source = assets / asset["path"]
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != asset["sha256"]:
        raise ValueError(f"Source hash changed: {source}")
    with Image.open(source) as image:
        height = min(image.height, 1600 if asset["category"] == "journal" else 1024)
        size = (round(image.width * height / image.height), height)
        # Premultiplied resampling avoids dark/white fringes around transparent card corners.
        result = image.convert("RGBa").resize(size, Image.Resampling.LANCZOS).convert("RGBA")
        destination = target / (asset["id"] + ".png")
        result.save(destination, optimize=True)
    records.append({"id": asset["id"], "source": asset["path"], "sourceSha256": digest,
                    "path": destination.relative_to(assets).as_posix(), "size": list(size),
                    "sha256": hashlib.sha256(destination.read_bytes()).hexdigest()})
(target / "manifest.json").write_text(json.dumps({"format": 1, "pillow": __version__,
    "method": "premultiplied RGBA Lanczos; preserved source scans", "assets": records},
    ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Prepared {len(records)} runtime textures; {sum((assets / r['path']).stat().st_size for r in records):,} bytes")

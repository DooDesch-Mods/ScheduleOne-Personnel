"""Makes the re-encoded variants of the sample cards that a card has to survive (or must be detected as damaged).

Usage: python -I survival.py <dir>   (dir holds small.png and large.png from `CardCodec.Tests write`)

Lossless variants keep the card readable; lossy ones are named *.expect-<error>.png so `CardCodec.Tests check`
knows the error it must report.
"""
import struct
import subprocess
import sys
import zlib
from pathlib import Path

from PIL import Image


def ffmpeg(src: Path, dst: Path, *args: str) -> None:
    result = subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(src), *args, str(dst)],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"ffmpeg failed for {dst.name}: {result.stderr.strip()}")
    print(f"made {dst.name}")


def write_rgb48(img: Image.Image, dst: Path) -> None:
    """Exact 8 -> 16 bit conversion (v * 257), what a lossless editor does when it raises the bit depth.

    ffmpeg's rgb48 path is not used here: its colour scaler changes some values by one, which is a lossy edit.
    """
    w, h = img.size
    px = img.convert("RGB").tobytes()
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        row = px[y * w * 3:(y + 1) * w * 3]
        raw += b"".join(struct.pack(">H", v * 257) for v in row)

    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    data = bytes([137, 80, 78, 71, 13, 10, 26, 10]) + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 16, 2, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b"")
    dst.write_bytes(data)
    print(f"made {dst.name}")


def main() -> int:
    root = Path(sys.argv[1])
    for name in ("small", "large"):
        src = root / f"{name}.png"
        img = Image.open(src)
        img.load()

        img.save(root / f"{name}.pil-optimize.png", optimize=True)
        print(f"made {name}.pil-optimize.png")
        img.convert("RGBA").save(root / f"{name}.pil-rgba.png")
        print(f"made {name}.pil-rgba.png")
        write_rgb48(img, root / f"{name}.rgb48.png")
        ffmpeg(src, root / f"{name}.ffmpeg-rgba.png", "-pix_fmt", "rgba")

        # Lossless WebP and back: what an image host does when it re-encodes without loss.
        webp = root / f"{name}.lossless.webp"
        img.save(webp, lossless=True, quality=100)
        Image.open(webp).save(root / f"{name}.webp-lossless-back.png")
        webp.unlink()
        print(f"made {name}.webp-lossless-back.png")

        # Lossy re-encodes: the card must report damage, not garbage.
        jpg = root / f"{name}.q95.jpg"
        img.convert("RGB").save(jpg, quality=95)
        Image.open(jpg).save(root / f"{name}.jpeg-back.expect-lost.png")
        jpg.unlink()
        print(f"made {name}.jpeg-back.expect-lost.png")
        img.resize((img.width // 2, img.height // 2)).save(root / f"{name}.half.expect-lost.png")
        print(f"made {name}.half.expect-lost.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""The app's icons come from the Nexus family kit (NexusCommunication/icones, the icons of julienpiron.fr),
copied as they are, never redrawn. Rule of the kit: 44 px and more, the mark (the pictogram in the medallion of
julienpiron.fr); under 44 px, the round (the same medallion cut round, transparent corners; the pictogram alone is
never shown, Julien 27/10). NexusSystem: the cyan chip with the π.

- Assets/nexus<product>-round.png, 96 px (navigation, title bar: WinUI's SvgImageSource does not draw the medallion,
  an image embedded in the SVG) and Assets/nexus<product>-mark.png (cards, page headers),
  for NexusSystem and each product;
- the package's logos at every scale Windows asks for (scale-100, 125, 150, 200, 400): Square150x150Logo, StoreLogo and
  Wide310x150Logo from the mark; Square44x44Logo (the small-icon slot) from the round, at every scale and every
  targetsize (taskbar, Start, Alt+Tab, Explorer), plated, unplated and light-unplated, so Windows never enlarges or
  mixes shapes;
- Assets/nexus.ico for Windows: the round under 44 px, the mark from 48 px.
Also writes a sheet of the small sizes, enlarged, to check how readable they are.

    python -B tools/icon_from_site.py [kit folder] [sheet.png]"""

import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Nexus.App" / "Assets"
KIT = ROOT.parent / "NexusCommunication" / "icones"
NAMES = ["system", "windows", "android", "browser", "keypad", "relay", "agymail"]
ICO_SMALL = [16, 20, 24, 32, 40]  # the round
ICO_LARGE = [48, 64, 256]  # the mark
SCALES = [100, 125, 150, 200, 400]  # .scale-N: display scaling
TARGET_SIZES = [16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256]  # Square44x44Logo.targetsize-N


def png(kit: Path, name: str, kind: str, size: int) -> Image.Image:
    """The kit's export at the nearest size at or above, reduced if needed."""
    sizes = sorted(int(p.stem.rsplit("-", 1)[1]) for p in (kit / "png" / f"nexus{name}-{kind}").glob("*.png"))
    best = next((s for s in sizes if s >= size), sizes[-1])
    image = Image.open(kit / "png" / f"nexus{name}-{kind}" / f"nexus{name}-{kind}-{best}.png").convert("RGBA")
    return image if best == size else image.resize((size, size), Image.LANCZOS)


def main() -> None:
    kit = Path(sys.argv[1]) if len(sys.argv) > 1 else KIT
    for name in NAMES:
        (ASSETS / f"nexus{name}-icon.svg").unlink(missing_ok=True)  # the pictogram alone, no longer shown
        png(kit, name, "round", 96).save(ASSETS / f"nexus{name}-round.png")
        png(kit, name, "mark", 256).save(ASSETS / f"nexus{name}-mark.png")
    for old in [*ASSETS.glob("Square*Logo*.png"), *ASSETS.glob("StoreLogo*.png"), *ASSETS.glob("Wide310x150Logo*.png")]:
        old.unlink()  # one generation at a time: an unqualified file would clash with its scale-100 variant
    for scale in SCALES:
        k = scale / 100
        png(kit, "system", "mark", round(150 * k)).save(ASSETS / f"Square150x150Logo.scale-{scale}.png")
        png(kit, "system", "mark", round(50 * k)).save(ASSETS / f"StoreLogo.scale-{scale}.png")
        png(kit, "system", "round", round(44 * k)).save(ASSETS / f"Square44x44Logo.scale-{scale}.png")
        wide = Image.new("RGBA", (round(310 * k), round(150 * k)), (0, 0, 0, 0))
        wide.alpha_composite(png(kit, "system", "mark", round(150 * k)), (round(80 * k), 0))
        wide.save(ASSETS / f"Wide310x150Logo.scale-{scale}.png")
    for size in TARGET_SIZES:
        image = png(kit, "system", "round", size)
        for form in ("", "_altform-unplated", "_altform-lightunplated"):
            image.save(ASSETS / f"Square44x44Logo.targetsize-{size}{form}.png")
    icons = [png(kit, "system", "round", s) for s in ICO_SMALL] + [png(kit, "system", "mark", s) for s in ICO_LARGE]
    icons[-1].save(ASSETS / "nexus.ico", sizes=[(i.width, i.height) for i in icons], append_images=icons[:-1])
    if len(sys.argv) > 2:
        # 16, 24, 32 and 48 px as Windows shows them, then enlarged 8 times (each pixel a square), on light and dark.
        small = [16, 24, 32, 48]
        cell = 48 * 8 + 24
        sheet = Image.new("RGBA", (cell * len(small), cell * 2), (0, 0, 0, 0))
        for row, background in enumerate([(243, 243, 243, 255), (32, 32, 32, 255)]):
            for col, size in enumerate(small):
                tile = Image.new("RGBA", (cell, cell), background)
                kind = "round" if size < 44 else "mark"
                icon = png(kit, "system", kind, size).resize((size * 8, size * 8), Image.NEAREST)
                tile.alpha_composite(icon, ((cell - size * 8) // 2, (cell - size * 8) // 2))
                sheet.paste(tile, (col * cell, row * cell))
        sheet.save(sys.argv[2])
    print(f"icons from {kit}")


if __name__ == "__main__":
    main()

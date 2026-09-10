#!/usr/bin/env python3
"""Чинит обрезанные сабсеты шрифтов из mutool extract для браузеров.

mutool достает из PDF встроенные сабсеты:
- без таблицы OS/2 -> OTS: "missing required table";
- только с MacRoman-cmap -> браузер не мапит символы по Unicode;
- иногда с битым post 2.0 (лишние байты) -> OTS бракует.

Скрипт: достраивает OS/2 v0 из метрик, добавляет Unicode-cmap,
схлопывает post в 3.0. Имена глифов браузеру не нужны.
Использование: repair_font.py <in.ttf> [<out.ttf>]  (по умолчанию на месте)
"""
import sys

from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._c_m_a_p import CmapSubtable
from fontTools.ttLib.tables.O_S_2f_2 import Panose


def fix_os2(font, src):
    for required in ("head", "hhea", "hmtx", "maxp", "cmap"):
        if required not in font:
            return False

    head = font["head"]
    hhea = font["hhea"]
    hmtx = font["hmtx"]
    upm = head.unitsPerEm or 1000
    bold = "bold" in src.lower()

    advances = [adv for adv, _ in hmtx.metrics.values()]
    avg_advance = int(sum(advances) / len(advances)) if advances else upm // 2

    codepoints = set()
    for table in font["cmap"].tables:
        if table.isUnicode():
            codepoints.update(table.cmap.keys())

    os2 = newTable("OS/2")
    os2.version = 0
    os2.xAvgCharWidth = avg_advance
    os2.usWeightClass = 700 if bold else 400
    os2.usWidthClass = 5
    os2.fsType = 0
    os2.ySubscriptXSize = int(upm * 0.65)
    os2.ySubscriptYSize = int(upm * 0.70)
    os2.ySubscriptXOffset = 0
    os2.ySubscriptYOffset = int(upm * 0.14)
    os2.ySuperscriptXSize = int(upm * 0.65)
    os2.ySuperscriptYSize = int(upm * 0.70)
    os2.ySuperscriptXOffset = 0
    os2.ySuperscriptYOffset = int(upm * 0.48)
    os2.yStrikeoutSize = max(1, int(upm * 0.05))
    os2.yStrikeoutPosition = int(upm * 0.25)
    os2.sFamilyClass = 0
    os2.panose = Panose()
    os2.ulUnicodeRange1 = 0
    os2.ulUnicodeRange2 = 0
    os2.ulUnicodeRange3 = 0
    os2.ulUnicodeRange4 = 0
    os2.achVendID = "NONE"
    os2.fsSelection = 0x20 if bold else 0x40
    os2.usFirstCharIndex = min(codepoints) if codepoints else 0
    os2.usLastCharIndex = max(codepoints) if codepoints else 0
    os2.sTypoAscender = hhea.ascent
    os2.sTypoDescender = hhea.descent
    os2.sTypoLineGap = hhea.lineGap
    os2.usWinAscent = max(0, hhea.ascent)
    os2.usWinDescent = max(0, -hhea.descent)
    os2.ulCodePageRange1 = 0
    os2.ulCodePageRange2 = 0

    font["OS/2"] = os2
    return True


def fix_post(font):
    post = font["post"]
    if post.formatType == 3.0:
        return False
    post.formatType = 3.0
    post.mapping = {}
    post.extraNames = []
    post.glyphOrder = font.getGlyphOrder()
    return True


def fix_cmap(font):
    cmap = font["cmap"]
    has_unicode = any(
        (t.platformID, t.platEncID) in ((3, 1), (0, 3), (3, 10)) for t in cmap.tables
    )
    if has_unicode:
        return False
    mac = next(
        (t for t in cmap.tables if (t.platformID, t.platEncID) == (1, 0)), None
    )
    if mac is None:
        return False
    sub = CmapSubtable.newSubtable(4)
    sub.platformID = 3
    sub.platEncID = 1
    sub.language = 0
    sub.cmap = {}
    for code, glyph in mac.cmap.items():
        try:
            ch = bytes([code]).decode("mac_roman")
        except (ValueError, UnicodeDecodeError):
            continue
        sub.cmap[ord(ch)] = glyph
    cmap.tables.append(sub)
    return True


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else sys.argv[1]

    font = TTFont(src)
    fixed = []
    if "OS/2" not in font:
        if not fix_os2(font, src):
            print(f"Нет обязательных таблиц — починить нельзя: {src}")
            return 1
        fixed.append("OS/2")
    if fix_post(font):
        fixed.append("post")
    if fix_cmap(font):
        fixed.append("cmap")

    font.save(dst)
    print(f"OK ({', '.join(fixed) if fixed else 'уже ок'}): {dst}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

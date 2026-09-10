#!/usr/bin/env python3
"""Перестраивает Unicode-cmap сабсета по ToUnicode CMap из PDF.

Проблема: у символических шрифтов (FontAwesome) в PDF нет /Encoding,
есть только /ToUnicode. Рендер идет по встроенной карте байт->глиф,
а символы PdfPig берет из ToUnicode. Мапить через MacRoman (как раньше) —
неверно: получаются перепутанные символы (кость вместо пива).

Скрипт: читает ToUnicode-поток шрифта /BaseFont из PDF (bfchar/bfrange),
для каждого байта берет глиф из встроенной MacRoman-карты сабсета
и строит Unicode-cmap unicode(ToUnicode)->глиф.
Без --pdf/--font работает как раньше (MacRoman-синтез).

Использование:
  rebuild_cmap.py <in.ttf> [<out.ttf>] [--pdf FILE.pdf --font BaseFontName]
"""
import re
import sys

from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._c_m_a_p import CmapSubtable


def parse_tounicode(data: bytes):
    """Возвращает {byte_code: unicode_char} из ToUnicode CMap."""
    text = data.decode("latin-1")
    mapping = {}

    def hexstr(s):
        s = s.strip("<>")
        return bytes.fromhex(s)

    for block in re.findall(
        r"beginbfchar(.*?)endbfchar", text, re.DOTALL | re.IGNORECASE
    ):
        for src, dst in re.findall(r"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", block):
            src_b = hexstr(src)
            if len(src_b) != 1:
                continue
            try:
                ch = hexstr(dst).decode("utf-16-be")
            except (ValueError, UnicodeDecodeError):
                continue
            if ch:
                mapping[src_b[0]] = ch[0]

    for block in re.findall(
        r"beginbfrange(.*?)endbfrange", text, re.DOTALL | re.IGNORECASE
    ):
        for m in re.finditer(
            r"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|\[(.*?)\])",
            block,
        ):
            lo_b, hi_b, dst_single, dst_array = m.groups()
            lo_b, hi_b = hexstr(lo_b), hexstr(hi_b)
            if len(lo_b) != 1 or len(hi_b) != 1:
                continue
            lo, hi = lo_b[0], hi_b[0]
            if dst_single is not None:
                try:
                    base = int.from_bytes(hexstr(dst_single), "big")
                    width = len(hexstr(dst_single))
                except ValueError:
                    continue
                for code in range(lo, hi + 1):
                    try:
                        ch = (base + code - lo).to_bytes(
                            width, "big"
                        ).decode("utf-16-be")
                    except (OverflowError, ValueError, UnicodeDecodeError):
                        continue
                    mapping[code] = ch[0] if ch else mapping.get(code)
                    if code in mapping and mapping[code] is None:
                        del mapping[code]
            else:
                items = re.findall(r"<([0-9A-Fa-f]+)>", dst_array or "")
                for i, item in enumerate(items):
                    if lo + i > hi:
                        break
                    try:
                        ch = hexstr(item).decode("utf-16-be")
                    except (ValueError, UnicodeDecodeError):
                        continue
                    if ch:
                        mapping[lo + i] = ch[0]
    return mapping


def load_tounicode(pdf_path, base_font):
    import pikepdf

    pdf = pikepdf.open(pdf_path)
    for page in pdf.pages:
        fonts = page.get("/Resources", {}).get("/Font", {})
        for _, ref in fonts.items():
            if str(ref.get("/BaseFont", "")).lstrip("/") != base_font:
                continue
            tu = ref.get("/ToUnicode")
            if tu is None:
                continue
            return parse_tounicode(tu.read_bytes())
    return {}


def main() -> int:
    args = sys.argv[1:]
    if not args or "-h" in args or "--help" in args:
        print(__doc__)
        return 2

    pdf_path = None
    base_font = None
    if "--pdf" in args:
        pdf_path = args[args.index("--pdf") + 1]
    if "--font" in args:
        base_font = args[args.index("--font") + 1]
    positionals = [a for a in args if not a.startswith("--") and a not in
                   ([pdf_path, base_font] if pdf_path else [])]
    src = positionals[0]
    dst = positionals[1] if len(positionals) > 1 else src

    font = TTFont(src)
    if "cmap" not in font:
        print(f"Нет cmap: {src}")
        return 1

    if not pdf_path or not base_font:
        print(f"Без --pdf/--font нечего перестраивать: {src}")
        return 2

    try:
        to_unicode = load_tounicode(pdf_path, base_font)
    except ImportError:
        print("Нет pikepdf — пропускаю")
        return 0
    if not to_unicode:
        print(f"ToUnicode для {base_font} не найден — оставляю как есть")
        return 0

    mac = next(
        (t for t in font["cmap"].tables if (t.platformID, t.platEncID) == (1, 0)),
        None,
    )
    if mac is None:
        print(f"Нет MacRoman-карты: {src}")
        return 1

    # Сносим ранее синтезированные Unicode-таблицы, строим по ToUnicode.
    font["cmap"].tables = [
        t for t in font["cmap"].tables
        if (t.platformID, t.platEncID) not in ((3, 1), (0, 3))
    ]
    sub = CmapSubtable.newSubtable(4)
    sub.platformID = 3
    sub.platEncID = 1
    sub.language = 0
    sub.cmap = {}
    missed = 0
    for code, ch in sorted(to_unicode.items()):
        glyph = mac.cmap.get(code)
        if glyph is None:
            missed += 1
            continue
        sub.cmap[ord(ch)] = glyph
    font["cmap"].tables.append(sub)

    font.save(dst)
    print(f"OK (ToUnicode: {len(sub.cmap)} маппингов, без глифа: {missed}): {dst}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

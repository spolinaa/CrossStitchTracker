#!/usr/bin/env python3
"""Разбирает ключ x-floss.

Формат: строки слева. В строке y-cod (код) и y-sym (символ) — у них
может различаться y (базовая линия зависит от шрифта), поэтому пары
собираем жадным матчингом по |dy|: все кандидаты сортируем по расстоянию,
каждый символ и каждый код используем не более раза.

Символ — одиночный знак в левой колонке (x < 90), цифры тоже допускаются.
Код — токен из колонки кодов (60 < x < 180): цифры/буквы/дефис, либо
именованные коды без цифр (Blanc, Ecru). Из «грязных» спанов код
вытаскиваем регексом (например "310 (x2)" -> "310").

Символы бывают двух видов:
- текстовые глифы из awesome-шрифтов: обычный текст (span["text"]);
- значки-кривые или растровые картинки без текста: текста нет, но есть
  векторный рисунок или картинка в зоне символа — такие пары тоже отдаём,
  вместо symbol кладём пустую строку + поле raw с местом (страница и y),
  код при этом всё равно извлекается.

Сравнение идёт по ЦЕНТРУ строк по y (а не по верху bbox): у разных шрифтов
базовая линия гуляет, а центр стабильнее.

Вход: --pdf FILE [--pages 1,3]
Выход: --out out.json {"pairs": [{"symbol","font","code","raw"}],
                       "debug": ["стр.1: ...", ...]}
"""
import json
import re
import sys

import pymupdf

SYM_X_MAX = 90
CODE_X_MIN = 60
CODE_X_MAX = 180
DY_TOL = 14
DY_TOL_WIDE = 20

NAMED_CODES = {"blanc", "ecru"}

CODE_RE = re.compile(r"[A-Za-z]{0,4}\d[A-Za-z0-9./-]*")


def main() -> int:
    args = sys.argv[1:]
    get = lambda name, d=None: args[args.index(name) + 1] if name in args else d
    pdf_path = get("--pdf")
    out_path = get("--out")
    if not pdf_path or not out_path:
        print(__doc__)
        return 2
    pages = None
    if get("--pages"):
        pages = {int(x.strip()) for x in get("--pages").split(",") if x.strip()}

    doc = pymupdf.open(pdf_path)
    pairs = []
    debug = []

    for pno in range(len(doc)):
        page_no = pno + 1
        if pages is not None and page_no not in pages:
            continue
        page = doc[pno]

        spans = []
        for block in page.get_text("dict")["blocks"]:
            for line in block.get("lines", []):
                for s in line["spans"]:
                    x0, y0, x1, y1 = s["bbox"]
                    t = s["text"].strip()
                    if not t:
                        continue
                    # центр строки — стабильнее верха bbox при разных шрифтах
                    spans.append((round(x0, 1), round((y0 + y1) / 2, 1), t, s["font"]))

        symbols = []
        codes = []
        for x, yc, t, font in spans:
            if x < SYM_X_MAX and len(t) == 1:
                symbols.append((yc, x, t, font))
            elif CODE_X_MIN < x < CODE_X_MAX:
                code = _extract_code(t)
                if code:
                    codes.append((yc, x, code))
                elif len(t) == 1 and x < SYM_X_MAX:
                    symbols.append((yc, x, t, font))

        # Только компактные рисунки/картинки из зоны символа:
        # длинные линейки сетки таблицы отфильтровываем по размеру.
        art = [r for r in _rects(page.get_drawings()) + _image_rects(page)
               if r[0] < SYM_X_MAX and r[2] > 8
               and 1 < (r[2] - r[0]) < 70 and 1 < (r[3] - r[1]) < 70]

        # Проход 1: строгий допуск. Проход 2: широкий — для остатков.
        used_s, used_c = set(), set()
        page_pairs = 0
        for tol in (DY_TOL, DY_TOL_WIDE):
            cands = []
            for si, (ys, _xs, _sym, _font) in enumerate(symbols):
                if si in used_s:
                    continue
                for ci, (yc, _xc, _code) in enumerate(codes):
                    if ci in used_c:
                        continue
                    d = abs(ys - yc)
                    if d <= tol:
                        cands.append((d, si, ci))
            cands.sort(key=lambda c: c[0])
            for _d, si, ci in cands:
                if si in used_s or ci in used_c:
                    continue
                used_s.add(si)
                used_c.add(ci)
                _ys, _xs, sym, font = symbols[si]
                _yc, _xc, code = codes[ci]
                pairs.append({"symbol": sym, "font": font, "code": code, "raw": ""})
                page_pairs += 1

        # Коды без текстового символа рядом: ищем рисунок/картинку слева
        # на том же y — это значок-кривые. Код всё равно отдаём.
        art_hits = 0
        for idx, (yc, _xc, code) in enumerate(codes):
            if idx in used_c:
                continue
            has_art = any(
                abs((y0 + y1) / 2 - yc) <= DY_TOL
                for x0, y0, x1, y1 in art
            )
            if has_art:
                pairs.append({"symbol": "", "font": "",
                              "code": code, "raw": f"p{page_no}:y{yc}"})
                used_c.add(idx)
                art_hits += 1

        # Диагностика по каждому коду без пары: ближайшие кандидаты.
        missing = []
        for idx, (yc, xc, code) in enumerate(codes):
            if idx in used_c:
                continue
            near_sym = [(abs(ys - yc), round(xs, 1), sym)
                        for si, (ys, xs, sym, _f) in enumerate(symbols)
                        if si not in used_s]
            near_sym.sort(key=lambda n: n[0])
            near_art = [abs((y0 + y1) / 2 - yc) for x0, y0, x1, y1 in art]
            near_art.sort()
            s_txt = (f"символ dy={near_sym[0][0]:.1f} x={near_sym[0][1]} {near_sym[0][2]!r}"
                     if near_sym else "символов нет")
            a_txt = f"рисунок dy={near_art[0]:.1f}" if near_art else "рисунков нет"
            missing.append(f"{code}@{yc} ({s_txt}; {a_txt}; xкод={xc})")

        page_pairs += art_hits
        msg = (f"стр.{page_no}: спанов {len(spans)}, символов {len(symbols)}, "
               f"кодов {len(codes)}, пар {page_pairs}")
        if missing:
            msg += f", БЕЗ ПАРЫ [{'; '.join(missing)}]"
        debug.append(msg)

    # дедуп по (symbol, font, code)
    seen = set()
    out = []
    for p in pairs:
        k = (p["symbol"], p["font"], p["code"])
        if k in seen:
            continue
        seen.add(k)
        out.append(p)

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"pairs": out, "debug": debug}, f, ensure_ascii=False)
    print(f"OK: {out_path} pairs={len(out)} debug={debug}")
    return 0


def _rects(drawings):
    rects = []
    try:
        for d in drawings:
            r = d["rect"]
            rects.append((round(r.x0, 1), round(r.y0, 1),
                          round(r.x1, 1), round(r.y1, 1)))
    except Exception:
        pass
    return rects


def _image_rects(page):
    rects = []
    try:
        for img in page.get_images(full=True):
            try:
                for r in page.get_image_rects(img[0]):
                    rects.append((round(r.x0, 1), round(r.y0, 1),
                                  round(r.x1, 1), round(r.y1, 1)))
            except Exception:
                continue
    except Exception:
        pass
    return rects


def _extract_code(t: str) -> str:
    if _is_code(t):
        return t
    m = CODE_RE.search(t)
    if m and _is_code(m.group(0)):
        return m.group(0)
    return ""


def _is_code(v: str) -> bool:
    if not v or len(v) > 12:
        return False
    if any(c.isspace() for c in v):
        return False
    if v.lower() in NAMED_CODES:
        return True
    if not any(c.isdigit() for c in v):
        return False
    return all(c.isalnum() or c in "-/." for c in v)


if __name__ == "__main__":
    raise SystemExit(main())

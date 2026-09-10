#!/usr/bin/env python3
"""Замеряет фон каждой клетки схемы из векторных заливок PDF.

Фоны клеток лежат залитыми прямоугольниками (~10x10pt) поверх белого
листа. Квадратики сортируются построчно и мапятся 1:1 на клетки нашей
сетки той же PDF-страницы (порядок row-major с обеих сторон).
Совпадение строго проверяется (число строк/столбцов); иначе — null
и фронт рисует авто-подложки.

Вход: --pdf FILE --cells cells.json --out out.json
cells.json: [{"key": "0:3:5", "pdfPage": 1, "x":.., "y":.., ...}, ...]
  x/y — индексы в НАШЕЙ сетке (не пункты!). Геометрия не нужна.
out.json: {"0:3:5": "#rrggbb" | null, ...}
"""
import json
import sys


def main() -> int:
    args = sys.argv[1:]
    get = lambda name: args[args.index(name) + 1] if name in args else None
    pdf_path = get("--pdf")
    cells_path = get("--cells")
    out_path = get("--out")
    if not pdf_path or not cells_path or not out_path:
        print(__doc__)
        return 2

    import pymupdf

    with open(cells_path, encoding="utf-8") as f:
        cells = json.load(f)

    by_page: dict = {}
    for c in cells:
        by_page.setdefault(c["pdfPage"], []).append(c)

    result = {}
    doc = pymupdf.open(pdf_path)
    for pdf_page, items in sorted(by_page.items()):
        # Наша сетка этой PDF-страницы.
        ours = sorted(items, key=lambda c: (c["y"], c["x"]))
        our_rows = max(c["y"] for c in ours) + 1
        our_cols = max(c["x"] for c in ours) + 1

        page = doc[pdf_page - 1]
        fills = [
            dr["rect"]
            for dr in page.get_drawings()
            if dr.get("fill") is not None
            and 5 < (dr["rect"].x1 - dr["rect"].x0) < 15
            and 5 < (dr["rect"].y1 - dr["rect"].y0) < 15
        ]
        fills.sort(key=lambda r: (r.y0, r.x0))

        # Кластеризуем в строки по y (разрыв > 5pt — новая строка).
        rows = []
        for r in fills:
            if rows and r.y0 - rows[-1][-1].y0 < 5:
                rows[-1].append(r)
            else:
                rows.append([r])
        for r in rows:
            r.sort(key=lambda q: q.x0)

        ok = (
            len(rows) == our_rows
            and all(len(r) == our_cols for r in rows)
        )
        if not ok:
            print(
                f"Стр. {pdf_page}: сетка заливок "
                f"{len(rows)}x{[len(r) for r in rows][:3]}... != "
                f"нашей {our_rows}x{our_cols} — без цветов"
            )
            for c in items:
                result[c["key"]] = None
            continue

        for (cell, rect) in zip(ours, [q for r in rows for q in r]):
            # Цвет заливки под клеткой: последняя в потоке уже выбрана
            # порядком (заливки идут построчно); берем напрямую.
            result[cell["key"]] = None  # заполним ниже

        # Перечитываем с цветами (порядок тот же).
        rects = [q for r in rows for q in r]
        # Нужны сами цвета: второй проход по drawings в том же порядке.
        ordered = []
        seen_rows = []
        for r in rows:
            ordered.extend(r)
        # Сопоставляем rect -> fill через тот же список drawings:
        fills_all = [
            (dr["rect"], dr.get("fill"))
            for dr in page.get_drawings()
            if dr.get("fill") is not None
            and 5 < (dr["rect"].x1 - dr["rect"].x0) < 15
            and 5 < (dr["rect"].y1 - dr["rect"].y0) < 15
        ]
        fills_all.sort(key=lambda t: (t[0].y0, t[0].x0))
        by_rect = {(round(r.x0, 1), round(r.y0, 1)): f for r, f in fills_all}
        for cell, rect in zip(ours, ordered):
            f = by_rect.get((round(rect.x0, 1), round(rect.y0, 1)), (1, 1, 1))
            result[cell["key"]] = "#{:02x}{:02x}{:02x}".format(
                int(f[0] * 255), int(f[1] * 255), int(f[2] * 255)
            )

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f)
    got = sum(1 for v in result.values() if v)
    print(f"OK: {got}/{len(result)} фонов замерено")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

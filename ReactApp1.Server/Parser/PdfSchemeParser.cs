using Tabula;
using Tabula.Extractors;
using UglyToad.PdfPig;

namespace PatternTracker.Server.Parser;

/// <summary>
/// Порт парсера из WebPatternTracker (PdfPig + Tabula).
/// Убраны Windows-зависимости (mutool/FontForge/Aspose) — шрифты пока не извлекаем,
/// храним только имя шрифта символа. Пути — через Path.GetTempPath (кроссплатформенно).
/// </summary>
public static class PdfSchemeParser
{
    public const int OnePageHeight = 70;
    public const int OnePageWidth = 50;

    public static ParsedScheme Parse(string filePath, int height, int width)
    {
        var scheme = new ParsedScheme { Width = width, Height = height };
        var colorIndexByKey = new Dictionary<(string Symbol, string Font), int>();

        int GetOrAddColor(string symbol, string font)
        {
            var key = (symbol, font);
            if (colorIndexByKey.TryGetValue(key, out var idx))
                return idx;
            idx = scheme.Colors.Count;
            colorIndexByKey[key] = idx;
            scheme.Colors.Add(new ParsedColor(symbol, font));
            return idx;
        }

        int parsedHeight = 0;
        int parsedWidth = 0;
        int pageIndex = 0;

        using var document = PdfDocument.Open(filePath, new ParsingOptions { ClipPaths = true });
        var extractor = new ObjectExtractor(document);
        IExtractionAlgorithm algorithm = new SpreadsheetExtractionAlgorithm();

        for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            if (parsedHeight >= height)
                break;

            var page = extractor.Extract(pageNum);
            var tables = algorithm.Extract(page);
            if (tables.Count == 0)
                continue;

            var table = tables[0];
            if (table.Rows.Count < OnePageHeight)
                continue;

            int heightToParse = Math.Min(height - parsedHeight, OnePageHeight);
            int widthToParse = Math.Min(width - parsedWidth, OnePageWidth);

            ParseTable(table, heightToParse, widthToParse, pageIndex, parsedWidth, pageNum, scheme, GetOrAddColor);
            parsedWidth += widthToParse;

            if (parsedWidth >= width)
            {
                parsedHeight += heightToParse;
                parsedWidth = 0;
                pageIndex++;
            }
        }

        return scheme;
    }

    public static ParsedScheme ParseAuto(string filePath, IProgress<PageProgress>? progress = null)
    {
        var scheme = new ParsedScheme();
        var colorIndexByKey = new Dictionary<(string Symbol, string Font), int>();

        int GetOrAddColor(string symbol, string font)
        {
            var key = (symbol, font);
            if (colorIndexByKey.TryGetValue(key, out var idx))
                return idx;
            idx = scheme.Colors.Count;
            colorIndexByKey[key] = idx;
            scheme.Colors.Add(new ParsedColor(symbol, font));
            return idx;
        }

        int pageIndex = 0;

        using var document = PdfDocument.Open(filePath, new ParsingOptions { ClipPaths = true });
        var extractor = new ObjectExtractor(document);
        IExtractionAlgorithm algorithm = new SpreadsheetExtractionAlgorithm();
        int totalPages = document.NumberOfPages;

        for (int pageNum = 1; pageNum <= totalPages; pageNum++)
        {
            progress?.Report(new PageProgress(pageNum, totalPages));
            var page = extractor.Extract(pageNum);
            var tables = algorithm.Extract(page);
            if (tables.Count == 0)
                continue;

            // Берем самую большую таблицу на странице (сетка схемы, а не легенда).
            var table = tables.OrderByDescending(t => t.Rows.Count).First();
            var dataRows = new List<List<(int ColorIndex, Tabula.Cell Source)>>();

            foreach (var row in table.Rows)
            {
                var cells = ParseRowAuto(row, GetOrAddColor);
                if (cells.Count > 0)
                    dataRows.Add(cells);
            }

            // Эвристика: страница легенды/инструкции дает мало строк/столбцов — пропускаем.
            if (dataRows.Count < 10)
                continue;
            int cols = dataRows.Max(r => r.Count);
            if (cols < 10)
                continue;

            int empty = GetOrAddColor("EMPTY", string.Empty);
            for (int y = 0; y < dataRows.Count; y++)
            {
                var row = dataRows[y];
                for (int x = 0; x < cols; x++)
                {
                    if (x < row.Count)
                    {
                        var (colorIndex, src) = row[x];
                        scheme.Cells.Add(new ParsedCell(pageIndex, x, y, colorIndex)
                        {
                            PdfPage = pageNum,
                            PdfX = src.X,
                            PdfY = src.Y,
                            PdfW = src.Width,
                            PdfH = src.Height
                        });
                    }
                    else
                    {
                        scheme.Cells.Add(new ParsedCell(pageIndex, x, y, empty));
                    }
                }
            }

            scheme.Sheets.Add(new ParsedSheet(pageIndex, pageNum, cols, dataRows.Count));
            scheme.Width = Math.Max(scheme.Width, cols);
            scheme.Height = Math.Max(scheme.Height, dataRows.Count);
            pageIndex++;
        }

        return scheme;
    }

    private enum CellKind
    {
        Empty,
        Symbol,
        Garbage
    }

    /// <summary>
    /// Достает символ клетки вместе со шрифтом ИМЕННО символа.
    /// В PDF узкие глифы дополнены пробелом для центровки, причем пробел
    /// часто в другом шрифте — брать шрифт первого элемента (как раньше)
    /// означало приписать символу чужой шрифт (кость превращалась в пиво).
    /// </summary>
    private static (CellKind Kind, string Symbol, string Font) ParseCellContent(Tabula.Cell cell)
    {
        string symbol = string.Empty;
        string font = string.Empty;
        int count = 0;

        foreach (var te in cell.TextElements)
        {
            // TextElement == одна буква: текст через GetText(), шрифт через Font.
            foreach (var letter in te.TextElements)
            {
                var v = letter.GetText()?.Trim();
                if (string.IsNullOrEmpty(v))
                    continue;
                if (v.Length > 1)
                    return (CellKind.Garbage, string.Empty, string.Empty);
                count++;
                if (count > 1)
                    return (CellKind.Garbage, string.Empty, string.Empty);
                symbol = v;
                font = letter.Font.Name;
            }
        }

        return count == 0
            ? (CellKind.Empty, string.Empty, string.Empty)
            : (CellKind.Symbol, symbol, font);
    }

    private static List<(int ColorIndex, Tabula.Cell Source)> ParseRowAuto(
        IReadOnlyList<Tabula.Cell> row,
        Func<string, string, int> getOrAddColor)
    {
        var result = new List<(int, Tabula.Cell)>();

        foreach (var cell in row)
        {
            if (cell.Height < 2 || cell.Width < 2)
                continue;

            var (kind, symbol, fontName) = ParseCellContent(cell);
            // Мусор (не клетка схемы, а текст/легенда): строку отбрасываем.
            if (kind == CellKind.Garbage)
                return new List<(int, Tabula.Cell)>();

            int colorIndex = kind == CellKind.Empty
                ? getOrAddColor("EMPTY", string.Empty)
                : getOrAddColor(symbol, fontName);

            result.Add((colorIndex, cell));
        }

        return result;
    }
    private static void ParseTable(
        Table table,
        int height,
        int width,
        int pageIndex,
        int xOffset,
        int pdfPageNum,
        ParsedScheme scheme,
        Func<string, string, int> getOrAddColor)
    {
        int parsedRows = 0;

        foreach (var row in table.Rows)
        {
            if (row.Count < OnePageWidth)
                break;
            if (parsedRows >= height)
                break;

            var cells = ParseRow(row, width, getOrAddColor);
            if (cells.Count == 0)
                continue;

            for (int x = 0; x < cells.Count; x++)
            {
                var (colorIndex, src) = cells[x];
                scheme.Cells.Add(new ParsedCell(pageIndex, xOffset + x, parsedRows, colorIndex)
                {
                    PdfPage = pdfPageNum,
                    PdfX = src.X,
                    PdfY = src.Y,
                    PdfW = src.Width,
                    PdfH = src.Height
                });
            }

            parsedRows++;
        }
    }

    private static List<(int ColorIndex, Tabula.Cell Source)> ParseRow(
        IReadOnlyList<Tabula.Cell> row,
        int width,
        Func<string, string, int> getOrAddColor)
    {
        var result = new List<(int, Tabula.Cell)>();
        int skipped = 0;

        foreach (var cell in row)
        {
            if (result.Count >= width)
                break;

            // Не набирается целая строка из-за скипнутых — считаем строку неподходящей.
            if (skipped + width > row.Count)
                return new List<(int, Tabula.Cell)>();

            if (cell.Height < 2 || cell.Width < 2)
            {
                skipped++;
                continue;
            }

            var (kind, symbol, fontName) = ParseCellContent(cell);
            if (kind == CellKind.Garbage)
                break;

            result.Add((kind == CellKind.Empty
                ? getOrAddColor("EMPTY", string.Empty)
                : getOrAddColor(symbol, fontName), cell));
        }

        return result;
    }
}

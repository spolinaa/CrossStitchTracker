namespace PatternTracker.Server.Parser;

/// <summary>Результат парсинга PDF-схемы в каноническую модель (без привязки к БД).</summary>
public sealed class ParsedScheme
{
    public List<ParsedColor> Colors { get; } = new();
    public List<ParsedCell> Cells { get; } = new();
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Листы PDF, распознанные как страницы сетки: ширина/высота каждого в клетках.</summary>
    public List<ParsedSheet> Sheets { get; } = new();

    /// <summary>Фон клеток из PDF. Заполняется сэмплером после парсинга.</summary>
    public ParsedSchemeBackgrounds Backgrounds { get; } = new();
}

/// <summary>Один распознанный лист сетки: индекс страницы сетки, PdfPage, размер в клетках.</summary>
public sealed record ParsedSheet(int Page, int PdfPage, int W, int H);

public sealed record ParsedColor(string Symbol, string Font);

public sealed record ParsedCell(int Page, int X, int Y, int ColorIndex)
{
    /// <summary>Номер страницы в PDF (1-based), нужен для сэмплинга фона.</summary>
    public int PdfPage { get; init; }

    /// <summary>Прямоугольник клетки в пунктах PDF.</summary>
    public double PdfX { get; init; }

    public double PdfY { get; init; }

    public double PdfW { get; init; }

    public double PdfH { get; init; }
}

/// <summary>Фон клетки в hex (#rrggbb). Ключ — (Page, X, Y).</summary>
public sealed class ParsedSchemeBackgrounds : Dictionary<(int Page, int X, int Y), string>
{
}

/// <summary>Прогресс парсинга: какая PDF-страница обрабатывается.</summary>
public sealed record PageProgress(int CurrentPage, int TotalPages);

namespace PatternTracker.Server.Models;

public sealed record PatternSummaryDto(
    int Id,
    string Name,
    int Width,
    int Height,
    int TotalCells,
    int FinishedCells,
    DateTime CreatedAt,
    string? SourceFileName,
    int PageCount,
    int? GridCols,
    int? GridRows);

public sealed record PatternDetailDto(
    int Id,
    string Name,
    int Width,
    int Height,
    List<ColorDto> Colors,
    List<List<List<CellDto>>> Pages);

public sealed record ColorDto(
    int ColorId,
    string Symbol,
    string Font,
    int TotalCells,
    int FinishedCells,
    string? Sample,
    string? FlossBrand,
    string? FlossCode,
    string? FlossHex);

public sealed record CellDto(int X, int Y, int ColorId, bool IsFinished, string? Bg);

/// <summary>Легкое превью для карточки: ужатая мозаика первой страницы.</summary>
public sealed record PatternPreviewDto(
    int Id,
    string Name,
    int Width,
    int Height,
    List<ColorDto> Colors,
    List<List<CellDto>> Mosaic);

/// <summary>Одна страница схемы для вьювера.</summary>
public sealed record PatternPageDto(
    int Id,
    string Name,
    int Width,
    int Height,
    int Page,
    int PageCount,
    List<ColorDto> Colors,
    List<List<CellDto>> Rows,
    string? FontsLink,
    Dictionary<string, string> Fonts,
    int? GridCols,
    int? GridRows,
    List<SheetDto> Sheets);

/// <summary>Один лист PDF на склеенном полотне: смещение и размер в клетках.</summary>
public sealed record SheetDto(int Page, int Col, int Row, int X, int Y, int W, int H);

/// <summary>Листы распознанной схемы для окна раскладки.</summary>
public sealed record PatternSheetsDto(
    int PatternId,
    int SheetCount,
    List<SheetDto> Sheets,
    int? GridCols,
    int? GridRows,
    int CanvasW,
    int CanvasH);

public sealed record SetLayoutRequest(int Cols, int Rows);

/// <summary>Пара из ключа палитры, сопоставленная с цветом схемы.</summary>
public sealed record FlossKeyMatchDto(
    int? ColorId,
    string Symbol,
    string Font,
    string Code,
    string? Hex);

/// <summary>Пары символ-код из ключа вместе со шрифтами ключа (awesome-шрифты символов).</summary>
public sealed record FlossKeyResultDto(
    List<FlossKeyMatchDto> Pairs,
    string? FontsLink,
    Dictionary<string, string> Fonts,
    List<string> Debug);

public sealed record FlossMappingDto(int ColorId, string Code);

public sealed record SaveFlossMapRequest(string Brand, List<FlossMappingDto> Mappings);

public sealed record SetFlossRequest(int ColorId, string Brand, string Code);

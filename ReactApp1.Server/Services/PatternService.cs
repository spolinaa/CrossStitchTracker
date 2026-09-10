using Microsoft.EntityFrameworkCore;
using PatternTracker.Server.Data;
using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Models;
using PatternTracker.Server.Parser;

namespace PatternTracker.Server.Services;

public class PatternService : IPatternService
{
    private readonly TrackerDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly FlossPaletteService _floss;

    public PatternService(TrackerDbContext db, IWebHostEnvironment env, FlossPaletteService floss)
    {
        _db = db;
        _env = env;
        _floss = floss;
    }

    public static string FontsDir(IWebHostEnvironment env, int patternId) =>
        Path.Combine(env.ContentRootPath, "wwwroot", "static_fonts", patternId.ToString());

    public async Task<List<PatternSummaryDto>> GetPatternsAsync(string psuid)
    {
        return await _db.Patterns
            .Where(p => p.Owner.YandexPsuid == psuid)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PatternSummaryDto(
                p.Id,
                p.Name,
                p.Width,
                p.Height,
                p.Cells.Count,
                p.Cells.Count(c => c.IsFinished),
                p.CreatedAt,
                p.SourceFileName,
                p.Cells.Select(c => c.Page).Distinct().Count(),
                p.GridCols,
                p.GridRows))
            .ToListAsync();
    }

    public async Task<PatternDetailDto?> GetPatternAsync(string psuid, int id)
    {
        var pattern = await _db.Patterns
            .Include(p => p.Colors)
            .Include(p => p.Cells)
            .Where(p => p.Id == id && p.Owner.YandexPsuid == psuid)
            .FirstOrDefaultAsync();

        return pattern is null ? null : ToDetail(pattern);
    }

    public async Task<PatternPreviewDto?> GetPreviewAsync(string psuid, int id)
    {
        var meta = await _db.Patterns.AsNoTracking()
            .Where(p => p.Id == id && p.Owner.YandexPsuid == psuid)
            .Select(p => new { p.Id, p.Name, p.Width, p.Height })
            .FirstOrDefaultAsync();
        if (meta is null)
            return null;

        var colors = await _db.PatternColors.AsNoTracking()
            .Where(c => c.PatternId == id)
            .OrderBy(c => c.ColorIndex)
            .ToListAsync();

        var counts = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .GroupBy(c => c.ColorIndex)
            .Select(g => new { Color = g.Key, Total = g.Count(), Done = g.Count(c => c.IsFinished) })
            .ToDictionaryAsync(x => x.Color);

        const int max = 48;
        var rows = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id && c.Page == 0)
            .GroupBy(c => c.Y)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(c => c.X).ToList())
            .ToListAsync();

        int maxCols = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        int stepY = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)max));
        int stepX = Math.Max(1, (int)Math.Ceiling(maxCols / (double)max));

        var mosaic = rows
            .Where((_, y) => y % stepY == 0)
            .Select(r => r
                .Where((_, x) => x % stepX == 0)
                .Select(c => new CellDto(c.X, c.Y, c.ColorIndex, c.IsFinished, c.BgColor))
                .ToList())
            .ToList();

        var samples = rows
            .SelectMany(r => r)
            .GroupBy(c => c.ColorIndex)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => c.BgColor).FirstOrDefault(b => b != null));

        var colorDtos = colors.Select(c =>
        {
            counts.TryGetValue(c.ColorIndex, out var agg);
            return new ColorDto(c.ColorIndex, c.Symbol, c.Font, agg?.Total ?? 0, agg?.Done ?? 0, samples.GetValueOrDefault(c.ColorIndex), c.FlossBrand, c.FlossCode, c.FlossHex);
        }).ToList();

        return new PatternPreviewDto(meta.Id, meta.Name, meta.Width, meta.Height, colorDtos, mosaic);
    }

    public async Task<PatternPageDto?> GetPatternPageAsync(string psuid, int id, int page)
    {
        var meta = await _db.Patterns.AsNoTracking()
            .Where(p => p.Id == id && p.Owner.YandexPsuid == psuid)
            .Select(p => new { p.Id, p.Name, p.Width, p.Height, p.GridCols, p.GridRows })
            .FirstOrDefaultAsync();
        if (meta is null)
            return null;

        var colors = await _db.PatternColors.AsNoTracking()
            .Where(c => c.PatternId == id)
            .OrderBy(c => c.ColorIndex)
            .ToListAsync();

        var counts = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .GroupBy(c => c.ColorIndex)
            .Select(g => new { Color = g.Key, Total = g.Count(), Done = g.Count(c => c.IsFinished) })
            .ToDictionaryAsync(x => x.Color);

        int pageCount = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .Select(c => c.Page)
            .Distinct()
            .CountAsync();
        if (pageCount == 0)
            return null;

        // Склеенное полотно: все листы рядом по раскладке GridCols x GridRows.
        var sheetRows = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .GroupBy(c => c.Page)
            .Select(g => new
            {
                Page = g.Key,
                MaxX = g.Max(c => c.X),
                MaxY = g.Max(c => c.Y)
            })
            .OrderBy(s => s.Page)
            .ToListAsync();
        var sheets = sheetRows.Select(s => new SheetSize(s.Page, s.MaxX + 1, s.MaxY + 1)).ToList();
        var layout = BuildLayout(sheets, meta.GridCols, meta.GridRows);
        var byGlobal = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .ToListAsync();
        var cellByPage = byGlobal
            .GroupBy(c => (c.Page, c.Y, c.X))
            .ToDictionary(g => g.Key, g => g.First());

        var stitched = new List<List<CellDto>>();
        for (int gy = 0; gy < layout.TotalH; gy++)
        {
            var row = new List<CellDto>();
            for (int gx = 0; gx < layout.TotalW; gx++)
            {
                PlacedSheet? hit = null;
                int hlx = 0, hly = 0;
                foreach (var pl in layout.Placement)
                {
                    int lx = gx - pl.X, ly = gy - pl.Y;
                    if (lx >= 0 && ly >= 0 && lx < pl.W && ly < pl.H)
                    {
                        hit = pl; hlx = lx; hly = ly;
                        break;
                    }
                }
                if (hit is null)
                {
                    row.Add(new CellDto(gx, gy, -1, true, null));
                    continue;
                }
                if (cellByPage.TryGetValue((hit.Page, hly, hlx), out var c))
                    row.Add(new CellDto(gx, gy, c.ColorIndex, c.IsFinished, c.BgColor));
                else
                    row.Add(new CellDto(gx, gy, -1, true, null));
            }
            stitched.Add(row);
        }

        var samples = byGlobal
            .GroupBy(c => c.ColorIndex)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => c.BgColor).FirstOrDefault(b => b != null));

        var colorDtos = colors.Select(c =>
        {
            counts.TryGetValue(c.ColorIndex, out var agg);
            return new ColorDto(c.ColorIndex, c.Symbol, c.Font, agg?.Total ?? 0, agg?.Done ?? 0, samples.GetValueOrDefault(c.ColorIndex), c.FlossBrand, c.FlossCode, c.FlossHex);
        }).ToList();

        var sheetDtos = layout.Placement
            .Select(pl => new SheetDto(pl.Page, pl.Col, pl.Row, pl.X, pl.Y, pl.W, pl.H))
            .ToList();

        return new PatternPageDto(meta.Id, meta.Name, layout.TotalW, layout.TotalH, 0, 1, colorDtos, stitched,
            GetFontsLink(id), GetFontsMap(id), layout.Cols, layout.Rows, sheetDtos);
    }

    /// <summary>Раскладка листов на полотне: по строкам слева направо.</summary>
    private sealed record SheetSize(int Page, int W, int H);

    private sealed record PlacedSheet(int Page, int Col, int Row, int X, int Y, int W, int H);

    private static (int Cols, int Rows, int TotalW, int TotalH, List<PlacedSheet> Placement)
        BuildLayout(List<SheetSize> sheets, int? gridCols, int? gridRows)
    {
        int n = sheets.Count;
        int cols = gridCols is > 0 ? gridCols.Value : n;
        cols = Math.Clamp(cols, 1, Math.Max(1, n));
        int rows = gridRows is > 0 ? gridRows.Value : (int)Math.Ceiling(n / (double)cols);
        rows = Math.Max(1, rows);

        // Ширина каждой колонки = максимум ширин листов в ней; аналогично ряды.
        var colW = new int[cols];
        var rowH = new int[rows];
        var cells = new List<PlacedSheet>();
        for (int i = 0; i < sheets.Count; i++)
        {
            int col = i % cols, row = i / cols;
            if (row >= rows) break;
            int w = sheets[i].W, h = sheets[i].H;
            cells.Add(new PlacedSheet(sheets[i].Page, col, row, 0, 0, w, h));
            colW[col] = Math.Max(colW[col], w);
            rowH[row] = Math.Max(rowH[row], h);
        }
        var colX = new int[cols];
        for (int c = 1; c < cols; c++) colX[c] = colX[c - 1] + colW[c - 1];
        var rowY = new int[rows];
        for (int r = 1; r < rows; r++) rowY[r] = rowY[r - 1] + rowH[r - 1];
        int totalW = cols == 0 ? 0 : colX[cols - 1] + colW[cols - 1];
        int totalH = rows == 0 ? 0 : rowY[rows - 1] + rowH[rows - 1];

        var placement = cells
            .Select(c => c with { X = colX[c.Col], Y = rowY[c.Row] })
            .ToList();
        return (cols, rows, totalW, totalH, placement);
    }

    private string? GetFontsLink(int patternId)
    {
        var dir = FontsDir(_env, patternId);
        return Directory.Exists(dir) ? $"/static_fonts/{patternId}/" : null;
    }

    private Dictionary<string, string> GetFontsMap(int patternId)
    {
        var dir = FontsDir(_env, patternId);
        return Directory.Exists(dir)
            ? FontExtractor.ReadMap(dir)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public async Task<PatternDetailDto?> UploadAsync(string psuid, string name, Stream pdf, int height, int width)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.YandexPsuid == psuid);
        if (user is null)
            return null;

        // Сохраняем PDF во временный файл (кроссплатформенно) — парсеру нужен путь.
        var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            await using (var fs = File.Create(tmpPath))
                await pdf.CopyToAsync(fs);

            var scheme = height > 0 && width > 0
                ? PdfSchemeParser.Parse(tmpPath, height, width)
                : PdfSchemeParser.ParseAuto(tmpPath);
            if (scheme.Cells.Count == 0)
                return null;

            return await SaveSchemeAsync(psuid, name, scheme);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    public async Task<PatternDetailDto?> SaveSchemeAsync(
        string psuid, string name, ParsedScheme scheme, string? sourceFileName = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.YandexPsuid == psuid);
        if (user is null)
            return null;

        var pattern = new Pattern
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Схема {DateTime.UtcNow:yyyy-MM-dd HH:mm}" : name,
            OwnerId = user.Id,
            Width = scheme.Width,
            Height = scheme.Height,
            SourceFileName = sourceFileName,
            Colors = scheme.Colors.Select((c, i) => new PatternColor
            {
                ColorIndex = i,
                Symbol = c.Symbol,
                Font = c.Font
            }).ToList(),
            Cells = scheme.Cells.Select(c => new PatternCell
            {
                Page = c.Page,
                X = c.X,
                Y = c.Y,
                ColorIndex = c.ColorIndex,
                IsFinished = false,
                BgColor = scheme.Backgrounds.TryGetValue((c.Page, c.X, c.Y), out var bg) ? bg : null
            }).ToList()
        };

        _db.Patterns.Add(pattern);
        await _db.SaveChangesAsync();

        return await GetPatternAsync(psuid, pattern.Id);
    }

    public async Task<bool> SetCellAsync(string psuid, int patternId, int page, int x, int y, bool isFinished)
    {
        // Клик приходит с глобальными координатами полотна: разворачиваем
        // в (лист, локальные x/y) по текущей раскладке GridCols/GridRows.
        var (ok, realPage, lx, ly) = await ResolveGlobalCellAsync(psuid, patternId, x, y);
        if (!ok)
            return false;
        if (page != 0 && page != realPage)
        {
            // фронт в режиме полотна всегда шлёт page=0 — иначе сверяем.
        }

        var cell = await _db.PatternCells
            .Where(c => c.PatternId == patternId
                && c.Page == realPage && c.X == lx && c.Y == ly
                && _db.Patterns.Any(p => p.Id == patternId && p.Owner.YandexPsuid == psuid))
            .FirstOrDefaultAsync();

        if (cell is null)
            return false;

        cell.IsFinished = isFinished;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Глобальные координаты полотна -> (лист, локальные x/y).</summary>
    private async Task<(bool Ok, int Page, int X, int Y)> ResolveGlobalCellAsync(
        string psuid, int patternId, int gx, int gy)
    {
        var owned = await _db.Patterns.AsNoTracking()
            .Where(p => p.Id == patternId && p.Owner.YandexPsuid == psuid)
            .Select(p => new { p.GridCols, p.GridRows })
            .FirstOrDefaultAsync();
        if (owned is null)
            return (false, 0, 0, 0);

        var sheetRows4 = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == patternId)
            .GroupBy(c => c.Page)
            .Select(g => new
            {
                Page = g.Key,
                MaxX = g.Max(c => c.X),
                MaxY = g.Max(c => c.Y)
            })
            .OrderBy(s => s.Page)
            .ToListAsync();
        var layout = BuildLayout(sheetRows4.Select(s => new SheetSize(s.Page, s.MaxX + 1, s.MaxY + 1)).ToList(), owned.GridCols, owned.GridRows);
        foreach (var pl in layout.Placement)
        {
            int lx = gx - pl.X, ly = gy - pl.Y;
            if (lx >= 0 && ly >= 0 && lx < pl.W && ly < pl.H)
                return (true, pl.Page, lx, ly);
        }
        return (false, 0, 0, 0);
    }

    public async Task<PatternSheetsDto?> GetSheetsAsync(string psuid, int patternId)
    {
        var pattern = await _db.Patterns.AsNoTracking()
            .Where(p => p.Id == patternId && p.Owner.YandexPsuid == psuid)
            .Select(p => new { p.Id, p.GridCols, p.GridRows })
            .FirstOrDefaultAsync();
        if (pattern is null)
            return null;

        var sheetRows2 = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == patternId)
            .GroupBy(c => c.Page)
            .Select(g => new
            {
                Page = g.Key,
                MaxX = g.Max(c => c.X),
                MaxY = g.Max(c => c.Y)
            })
            .OrderBy(s => s.Page)
            .ToListAsync();
        var layout = BuildLayout(sheetRows2.Select(s => new SheetSize(s.Page, s.MaxX + 1, s.MaxY + 1)).ToList(), pattern.GridCols, pattern.GridRows);
        return new PatternSheetsDto(
            patternId,
            sheetRows2.Count,
            layout.Placement.Select(pl => new SheetDto(pl.Page, pl.Col, pl.Row, pl.X, pl.Y, pl.W, pl.H)).ToList(),
            layout.Cols, layout.Rows, layout.TotalW, layout.TotalH);
    }

    public async Task<bool> SetLayoutAsync(string psuid, int patternId, int cols, int rows)
    {
        var pattern = await _db.Patterns
            .Where(p => p.Id == patternId && p.Owner.YandexPsuid == psuid)
            .FirstOrDefaultAsync();
        if (pattern is null)
            return false;

        int sheetCount = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == patternId)
            .Select(c => c.Page)
            .Distinct()
            .CountAsync();
        if (sheetCount == 0)
            return false;

        cols = Math.Clamp(cols, 1, Math.Max(1, sheetCount));
        rows = Math.Clamp(rows, 1, Math.Max(1, sheetCount));
        // Раскладка обязана вместить все листы.
        while (cols * rows < sheetCount)
            rows++;

        var sheetRows3 = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == patternId)
            .GroupBy(c => c.Page)
            .Select(g => new
            {
                Page = g.Key,
                MaxX = g.Max(c => c.X),
                MaxY = g.Max(c => c.Y)
            })
            .OrderBy(s => s.Page)
            .ToListAsync();
        var layout = BuildLayout(sheetRows3.Select(s => new SheetSize(s.Page, s.MaxX + 1, s.MaxY + 1)).ToList(), cols, rows);

        pattern.GridCols = cols;
        pattern.GridRows = layout.Rows;
        pattern.Width = layout.TotalW;
        pattern.Height = layout.TotalH;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> SetColorCellsAsync(string psuid, int patternId, int colorId, bool isFinished)
    {
        var owned = await _db.Patterns.AnyAsync(p => p.Id == patternId && p.Owner.YandexPsuid == psuid);
        if (!owned)
            return 0;

        var cells = await _db.PatternCells
            .Where(c => c.PatternId == patternId && c.ColorIndex == colorId)
            .ToListAsync();

        foreach (var cell in cells)
            cell.IsFinished = isFinished;

        await _db.SaveChangesAsync();
        return cells.Count;
    }

    public async Task<bool> DeleteAsync(string psuid, int patternId)
    {
        var pattern = await _db.Patterns
            .Where(p => p.Id == patternId && p.Owner.YandexPsuid == psuid)
            .FirstOrDefaultAsync();

        if (pattern is null)
            return false;

        _db.Patterns.Remove(pattern);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<FlossKeyMatchDto>> MatchFlossKeyAsync(
        string psuid, int patternId, string brand, List<FlossKeyPair> pairs)
    {
        var owned = await _db.Patterns.AnyAsync(p => p.Id == patternId && p.Owner.YandexPsuid == psuid);
        if (!owned)
            return new List<FlossKeyMatchDto>();

        var colors = await _db.PatternColors.AsNoTracking()
            .Where(c => c.PatternId == patternId)
            .ToListAsync();

        var bySymbolFont = new Dictionary<(string Symbol, string Font), PatternColor>();
        foreach (var c in colors)
        {
            // Один и тот же символ может встречаться в схеме в нескольких
            // awesome-шрифтах: храним все варианты, чтобы сравнивать со шрифтом ключа.
            bySymbolFont.TryAdd((c.Symbol, FontNames.Normalize(c.Font)), c);
        }
        var bySymbol = colors
            .GroupBy(c => c.Symbol)
            .ToDictionary(g => g.Key, g => g.ToList());

        return pairs.Select(p =>
        {
            var normFont = FontNames.Normalize(p.Font);
            int? colorId = null;
            if (bySymbolFont.TryGetValue((p.Symbol, normFont), out var exact))
            {
                // Точное совпадение символа И шрифта: дистрибутив ключа совпал со схемой.
                colorId = exact.ColorIndex;
            }
            else if (bySymbol.TryGetValue(p.Symbol, out var group))
            {
                // Тот же символ другим шрифтом: если в схеме он один — привязываем
                // смело; если в нескольких шрифтах — по шрифту уже не различить,
                // оставляем null, чтобы не привязать не тот цвет.
                if (group.Count == 1)
                    colorId = group[0].ColorIndex;
            }

            return new FlossKeyMatchDto(colorId, p.Symbol, p.Font, p.Code, _floss.GetHex(brand, p.Code));
        }).ToList();
    }

    public async Task<int> SaveFlossMapAsync(
        string psuid, int patternId, string brand, List<FlossMappingDto> mappings)
    {
        var colors = await _db.PatternColors
            .Where(c => c.PatternId == patternId && _db.Patterns.Any(p => p.Id == patternId && p.Owner.YandexPsuid == psuid))
            .ToListAsync();

        var byId = colors.ToDictionary(c => c.ColorIndex);
        // Дедуп: одна и та же пара ключ->цвет могла прийти дважды
        // (например, дублирующиеся строки в ключе) — последний код побеждает.
        var seen = new HashSet<int>();
        int saved = 0;
        foreach (var m in mappings.AsEnumerable().Reverse())
        {
            if (!seen.Add(m.ColorId))
                continue;
            if (!byId.TryGetValue(m.ColorId, out var color))
                continue;
            var code = m.Code?.Trim() ?? string.Empty;
            color.FlossBrand = brand;
            color.FlossCode = code;
            color.FlossHex = string.IsNullOrEmpty(code) ? null : _floss.GetHex(brand, code);
            saved++;
        }

        await _db.SaveChangesAsync();
        return saved;
    }

    public async Task<bool> SetFlossCodeAsync(
        string psuid, int patternId, int colorId, string brand, string code)
    {
        var color = await _db.PatternColors
            .Where(c => c.PatternId == patternId && c.ColorIndex == colorId
                && _db.Patterns.Any(p => p.Id == patternId && p.Owner.YandexPsuid == psuid))
            .FirstOrDefaultAsync();

        if (color is null)
            return false;

        code = code?.Trim() ?? string.Empty;
        color.FlossBrand = brand;
        color.FlossCode = code;
        color.FlossHex = string.IsNullOrEmpty(code) ? null : _floss.GetHex(brand, code);
        await _db.SaveChangesAsync();
        return true;
    }

    private static PatternDetailDto ToDetail(Pattern pattern)
    {
        var cellsByColor = pattern.Cells
            .GroupBy(c => c.ColorIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var colors = pattern.Colors
            .OrderBy(c => c.ColorIndex)
            .Select(c =>
            {
                cellsByColor.TryGetValue(c.ColorIndex, out var list);
                list ??= new List<PatternCell>();
                return new ColorDto(c.ColorIndex, c.Symbol, c.Font, list.Count, list.Count(x => x.IsFinished), list.Select(x => x.BgColor).FirstOrDefault(b => b != null), c.FlossBrand, c.FlossCode, c.FlossHex);
            })
            .ToList();

        var pages = pattern.Cells
            .GroupBy(c => c.Page)
            .OrderBy(g => g.Key)
            .Select(g => g.GroupBy(c => c.Y)
                .OrderBy(r => r.Key)
                .Select(r => r.OrderBy(c => c.X)
                    .Select(c => new CellDto(c.X, c.Y, c.ColorIndex, c.IsFinished, c.BgColor))
                    .ToList())
                .ToList())
            .ToList();

        return new PatternDetailDto(pattern.Id, pattern.Name, pattern.Width, pattern.Height, colors, pages);
    }
}

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
                p.SourceFileName))
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

        int pageCount = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id)
            .Select(c => c.Page)
            .Distinct()
            .CountAsync();
        if (pageCount == 0)
            return null;
        page = Math.Clamp(page, 0, pageCount - 1);

        var flat = await _db.PatternCells.AsNoTracking()
            .Where(c => c.PatternId == id && c.Page == page)
            .OrderBy(c => c.Y)
            .ThenBy(c => c.X)
            .ToListAsync();

        var rows = flat
            .GroupBy(c => c.Y)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(c => new CellDto(c.X, c.Y, c.ColorIndex, c.IsFinished, c.BgColor)).ToList())
            .ToList();

        var samples = flat
            .GroupBy(c => c.ColorIndex)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => c.BgColor).FirstOrDefault(b => b != null));

        var colorDtos = colors.Select(c =>
        {
            counts.TryGetValue(c.ColorIndex, out var agg);
            return new ColorDto(c.ColorIndex, c.Symbol, c.Font, agg?.Total ?? 0, agg?.Done ?? 0, samples.GetValueOrDefault(c.ColorIndex), c.FlossBrand, c.FlossCode, c.FlossHex);
        }).ToList();

        return new PatternPageDto(meta.Id, meta.Name, meta.Width, meta.Height, page, pageCount, colorDtos, rows,
            GetFontsLink(id), GetFontsMap(id));
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
        var cell = await _db.PatternCells
            .Where(c => c.PatternId == patternId
                && c.Page == page && c.X == x && c.Y == y
                && _db.Patterns.Any(p => p.Id == patternId && p.Owner.YandexPsuid == psuid))
            .FirstOrDefaultAsync();

        if (cell is null)
            return false;

        cell.IsFinished = isFinished;
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

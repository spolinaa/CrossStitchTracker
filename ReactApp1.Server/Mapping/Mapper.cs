using PatternTracker.Server.Models;

namespace PatternTracker.Server.Mapping;

public static class Mapper
{
    public static PatternDetailDto ToDetailDto(Pattern pattern)
    {
        var colors = pattern.Colors
            .OrderBy(c => c.ColorIndex)
            .Select(c =>
            {
                var cells = pattern.Cells.Where(x => x.ColorIndex == c.ColorIndex).ToList();
                return new ColorDto(c.ColorIndex, c.Symbol, c.Font, cells.Count, cells.Count(x => x.IsFinished), cells.Select(x => x.BgColor).FirstOrDefault(b => b != null), c.FlossBrand, c.FlossCode, c.FlossHex);
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

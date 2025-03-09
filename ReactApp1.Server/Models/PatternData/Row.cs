namespace PatternTracker.Server.Models.PatternData;

public record Row
{
    public List<Cell> Columns { get; init; }
}

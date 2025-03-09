using System.Drawing;

namespace PatternTracker.Server.Models.PatternData;

public record Table
{
    public List<Row> Rows { get; init; }
}

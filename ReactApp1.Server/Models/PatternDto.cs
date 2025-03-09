using System.Drawing;
using PatternTracker.Server.Models.PatternData;

namespace PatternTracker.Server.Models;

public record PatternDto
{
    public List<Color> Colors { get; init; }

    public string FontsPath { get; init; }  

    public List<Table> Data { get; init; }
}

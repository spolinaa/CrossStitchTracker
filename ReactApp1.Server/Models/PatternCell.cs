namespace PatternTracker.Server.Models;

public class PatternCell
{
    public int Id { get; set; }

    public int PatternId { get; set; }

    public int Page { get; set; }

    /// <summary>Колонка (X).</summary>
    public int X { get; set; }

    /// <summary>Строка (Y).</summary>
    public int Y { get; set; }

    public int ColorIndex { get; set; }

    public bool IsFinished { get; set; }

    /// <summary>Фон клетки из PDF (#rrggbb). Null — авто-подложка на фронте.</summary>
    public string? BgColor { get; set; }
}

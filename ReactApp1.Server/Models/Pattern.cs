namespace PatternTracker.Server.Models;

public class Pattern
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int OwnerId { get; set; }

    public User Owner { get; set; } = null!;

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Имя загруженного PDF — чтобы не путать похожие верстки.</summary>
    public string? SourceFileName { get; set; }

    /// <summary>Раскладка листов PDF в сетке полотна: столбцов. Null — авто (по одной странице).</summary>
    public int? GridCols { get; set; }

    /// <summary>Раскладка листов PDF в сетке полотна: рядов. Null — авто.</summary>
    public int? GridRows { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PatternColor> Colors { get; set; } = new();

    public List<PatternCell> Cells { get; set; } = new();
}

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PatternColor> Colors { get; set; } = new();

    public List<PatternCell> Cells { get; set; } = new();
}

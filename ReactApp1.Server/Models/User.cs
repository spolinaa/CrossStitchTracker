namespace PatternTracker.Server.Models;

public class User
{
    public int Id { get; set; }

    public string YandexPsuid { get; set; } = string.Empty;

    public string? Login { get; set; }

    public string? RealName { get; set; }

    public string? YandexId { get; set; }

    public List<Pattern> Patterns { get; set; } = new();
}

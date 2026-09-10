namespace PatternTracker.Server.Models;

public class PatternColor
{
    public int Id { get; set; }

    public int PatternId { get; set; }

    /// <summary>Индекс цвета в палитре схемы (то, что фронт использует как colorId).</summary>
    public int ColorIndex { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Font { get; set; } = string.Empty;

    /// <summary>Бренд мулине (DMC, Anchor...), заданный через ключ палитры.</summary>
    public string? FlossBrand { get; set; }

    /// <summary>Номер цвета в палитре бренда.</summary>
    public string? FlossCode { get; set; }

    /// <summary>Hex из палитры бренда. Null — закрашивать нечем.</summary>
    public string? FlossHex { get; set; }
}

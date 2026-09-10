using System.Text.Json;

namespace PatternTracker.Server.Services;

/// <summary>
/// Библиотека палитр мулине: Data/FlossPalettes/{brand}.json вида {"310": "#hex"}.
/// Подсказывает hex по бренду и номеру для закрашивания вышитых клеток.
/// </summary>
public class FlossPaletteService
{
    private readonly Dictionary<string, Dictionary<string, string>> _palettes =
        new(StringComparer.OrdinalIgnoreCase);

    public FlossPaletteService(IWebHostEnvironment env, ILogger<FlossPaletteService> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, "Data", "FlossPalettes");
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("Нет папки палитр: {Dir}", dir);
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var brand = Path.GetFileNameWithoutExtension(file);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (map is null)
                    continue;

                // Коды ищем без учета регистра/пробелов ("blanc" == "Blanc").
                _palettes[brand] = new Dictionary<string, string>(
                    map.Select(kv => KeyValuePair.Create(Norm(kv.Key), kv.Value)),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не прочиталась палитра {File}", file);
            }
        }

        logger.LogInformation("Палитры: {Brands}", string.Join(", ", Brands.Select(b => $"{b}({_palettes[b].Count})")));
    }

    public IReadOnlyList<string> Brands =>
        _palettes.Keys.OrderBy(b => b).ToList();

    public string? GetHex(string brand, string code)
    {
        if (!_palettes.TryGetValue(brand, out var map))
            return null;

        if (map.TryGetValue(Norm(code), out var hex))
            return hex;

        // Смеси "152-403": берем hex первого компонента.
        var dash = code.IndexOf('-');
        if (dash > 0)
            return map.TryGetValue(Norm(code[..dash]), out var firstHex) ? firstHex : null;

        return null;
    }

    private static string Norm(string code) => code.Trim().ToUpperInvariant();
}

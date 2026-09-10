using System.Text.RegularExpressions;

namespace PatternTracker.Server.Parser;

/// <summary>
/// Нормализация имён шрифтов: в PDF встроены сабсеты с тегом
/// ("AAAAAE+FontAwesome"), а извлечённый TTF / другой парсер могут видеть
/// то же семейство без тега или с другим тегом. Для сопоставления
/// сравниваем базовое имя после '+'.
/// </summary>
public static partial class FontNames
{
    [GeneratedRegex(@"^[A-Z0-9]{6}\+(.+)$")]
    private static partial Regex SubsetTagRegex();

    public static string Normalize(string? font)
    {
        if (string.IsNullOrEmpty(font))
            return string.Empty;
        var m = SubsetTagRegex().Match(font);
        return m.Success ? m.Groups[1].Value : font;
    }
}

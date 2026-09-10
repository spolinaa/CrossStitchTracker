using System.Text.Json;
using Tabula;
using Tabula.Extractors;
using UglyToad.PdfPig;

namespace PatternTracker.Server.Parser;

/// <summary>Пара символ-&gt;код из ключа палитры.</summary>
public sealed record FlossKeyPair(string Symbol, string Font, string Code);

/// <summary>
/// Разбирает ключ палитры «Символ | Код | Крестиков | ...» (x-floss/п.
/// Трактуем каждую ТАБЛИЧНУЮ строку: символ — одиночная литера 1 симв,
/// код — значение в соседней колонке правее по сетке таблицы.
/// Смеси вида "152-403" — валидный код (два через дефис).
/// "(пусто)" и строки без символа пропускаем.
/// </summary>
public static class FlossKeyParser
{
    public static List<FlossKeyPair> Parse(string filePath, int[]? pages = null)
    {
        var pairs = new List<FlossKeyPair>();
        var only = pages is { Length: > 0 } ? new HashSet<int>(pages) : null;

        using var document = PdfDocument.Open(filePath, new ParsingOptions { ClipPaths = true });
        var extractor = new ObjectExtractor(document);
        IExtractionAlgorithm algorithm = new SpreadsheetExtractionAlgorithm();

        for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            if (only is not null && !only.Contains(pageNum))
                continue;

            var page = extractor.Extract(pageNum);
            List<Table> tables;
            try
            {
                tables = algorithm.Extract(page);
            }
            catch
            {
                continue;
            }
            if (tables.Count == 0)
                continue;

            // Самая широкая таблица страницы = сетка ключа.
            var table = tables.OrderByDescending(t => t.ColumnCount).First();
            foreach (var row in table.Rows)
            {
                var pair = ParseRow(row);
                if (pair is not null)
                    pairs.Add(pair);
            }
        }

        return pairs
            .GroupBy(p => (p.Symbol, p.Font))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>DTO для JSON-ответа parse_floss_key.py (имена — как в JSON).</summary>
public sealed class FlossKeyRaw
{
    public string symbol { get; set; } = string.Empty;
    public string font { get; set; } = string.Empty;
    public string code { get; set; } = string.Empty;
    public string? raw { get; set; }
}

/// <summary>Результат разбора ключа: пары + диагностические строки скрипта.</summary>
public sealed record FlossKeyParseResult(List<FlossKeyPair> Pairs, List<string> Debug);

/// <summary>
/// Разбирает ключ через Tools/parse_floss_key.py (PyMuPDF): текст у таких
/// листов читается надёжнее. Без python3 — возвращает пустой список.
/// </summary>
public static FlossKeyParseResult ParseWithPython(string filePath, int[]? pages, string outPath)
{
    var script = FindScript();
    if (script is null)
        return new FlossKeyParseResult(new List<FlossKeyPair>(), new List<string>());
    if (!File.Exists(script))
        return new FlossKeyParseResult(new List<FlossKeyPair>(), new List<string>());

    var parts = new List<string> {
        "--pdf", $"\"{filePath}\"", "--out", $"\"{outPath}\""
    };
    if (pages is { Length: > 0 })
    {
        parts.Add("--pages");
        parts.Add(string.Join(",", pages));
    }

    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{script}\" {string.Join(" ", parts)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null || !proc.WaitForExit(TimeSpan.FromSeconds(120)) || proc.ExitCode != 0)
            return new FlossKeyParseResult(new List<FlossKeyPair>(), new List<string>());
        if (!System.IO.File.Exists(outPath))
            return new FlossKeyParseResult(new List<FlossKeyPair>(), new List<string>());

        var text = System.IO.File.ReadAllText(outPath);
        var doc = JsonDocument.Parse(text);
        List<FlossKeyRaw> raw;
        var debug = new List<string>();
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("pairs", out var pairsEl))
        {
            raw = JsonSerializer.Deserialize<List<FlossKeyRaw>>(pairsEl.GetRawText())
                ?? new List<FlossKeyRaw>();
            if (doc.RootElement.TryGetProperty("debug", out var debugEl)
                && debugEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in debugEl.EnumerateArray())
                    debug.Add(d.GetString() ?? string.Empty);
            }
        }
        else
        {
            raw = JsonSerializer.Deserialize<List<FlossKeyRaw>>(text)
                ?? new List<FlossKeyRaw>();
        }
        if (raw is null)
            return new FlossKeyParseResult(new List<FlossKeyPair>(), debug);

        var pairs = raw
            .Where(p => (p.symbol != string.Empty || !string.IsNullOrEmpty(p.raw)) && p.code != string.Empty)
            .Select(p => new FlossKeyPair(
                p.symbol != string.Empty ? p.symbol : (p.raw ?? string.Empty),
                p.font,
                p.code))
            .ToList();
        return new FlossKeyParseResult(pairs, debug);
    }
    catch
    {
        return new FlossKeyParseResult(new List<FlossKeyPair>(), new List<string>());
    }
}

    private static string? FindScript()
    {
        var cwd = Directory.GetCurrentDirectory();
        foreach (var candidate in new[]
                 {
                     Path.Combine(cwd, "Tools", "parse_floss_key.py"),
                     Path.Combine(cwd, "ReactApp1.Server", "Tools", "parse_floss_key.py"),
                     Path.Combine(Path.GetTempPath(), "..", "..", "projects", "CrossStitchTracker", "ReactApp1.Server", "Tools", "parse_floss_key.py")
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // последняя попытка — рядом с бинарем
        try
        {
            var bin = Path.GetDirectoryName(System.Environment.ProcessPath);
            if (bin is not null)
            {
                var home = Path.Combine(bin, "..", "..", "..", "..", "..");
                var rel = Path.Combine(home, "projects", "CrossStitchTracker", "ReactApp1.Server", "Tools", "parse_floss_key.py");
                if (File.Exists(rel))
                    return rel;
            }
        }
        catch
        {
        }

        return null;
    }

    private static FlossKeyPair? ParseRow(IReadOnlyList<Tabula.Cell> row)
    {
        // tabula: ячейки разбиты по горизонтальным линиям/тексту.
        // В строке: символ (одиночный), код (токен с цифрой), потом колонки типа "Цвет".
        var cells = row.Where(c => c.Width >= 2 && c.Height >= 2).ToList();

        // нормализуем: найди первый одиночный символ; к нему следующий ячейковый код
        string? symbol = null;
        string? font = null;
        bool seenSymbol = false;
        string? code = null;

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            string text = "";
            string f = "";
            foreach (var te in c.TextElements)
            {
                foreach (var letter in te.TextElements)
                {
                    text += letter.GetText();
                    f = letter.Font.Name;
                }
            }
            var t = text.Trim();

            if (!seenSymbol)
            {
                // символ: однобуквенный токен (и не "(пусто)" и не код)
                if (t.Length == 1 && !t.Any(char.IsDigit))
                {
                    seenSymbol = true;
                    symbol = t;
                    font = f;
                }
                continue;
            }

            if (!IsCode(t))
                continue;
            code = t;
            break;
        }

        if (symbol is null) return null;
        if (code is null) return null;

        return new FlossKeyPair(symbol, font, code);
    }

    private static bool IsCode(string v)
    {
        if (v.Length is 0 or > 12) return false;
        if (v.Any(char.IsWhiteSpace)) return false;
        if (!v.Any(char.IsDigit)) return false; // код всегда с цифрой
        return v.All(c => char.IsLetterOrDigit(c) || c is '-' or '/' or '.');
    }
}
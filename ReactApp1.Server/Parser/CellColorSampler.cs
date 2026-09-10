using System.Diagnostics;
using System.Text.Json;

namespace PatternTracker.Server.Parser;

/// <summary>
/// Замеряет фон клеток вызовом Tools/sample_colors.py (векторные заливки PDF).
/// Без python3/pymupdf — тихо пропускаем, фронт упадет на авто-подложки.
/// </summary>
public static class CellColorSampler
{
    public static void SampleBackgrounds(
        string pdfPath, ParsedScheme scheme, string scriptPath, ILogger? logger = null)
    {
        var cells = scheme.Cells
            .Where(c => c.PdfW > 0 && c.PdfH > 0)
            .Select(c => new
            {
                key = $"{c.Page}:{c.X}:{c.Y}",
                pdfPage = c.PdfPage,
                x = c.PdfX,
                y = c.PdfY,
                w = c.PdfW,
                h = c.PdfH
            })
            .ToList();

        if (cells.Count == 0 || !File.Exists(scriptPath))
            return;

        var tmpDir = Directory.CreateTempSubdirectory("cellbg");
        try
        {
            var cellsPath = Path.Combine(tmpDir.FullName, "cells.json");
            var outPath = Path.Combine(tmpDir.FullName, "bg.json");
            File.WriteAllText(cellsPath, JsonSerializer.Serialize(cells));

            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" --pdf \"{pdfPath}\" --cells \"{cellsPath}\" --out \"{outPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return;
            if (!proc.WaitForExit(TimeSpan.FromMinutes(10)) || proc.ExitCode != 0)
            {
                logger?.LogWarning("Сэмплинг фона не удался: {Err}", proc.StandardError.ReadToEnd());
                return;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(outPath));
            if (map is null)
                return;

            foreach (var cell in scheme.Cells)
            {
                if (map.TryGetValue($"{cell.Page}:{cell.X}:{cell.Y}", out var bg) && !string.IsNullOrEmpty(bg))
                    scheme.Backgrounds[(cell.Page, cell.X, cell.Y)] = bg;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Сэмплинг фона пропущен");
        }
        finally
        {
            try { tmpDir.Delete(recursive: true); } catch { }
        }
    }
}

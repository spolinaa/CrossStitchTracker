using System.Diagnostics;
using System.Text.Json;

namespace PatternTracker.Server.Parser;

/// <summary>
/// Извлекает встроенные шрифты из PDF через mutool (кроссплатформенно)
/// и раскладывает их по файлам, названным как шрифт в PDF.
/// Возвращает маппинг имя_шрифта -> имя_файла (пишется и в fonts.json рядом).
/// </summary>
public static class FontExtractor
{
    public const string MapFileName = "fonts.json";

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".woff", ".woff2", ".ttc"];

    public static string? ResolveMutool()
    {
        foreach (var candidate in new[] { "mutool", "/opt/homebrew/bin/mutool", "/usr/local/bin/mutool" })
        {
            if (!candidate.Contains('/'))
                return candidate; // ищем в PATH при запуске
            if (File.Exists(candidate))
                return candidate;
        }
        return "mutool";
    }

    public static Dictionary<string, string> ExtractFonts(
        string pdfPath, string destDir, string? repairScriptPath = null,
        string? rebuildCmapScriptPath = null, ILogger? logger = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Directory.CreateDirectory(destDir);

        var tmpDir = Directory.CreateTempSubdirectory("fonts");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveMutool(),
                Arguments = $"extract \"{pdfPath}\"",
                WorkingDirectory = tmpDir.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            string stdout;
            using (var proc = Process.Start(psi))
            {
                if (proc is null)
                {
                    logger?.LogWarning("Не запустился mutool — шрифты пропущены");
                    return result;
                }
                stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(TimeSpan.FromMinutes(2)))
                {
                    try { proc.Kill(); } catch { }
                    logger?.LogWarning("mutool завис — шрифты пропущены");
                    return result;
                }
                if (proc.ExitCode != 0)
                {
                    logger?.LogWarning("mutool завершился с кодом {Code}", proc.ExitCode);
                    return result;
                }
            }

            // mutool пишет "extracting <файл>" — но надежнее просто перечислить шрифты в папке.
            _ = stdout;
            foreach (var file in Directory.GetFiles(tmpDir.FullName))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!FontExtensions.Contains(ext))
                    continue;

                var psName = TtfNameReader.TryGetPostScriptName(file)
                    ?? Path.GetFileNameWithoutExtension(file);
                var destName = SanitizeFileName(psName) + ext;
                File.Copy(file, Path.Combine(destDir, destName), overwrite: true);
                // Храним и полное имя сабсета ("AAAAAE+FontAwesome"),
                // и нормализованное ("FontAwesome"): другой парсер (pymupdf/pdfpig)
                // может видеть тот же шрифт без тега или с другим тегом.
                result[psName] = destName;
                var norm = FontNames.Normalize(psName);
                if (!string.Equals(norm, psName, StringComparison.Ordinal))
                    result.TryAdd(norm, destName);
            }

            File.WriteAllText(
                Path.Combine(destDir, MapFileName),
                JsonSerializer.Serialize(result));

            RepairFonts(destDir, repairScriptPath, logger);
            RebuildCmaps(pdfPath, destDir, result, rebuildCmapScriptPath, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось извлечь шрифты — символы будут без оригинальных глифов");
        }
        finally
        {
            try { tmpDir.Delete(recursive: true); } catch { }
        }

        return result;
    }

    /// <summary>
    /// mutool достает обрезанные сабсеты без OS/2 и Unicode-cmap —
    /// браузеры их отвергают. Дотягиваем скриптом Tools/repair_font.py.
    /// Без python3/fontTools шрифты остаются как есть (будут подложки).
    /// </summary>
    private static void RepairFonts(string destDir, string? repairScriptPath, ILogger? logger)
    {
        if (repairScriptPath is null || !File.Exists(repairScriptPath))
            return;

        foreach (var file in Directory.GetFiles(destDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not ".ttf" and not ".otf")
                continue;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{repairScriptPath}\" \"{file}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null)
                    return;
                if (!proc.WaitForExit(TimeSpan.FromMinutes(1)) || proc.ExitCode != 0)
                {
                    logger?.LogWarning(
                        "Починка шрифта {File} не удалась: {Err}",
                        Path.GetFileName(file), proc.StandardError.ReadToEnd());
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Нет python3/fontTools — шрифт {File} без починки", Path.GetFileName(file));
                return;
            }
        }
    }

    /// <summary>
    /// Перестраивает Unicode-cmap по ToUnicode из PDF: байты в символических
    /// шрифтах идут не по MacRoman, один глиф может лежать на чужом коде.
    /// Без pikepdf — пропускаем (остается MacRoman-синтез из repair).
    /// </summary>
    private static void RebuildCmaps(
        string pdfPath, string destDir,
        Dictionary<string, string> map, string? scriptPath, ILogger? logger)
    {
        if (scriptPath is null || !File.Exists(scriptPath))
            return;

        foreach (var (psName, fileName) in map)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" \"{Path.Combine(destDir, fileName)}\" "
                        + $"--pdf \"{pdfPath}\" --font \"{psName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null)
                    return;
                if (!proc.WaitForExit(TimeSpan.FromMinutes(1)) || proc.ExitCode != 0)
                {
                    logger?.LogWarning(
                        "Перестройка cmap {File} не удалась: {Err}",
                        fileName, proc.StandardError.ReadToEnd());
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Нет python3/pikepdf — cmap {File} без перестройки", fileName);
                return;
            }
        }
    }

    public static Dictionary<string, string> ReadMap(string fontsDir)
    {
        try
        {
            var path = Path.Combine(fontsDir, MapFileName);
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '+' or '-' or '_' ? ch : '_');
        return sb.Length == 0 ? "font" : sb.ToString();
    }
}

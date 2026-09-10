using Microsoft.AspNetCore.Mvc;
using PatternTracker.Server.Helpers;
using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Models;
using PatternTracker.Server.Parser;
using PatternTracker.Server.Services;

namespace PatternTracker.Server.Controllers;

[ApiController]
public class PatternController : ControllerBase
{
    private readonly IPatternService _patternService;
    private readonly ParseJobService _jobs;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PatternController> _logger;

    public PatternController(IPatternService patternService, ParseJobService jobs, IWebHostEnvironment env, ILogger<PatternController> logger)
    {
        _patternService = patternService;
        _jobs = jobs;
        _env = env;
        _logger = logger;
    }

    [HttpGet("/patterns")]
    public async Task<ActionResult<IReadOnlyCollection<PatternSummaryDto>>> GetPatterns(
        [FromHeader(Name = "Cookie")] string? cookie)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        return Ok(await _patternService.GetPatternsAsync(psuid));
    }

    /// <summary>
    /// Загрузка PDF-схемы: multipart/form-data с полем file.
    /// Размер определяется автоматически по сетке страниц.
    /// Сразу возвращает jobId, распознавание идет в фоне — статус в GET /patterns/upload/status.
    /// </summary>
    [HttpPost("/patterns/upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Upload(
        [FromHeader(Name = "Cookie")] string? cookie,
        IFormFile file,
        [FromForm] string? name = null)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("Нужен PDF-файл в поле file.");
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && file.ContentType != "application/pdf")
            return BadRequest("Ожидается PDF-файл.");

        var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        await using (var fs = System.IO.File.Create(tmpPath))
            await file.CopyToAsync(fs);

        var jobId = _jobs.Start(psuid, name ?? Path.GetFileNameWithoutExtension(file.FileName), tmpPath, file.FileName);
        return Accepted(new { jobId });
    }

    [HttpGet("/patterns/upload/status")]
    public IActionResult UploadStatus(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] Guid jobId)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var job = _jobs.Get(psuid, jobId);
        if (job is null)
            return NotFound();

        return Ok(new
        {
            status = job.Status.ToString().ToLowerInvariant(),
            currentPage = job.CurrentPage,
            totalPages = job.TotalPages,
            patternId = job.PatternId,
            error = job.Error
        });
    }

    [HttpGet("/pattern")]
    public async Task<IActionResult> GetPattern(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var pattern = await _patternService.GetPatternAsync(psuid, patternId);
        return pattern is null ? NotFound() : Ok(pattern);
    }

    [HttpGet("/pattern/preview")]
    public async Task<IActionResult> GetPreview(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var preview = await _patternService.GetPreviewAsync(psuid, patternId);
        return preview is null ? NotFound() : Ok(preview);
    }

    [HttpGet("/pattern/page")]
    public async Task<IActionResult> GetPage(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId,
        [FromQuery] int page = 0)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var result = await _patternService.GetPatternPageAsync(psuid, patternId, page);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Листы схемы и текущая раскладка — для окна «столбцы x ряды» после загрузки.</summary>
    [HttpGet("/pattern/sheets")]
    public async Task<IActionResult> GetSheets(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var result = await _patternService.GetSheetsAsync(psuid, patternId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Задать раскладку листов (столбцы x ряды), полотно пересоберётся.</summary>
    [HttpPost("/pattern/layout")]
    public async Task<IActionResult> SetLayout(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId,
        [FromBody] SetLayoutRequest request)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var ok = await _patternService.SetLayoutAsync(psuid, patternId, request.Cols, request.Rows);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Отметить одну клетку (закрашивание).</summary>
    [HttpPatch("/pattern/cell")]
    public async Task<IActionResult> SetCell(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromBody] SetCellRequest request)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var ok = await _patternService.SetCellAsync(
            psuid, request.PatternId, request.Page, request.X, request.Y, request.IsFinished);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Массово отметить/снять все клетки одного цвета (подсветка по цвету + отметка).</summary>
    [HttpPatch("/pattern/color")]
    public async Task<IActionResult> SetColor(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromBody] SetColorRequest request)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var updated = await _patternService.SetColorCellsAsync(
            psuid, request.PatternId, request.ColorId, request.IsFinished);
        return updated == 0 ? NotFound() : Ok(new { updated });
    }

    [HttpDelete("/pattern")]
    public async Task<IActionResult> DeletePattern(
        [FromHeader(Name = "Cookie")] string? cookie,
        [FromQuery] int patternId)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var ok = await _patternService.DeleteAsync(psuid, patternId);
        return ok ? Ok() : NotFound();
    }

    [HttpGet("/floss/brands")]
    public ActionResult<IReadOnlyCollection<string>> GetFlossBrands(
        [FromServices] FlossPaletteService floss)
    {
        return Ok(floss.Brands);
    }

    /// <summary>
    /// Превью страниц PDF-ключа: миниатюры, чтобы отметить страницы с палитрой.
    /// Ничего не сохраняет.
    /// </summary>
    [HttpPost("/pattern/{patternId:int}/floss-key-preview")]
    [RequestSizeLimit(100_000_000)]
    public IActionResult PreviewFlossKey(
        [FromHeader(Name = "Cookie")] string? cookie,
        int patternId,
        IFormFile file)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("Нужен PDF-файл в поле file.");

        var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        var tmpDir = Directory.CreateTempSubdirectory("keyprev");
        try
        {
            using (var fs = System.IO.File.Create(tmpPath))
                file.CopyTo(fs);

            int totalPages;
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(tmpPath))
                totalPages = doc.NumberOfPages;

            const int max = 20;
            var pages = new List<object>();
            foreach (var p in Enumerable.Range(1, Math.Min(totalPages, max)))
            {
                var png = Path.Combine(tmpDir.FullName, $"p{p}.png");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FontExtractor.ResolveMutool(),
                    Arguments = $"draw -r 40 -o \"{png}\" \"{tmpPath}\" {p}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null || !proc.WaitForExit(TimeSpan.FromSeconds(30)))
                    continue;
                if (proc.ExitCode != 0 || !System.IO.File.Exists(png))
                    continue;

                pages.Add(new
                {
                    page = p,
                    image = "data:image/png;base64," + Convert.ToBase64String(System.IO.File.ReadAllBytes(png))
                });
            }

            return Ok(new { pages, totalPages });
        }
        finally
        {
            if (System.IO.File.Exists(tmpPath))
                System.IO.File.Delete(tmpPath);
            try { tmpDir.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Разобрать PDF с ключом палитры: возвращает пары символ-код,
    /// сопоставленные с цветами схемы (colorId null — руками в UI).
    /// Вместе с парами отдаем fontsLink/fonts для шрифтов самого ключа
    /// (символы-awesome рендерим именно их глифами, а не текстом).
    /// pages — номера страниц через запятую ("1,3"), пусто = все.
    /// Ничего не сохраняет.
    /// </summary>
    [HttpPost("/pattern/{patternId:int}/floss-key")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> ParseFlossKey(
        [FromHeader(Name = "Cookie")] string? cookie,
        int patternId,
        IFormFile file,
        [FromForm] string brand,
        [FromForm] string? pages = null)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("Нужен PDF-файл в поле file.");
        if (string.IsNullOrWhiteSpace(brand))
            return BadRequest("Нужен бренд ниток.");

        int[]? only = null;
        if (!string.IsNullOrWhiteSpace(pages))
        {
            only = pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? n : -1)
                .Where(n => n > 0)
                .Distinct()
                .ToArray();
        }

        var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        var tmpOut = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await using (var fs = System.IO.File.Create(tmpPath))
                await file.CopyToAsync(fs);

            var parsed = FlossKeyParser.ParseWithPython(tmpPath, only, tmpOut);
            var pairs = parsed.Pairs;
            if (pairs.Count == 0)
                return UnprocessableEntity(
                    "В ключе не нашлось пар символ-код." +
                    (parsed.Debug.Count > 0 ? " " + string.Join(" ", parsed.Debug) : ""));

            var matched = await _patternService.MatchFlossKeyAsync(psuid, patternId, brand, pairs);

            // Шрифты именно ключа: символ-awesome в таблице крутим их глифами.
            string? fontsLink = null;
            var fonts = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var keyFontsDir = PatternService.FontsDir(_env, patternId) + "_key";
                if (Directory.Exists(keyFontsDir))
                    Directory.Delete(keyFontsDir, recursive: true);
                var toolsDir = Path.Combine(_env.ContentRootPath, "Tools");
                fonts = FontExtractor.ExtractFonts(
                    tmpPath,
                    keyFontsDir,
                    Path.Combine(toolsDir, "repair_font.py"),
                    Path.Combine(toolsDir, "rebuild_cmap.py"),
                    _logger);
                if (fonts.Count > 0)
                    fontsLink = $"/static_fonts/{patternId}_key/";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Шрифты ключа для {PatternId} пропущены", patternId);
            }

            return Ok(new FlossKeyResultDto(matched, fontsLink, fonts, parsed.Debug));
        }
        finally
        {
            if (System.IO.File.Exists(tmpPath))
                System.IO.File.Delete(tmpPath);
            if (System.IO.File.Exists(tmpOut))
                System.IO.File.Delete(tmpOut);
        }
    }

    [HttpPost("/pattern/{patternId:int}/floss-map")]
    public async Task<IActionResult> SaveFlossMap(
        [FromHeader(Name = "Cookie")] string? cookie,
        int patternId,
        [FromBody] SaveFlossMapRequest request)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var saved = await _patternService.SaveFlossMapAsync(psuid, patternId, request.Brand, request.Mappings);
        return Ok(new { saved });
    }

    [HttpPatch("/pattern/{patternId:int}/floss")]
    public async Task<IActionResult> SetFlossCode(
        [FromHeader(Name = "Cookie")] string? cookie,
        int patternId,
        [FromBody] SetFlossRequest request)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        var ok = await _patternService.SetFlossCodeAsync(
            psuid, patternId, request.ColorId, request.Brand, request.Code);
        return ok ? Ok() : NotFound();
    }
}

public sealed record SetCellRequest(int PatternId, int Page, int X, int Y, bool IsFinished);

public sealed record SetColorRequest(int PatternId, int ColorId, bool IsFinished);

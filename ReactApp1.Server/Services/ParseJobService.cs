using System.Collections.Concurrent;
using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Parser;

namespace PatternTracker.Server.Services;

public enum ParseJobStatus
{
    Running,
    Done,
    Failed
}

public sealed class ParseJob
{
    public Guid Id { get; init; }
    public string OwnerPsuid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? SourceFileName { get; init; }
    public ParseJobStatus Status { get; set; } = ParseJobStatus.Running;
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int? PatternId { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Фоновое распознавание PDF: upload сразу возвращает jobId,
/// фронт опрашивает статус и рисует прогресс «страница X из Y».
/// </summary>
public class ParseJobService
{
    private readonly ConcurrentDictionary<Guid, ParseJob> _jobs = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ParseJobService> _logger;

    public ParseJobService(IServiceScopeFactory scopes, IWebHostEnvironment env, ILogger<ParseJobService> logger)
    {
        _scopes = scopes;
        _env = env;
        _logger = logger;
    }

    public Guid Start(string psuid, string name, string pdfPath, string? sourceFileName = null)
    {
        var job = new ParseJob { Id = Guid.NewGuid(), OwnerPsuid = psuid, Name = name, SourceFileName = sourceFileName };
        _jobs[job.Id] = job;

        _ = Task.Run(() => RunAsync(job, pdfPath));
        return job.Id;
    }

    public ParseJob? Get(string psuid, Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) && job.OwnerPsuid == psuid ? job : null;
    }

    private async Task RunAsync(ParseJob job, string pdfPath)
    {
        try
        {
            var progress = new Progress<PageProgress>(p =>
            {
                job.CurrentPage = p.CurrentPage;
                job.TotalPages = p.TotalPages;
            });

            var scheme = await Task.Run(() => PdfSchemeParser.ParseAuto(pdfPath, progress));
            if (scheme.Cells.Count == 0)
            {
                job.Status = ParseJobStatus.Failed;
                job.Error = "Не удалось распознать схему: сетка не найдена.";
                return;
            }

            // Фон клеток отключен: схема черно-белая, цвета даст палитра ниток.
            using var scope = _scopes.CreateScope();
            var patterns = scope.ServiceProvider.GetRequiredService<IPatternService>();
            var detail = await patterns.SaveSchemeAsync(job.OwnerPsuid, job.Name, scheme, job.SourceFileName);

            if (detail is null)
            {
                job.Status = ParseJobStatus.Failed;
                job.Error = "Не удалось сохранить схему (нет пользователя?).";
                return;
            }

            job.PatternId = detail.Id;

            // Шрифты-символы из PDF — пока temp-файл еще жив.
            try
            {
                var fontsDir = PatternService.FontsDir(_env, detail.Id);
                var toolsDir = Path.Combine(_env.ContentRootPath, "Tools");
                FontExtractor.ExtractFonts(
                    pdfPath,
                    fontsDir,
                    Path.Combine(toolsDir, "repair_font.py"),
                    Path.Combine(toolsDir, "rebuild_cmap.py"),
                    _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Шрифты для {PatternId} пропущены", detail.Id);
            }

            job.Status = ParseJobStatus.Done;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Распознавание {JobId} упало", job.Id);
            job.Status = ParseJobStatus.Failed;
            job.Error = "Ошибка распознавания.";
        }
        finally
        {
            if (File.Exists(pdfPath))
                File.Delete(pdfPath);
        }
    }
}

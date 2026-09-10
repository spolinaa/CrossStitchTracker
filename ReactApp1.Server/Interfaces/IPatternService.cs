using PatternTracker.Server.Models;

namespace PatternTracker.Server.Interfaces;

public interface IPatternService
{
    Task<List<PatternSummaryDto>> GetPatternsAsync(string psuid);

    Task<PatternDetailDto?> GetPatternAsync(string psuid, int id);

    Task<PatternPreviewDto?> GetPreviewAsync(string psuid, int id);

    Task<PatternPageDto?> GetPatternPageAsync(string psuid, int id, int page);

    Task<PatternDetailDto?> UploadAsync(string psuid, string name, Stream pdf, int height, int width);

    Task<PatternDetailDto?> SaveSchemeAsync(
        string psuid, string name, Parser.ParsedScheme scheme, string? sourceFileName = null);

    Task<bool> SetCellAsync(string psuid, int patternId, int page, int x, int y, bool isFinished);

    /// <summary>Массовая отметка всех клеток одного цвета. Возвращает число обновленных.</summary>
    Task<int> SetColorCellsAsync(string psuid, int patternId, int colorId, bool isFinished);

    Task<bool> DeleteAsync(string psuid, int patternId);

    Task<List<FlossKeyMatchDto>> MatchFlossKeyAsync(
        string psuid, int patternId, string brand, List<Parser.FlossKeyPair> pairs);

    Task<int> SaveFlossMapAsync(
        string psuid, int patternId, string brand, List<FlossMappingDto> mappings);

    Task<bool> SetFlossCodeAsync(
        string psuid, int patternId, int colorId, string brand, string code);
}

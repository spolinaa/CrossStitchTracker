
using Microsoft.Extensions.FileSystemGlobbing.Internal;

namespace PatternTracker.Server.Interfaces;

public interface IPatternService
{
    Task<List<Pattern>> GetPatterns(string psuid);

    Task<Pattern?> GetPattern(string psuid, int id);

    Task<int> AddPattern(string psuid, string jsonPath, string fontsPath);

    Task DeletePattern(string psuid, int id);
}
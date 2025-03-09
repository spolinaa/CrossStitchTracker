using Microsoft.Extensions.FileSystemGlobbing.Internal;
using System.Text.Json;
using PatternTracker.Server.Models.PatternData;
namespace PatternTracker.Server.Services;

public class PatternService
{
    private readonly TrackerContext _trackerContext;

    public PatternService(TrackerContext trackerContext)
    {
        _trackerContext = trackerContext;
    }

    public async Task<List<Table>> GetPatterns(string psuid)
    {
        return await GetPatternsByPsuid(psuid).ToListAsync();
    }

    public async Task<Table?> GetPattern(string psuid, int patternId)
    {
        var patternInfo = await GetPatternsByPsuid(psuid)
            .Where(x => x.Id == patternId)
            .FirstOrDefaultAsync();
        if (patternInfo is null)
            return null;

        var pattern = CreatePattern(patternInfo.DataPath);

        return patternInfo is null
            ? null
            : JObject.FromObject(new
            {
                fontsLink = patternModel.FontsPath,
                patternInfo
            });
    }

    private IQueryable<Table> GetPatternsByPsuid(string psuid)
    {
        return _trackerContext.Patterns
            .Where(x => x.User.YandexPsuid == psuid);
    }

    private Table? CreatePattern(string jsonPath)
    {
        var content = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<Table>(content);
    }

    public async Task<int> AddPattern(string psuid, string jsonPath, string fontsPath)
    {
        var user = _trackerContext.Users.Where(x => x.YandexPsuid == psuid).FirstOrDefault();
        if (user == null)
            return 0;

        _trackerContext.Patterns.Add(new Table { DataPath = jsonPath, User = user, FontsPath = fontsPath });
        await _trackerContext.SaveChangesAsync();

        return _trackerContext.Patterns
            .Where(x => x.DataPath == jsonPath && x.User == user && x.FontsPath == fontsPath)
            .Select(x => x.Id)
            .First();
    }

    public async Task DeletePattern(string psuid, int patternId)
    {
        var pattern = await GetPattern(psuid, patternId);
        if (pattern is null)
            throw new ApplicationException("Нет такой схемы");
        _trackerContext.Remove(pattern);
        await _trackerContext.SaveChangesAsync();
    }
}

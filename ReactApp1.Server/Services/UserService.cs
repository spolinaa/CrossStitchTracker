using Microsoft.EntityFrameworkCore;
using PatternTracker.Server.Data;
using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Models;

namespace PatternTracker.Server.Services;

public class UserService : IUserService
{
    private readonly TrackerDbContext _db;

    public UserService(TrackerDbContext db)
    {
        _db = db;
    }

    public async Task AddUser(YandexClientInfo client)
    {
        if (string.IsNullOrWhiteSpace(client.Psuid))
            throw new ArgumentException("В ответе Яндекса нет psuid.", nameof(client));

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.YandexPsuid == client.Psuid);
        if (existing is not null)
        {
            existing.Login = client.Login;
            existing.RealName = client.RealName;
        }
        else
        {
            _db.Users.Add(new User
            {
                YandexPsuid = client.Psuid,
                Login = client.Login,
                RealName = client.RealName,
                YandexId = client.Id
            });
        }

        await _db.SaveChangesAsync();
    }

    public int GetUserId(string psuid)
    {
        return _db.Users
            .Where(u => u.YandexPsuid == psuid)
            .Select(u => u.Id)
            .FirstOrDefault();
    }
}

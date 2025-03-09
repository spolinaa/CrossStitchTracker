using PatternTracker.Server.Models;

namespace PatternTracker.Server.Interfaces;

public interface IUserService
{
    Task AddUser(YandexClientInfo client);

    int GetUserId(string psuid);
}

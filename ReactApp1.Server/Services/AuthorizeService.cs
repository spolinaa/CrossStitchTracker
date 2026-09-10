using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Models;
using System.Net;
using System.Text.Json;

namespace PatternTracker.Server.Services;

public class AuthorizeService : IAuthorizeService
{
    private readonly IUserService _userService;
    private readonly ILogger<AuthorizeService> _logger;

    public AuthorizeService(IUserService userService, ILogger<AuthorizeService> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task<string?> Authorize(string accessToken)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("Authorization", $"OAuth {accessToken}");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync("https://login.yandex.ru/info?format=json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Не удалось соединиться с login.yandex.ru");
            throw new InvalidOperationException("yandex-unreachable");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("login.yandex.ru отклонил токен: {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"yandex-{(int)response.StatusCode}");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("login.yandex.ru вернул {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"yandex-{(int)response.StatusCode}");
        }

        var body = await response.Content.ReadAsStringAsync();
        var clientInfo = JsonSerializer.Deserialize<YandexClientInfo>(body);
        if (clientInfo?.Psuid is null)
        {
            _logger.LogWarning("В ответе Яндекса нет psuid");
            return null;
        }

        await _userService.AddUser(clientInfo);
        return clientInfo.Psuid;
    }
}

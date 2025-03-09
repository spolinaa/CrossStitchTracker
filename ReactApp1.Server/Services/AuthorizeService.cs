using PatternTracker.Server.Interfaces;
using PatternTracker.Server.Models;
using System.Text.Json;

namespace PatternTracker.Server.Services;

public class AuthorizeService : IAuthorizeService
{
    private readonly IUserService _userService;

    public AuthorizeService(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<string?> Authorize(string accessToken)
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"OAuth {accessToken}");
        var response = await client.GetStringAsync("https://login.yandex.ru/info");
        //JObject json = JObject.Parse(response);

        //dynamic? values = JsonConvert.DeserializeObject(response);
        //if (values == null)
        //    return null;

        var clientInfo = JsonSerializer.Deserialize<YandexClientInfo>(response);
        if (clientInfo is null)
            return null;

        await _userService.AddUser(clientInfo);
        return clientInfo.Psuid;
    }
}

using Microsoft.AspNetCore.Mvc;
using PatternTracker.Server.Interfaces;

namespace PatternTracker.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthorizeController : ControllerBase
{
    private IAuthorizeService _authorizeService;

    public AuthorizeController(IAuthorizeService authorizeService)
    {
        _authorizeService = authorizeService;
    }

    [HttpGet("/auth_yandex")]
    public async Task<ActionResult<string?>> Authorize(string access_token, string token_type, string expires_in)
    {
        try
        {
            return await _authorizeService.Authorize(access_token);
        }
        catch
        {
            return BadRequest();
        }
    }
}

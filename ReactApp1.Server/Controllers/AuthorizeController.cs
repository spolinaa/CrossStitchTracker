using Microsoft.AspNetCore.Mvc;
using PatternTracker.Server.Helpers;
using PatternTracker.Server.Interfaces;

namespace PatternTracker.Server.Controllers;

[ApiController]
public class AuthorizeController : ControllerBase
{
    private readonly IAuthorizeService _authorizeService;
    private readonly IUserService _userService;
    private readonly ILogger<AuthorizeController> _logger;

    public AuthorizeController(
        IAuthorizeService authorizeService,
        IUserService userService,
        ILogger<AuthorizeController> logger)
    {
        _authorizeService = authorizeService;
        _userService = userService;
        _logger = logger;
    }

    [HttpGet("/auth_yandex")]
    public async Task<ActionResult<string?>> Authorize(string access_token, string token_type, string expires_in)
    {
        try
        {
            var psuid = await _authorizeService.Authorize(access_token);
            if (psuid is null)
                return BadRequest(new { error = "no-psuid" });

            Response.Cookies.Append("psuid", psuid, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                Path = "/",
                SameSite = SameSiteMode.Lax
            });
            return Ok(psuid);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка входа через Яндекс");
            return BadRequest(new { error = "internal" });
        }
    }

    [HttpGet("/user")]
    public IActionResult CheckUser([FromHeader(Name = "Cookie")] string? cookie)
    {
        var psuid = CookieHelper.GetPsuidCookie(cookie);
        if (psuid is null)
            return Unauthorized();

        return _userService.GetUserId(psuid) == 0
            ? NotFound()
            : NoContent();
    }

    [HttpPost("/logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("psuid");
        return Ok();
    }
}

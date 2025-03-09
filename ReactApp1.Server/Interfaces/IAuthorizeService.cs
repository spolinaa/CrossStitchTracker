namespace PatternTracker.Server.Interfaces;

public interface IAuthorizeService
{
    Task<string?> Authorize(string accessToken);
}

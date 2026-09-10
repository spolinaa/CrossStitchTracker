namespace PatternTracker.Server.Helpers;

public static class CookieHelper
{
    public static string? GetPsuidCookie(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return null;

        var parts = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && kv[0] == "psuid" && !string.IsNullOrWhiteSpace(kv[1]))
                return kv[1];
        }

        return null;
    }
}

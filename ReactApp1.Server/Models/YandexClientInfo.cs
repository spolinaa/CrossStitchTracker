namespace PatternTracker.Server.Models
{
    public record YandexClientInfo
    {
        public string RealName { get; init; }

        public string Login { get; init; }

        public string Id { get; init; }

        public string Psuid { get; init; }
    }
}

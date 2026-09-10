using System.Text.Json.Serialization;

namespace PatternTracker.Server.Models
{
    public record YandexClientInfo
    {
        [JsonPropertyName("real_name")]
        public string? RealName { get; init; }

        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("psuid")]
        public string? Psuid { get; init; }
    }
}

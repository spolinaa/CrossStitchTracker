namespace PatternTracker.Server.Models.PatternData
{
    public record Cell
    {
        public int ColorId { get; init; }

        public bool IsFinished { get; init; } = false;
    }
}

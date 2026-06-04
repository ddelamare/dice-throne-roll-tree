namespace DiceThroneApi.Models;

public class TelemetrySummary
{
    public int TotalVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public int TotalOperations { get; set; }
    public Dictionary<string, int> OperationCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> HeroUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PageVisits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? LastUpdatedUtc { get; set; }
}

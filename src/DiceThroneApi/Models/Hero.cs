namespace DiceThroneApi.Models;

public class Hero
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<RollObjective> Objectives { get; set; } = new();
    // Hero-specific overrides for the default token evaluation value.
    public Dictionary<string, double> TokenValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<int, double>> TokenThresholdBonuses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

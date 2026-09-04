namespace DiceThroneApi.Models;

public class EvaluationConfig
{
    // Values used to convert non-damage outcomes into the same "delta" unit.
    // Default: heal=1, card=1, cp=1, default token value=2.
    public double HealValue { get; set; } = 1.0;
    public double CardValue { get; set; } = 3.0;
    public double CpValue { get; set; } = 1.0;
    public double DefaultTokenValue { get; set; } = 2.0;
    public double EnemyDefenseDelta { get; set; } = 3.0; // How much to subtract from damage when calculating expected value (to account for enemy defense)
    public Dictionary<string, double> TokenValues { get; set; } = new();

    public void ApplyHeroDefaults(IReadOnlyDictionary<string, double>? heroTokenValues)
    {
        if (heroTokenValues == null) return;

        foreach (var (token, value) in heroTokenValues)
        {
            // Request/UI supplied values always win over hero defaults.
            if (!TokenValues.ContainsKey(token))
                TokenValues[token] = value;
        }
    }
}

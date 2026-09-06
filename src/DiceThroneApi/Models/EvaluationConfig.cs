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
    public Dictionary<string, double> TokenValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Additional value awarded each time a token count reaches a configured milestone.
    // The base TokenValues entry remains useful for incremental effects.
    public Dictionary<string, Dictionary<int, double>> TokenThresholdBonuses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void ApplyHeroDefaults(IReadOnlyDictionary<string, double>? heroTokenValues)
        => ApplyHeroDefaults(heroTokenValues, null);

    public void ApplyHeroDefaults(
        IReadOnlyDictionary<string, double>? heroTokenValues,
        IReadOnlyDictionary<string, Dictionary<int, double>>? heroTokenThresholdBonuses)
    {
        if (heroTokenValues != null)
        {
            foreach (var (token, value) in heroTokenValues)
            {
                // Request/UI supplied values always win over hero defaults.
                if (!TokenValues.ContainsKey(token))
                    TokenValues[token] = value;
            }
        }

        if (heroTokenThresholdBonuses != null)
        {
            foreach (var (token, bonuses) in heroTokenThresholdBonuses)
            {
                if (!TryGetThresholdBonuses(token, out var configured))
                {
                    TokenThresholdBonuses[token] = new Dictionary<int, double>(bonuses);
                    continue;
                }

                // Explicit request/UI milestones win; fill only missing milestones.
                foreach (var (threshold, bonus) in bonuses)
                {
                    if (!configured.ContainsKey(threshold))
                        configured[threshold] = bonus;
                }
            }
        }
    }

    public double CalculateTokenDelta(IEnumerable<string>? tokens)
    {
        if (tokens == null)
            return 0.0;

        double delta = 0.0;
        foreach (var group in tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase))
        {
            var token = group.Key;
            var count = group.Count();
            delta += count * (TryGetTokenValue(token, out var value) ? value : DefaultTokenValue);

            if (TryGetThresholdBonuses(token, out var bonuses))
            {
                foreach (var (threshold, bonus) in bonuses)
                {
                    if (threshold > 0)
                        delta += count / threshold * bonus;
                }
            }
        }

        return delta;
    }

    private bool TryGetTokenValue(string token, out double value)
    {
        if (TokenValues.TryGetValue(token, out value))
            return true;

        var match = TokenValues.FirstOrDefault(entry => entry.Key.Equals(token, StringComparison.OrdinalIgnoreCase));
        value = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }

    private bool TryGetThresholdBonuses(string token, out Dictionary<int, double> bonuses)
    {
        if (TokenThresholdBonuses.TryGetValue(token, out bonuses!))
            return true;

        var match = TokenThresholdBonuses.FirstOrDefault(entry => entry.Key.Equals(token, StringComparison.OrdinalIgnoreCase));
        bonuses = match.Value!;
        return !string.IsNullOrEmpty(match.Key);
    }
}

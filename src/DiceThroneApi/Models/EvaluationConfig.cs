namespace DiceThroneApi.Models;

public class EvaluationConfig
{
    // Values used to convert non-damage outcomes into the same "delta" unit.
    // Default: heal=1, card=1, cp=1, default token value=2.
    public double HealValue { get; set; } = 1.0;
    public double CardValue { get; set; } = 1.0;
    public double CpValue { get; set; } = 1.0;
    public double DefaultTokenValue { get; set; } = 2.0;
    public Dictionary<string, double> TokenValues { get; set; } = new();
}

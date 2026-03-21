namespace DiceThroneApi.Models;

public class RollAdvice
{
    public string ObjectiveName { get; set; } = string.Empty;
    public List<bool> DiceToKeep { get; set; } = new();
    public double Probability { get; set; }
    public string CalculationMethod { get; set; } = string.Empty;
    public int Damage { get; set; }
    public double ExpectedDamage { get; set; }
    public string? FallbackObjectiveName { get; set; }
    public double FallbackProbability { get; set; }
    public double FallbackExpectedDamage { get; set; }
}

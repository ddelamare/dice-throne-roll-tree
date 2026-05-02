namespace DiceThroneApi.Models;

public class RollAdvice
{
    public string ObjectiveName { get; set; } = string.Empty;
    public string ObjectiveNotation { get; set; } = string.Empty;
    public List<bool> DiceToKeep { get; set; } = new();
    public double Probability { get; set; }
    public string CalculationMethod { get; set; } = string.Empty;
    public int Damage { get; set; }
    public int Heal { get; set; }
    public int Cards { get; set; }
    public int Cp { get; set; }
    public List<string> Tokens { get; set; } = new();
    /// <summary>
    /// Expected delta = probability × (damage + heal + cards×cardValue + cp×cpValue + tokens×tokenValue).
    /// </summary>
    public double ExpectedDelta { get; set; }
    public string? FallbackObjectiveName { get; set; }
    public double FallbackProbability { get; set; }
    public double FallbackExpectedDelta { get; set; }
    
    /// <summary>
    /// Probability of hitting the objective if all dice are rerolled (baseline comparison).
    /// </summary>
    public double BaselineProbability { get; set; }
    
    /// <summary>
    /// Improvement in probability from optimal keep strategy vs rerolling all dice.
    /// Calculated as (Probability - BaselineProbability).
    /// </summary>
    public double ProbabilityImprovement { get; set; }
    
    /// <summary>
    /// The index of the objective in the hero's objectives list (for board order sorting).
    /// </summary>
    public int Index { get; set; }
}

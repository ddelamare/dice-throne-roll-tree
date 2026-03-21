using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class DiceRollAdvisor
{
    private const double ExpectedDamageTieTolerance = 1e-12;
    private readonly ProbabilityCalculator _calculator;
    private readonly MonteCarloSimulator _simulator;

    public DiceRollAdvisor(ProbabilityCalculator calculator, MonteCarloSimulator simulator)
    {
        _calculator = calculator;
        _simulator = simulator;
    }

    public List<RollAdvice> GetAdvice(List<int> currentDice, int rollsRemaining, List<RollObjective> objectives, string method = "analytic")
    {
        var advice = new List<RollAdvice>();

        foreach (var objective in objectives)
        {
            // Always use optimal keep strategy from the analytic calculator
            var optimalProb = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);
            
            // Calculate baseline probability (if we reroll all dice)
            var baselineProb = CalculateBaselineProbability(currentDice, rollsRemaining, objective);
            
            // Use Monte Carlo for probability if requested, but always use optimal keep strategy
            var prob = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase)
                ? _simulator.Simulate(objective, currentDice.Count, 10000, rollsRemaining)
                : optimalProb;
            
            advice.Add(new RollAdvice
            {
                ObjectiveName = objective.Name,
                DiceToKeep = toKeep,
                Probability = prob,
                CalculationMethod = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase) ? "Monte Carlo" : "Analytic",
                Damage = objective.Damage,
                ExpectedDamage = prob * objective.Damage,
                BaselineProbability = baselineProb,
                ProbabilityImprovement = optimalProb - baselineProb
            });
        }

        // Compute fallback for each objective: given the dice locked in for this objective,
        // which other damage-dealing objective has the best expected value?
        if (rollsRemaining > 0)
        {
            var damageObjectives = objectives.Where(o => o.Damage > 0).ToList();

            foreach (var a in advice)
            {
                var thisObjective = objectives.FirstOrDefault(o => o.Name == a.ObjectiveName);
                if (thisObjective == null || thisObjective.Damage == 0) continue;

                var others = damageObjectives.Where(o => o.Name != a.ObjectiveName).ToList();
                if (others.Count == 0) continue;

                RollObjective? bestFallbackObj = null;
                double bestFallbackProb = 0.0;
                double bestFallbackExpected = 0.0;

                foreach (var other in others)
                {
                    var fallbackProb = _calculator.CalculateWithForcedKeep(
                        currentDice, rollsRemaining, other, a.DiceToKeep);
                    var expected = fallbackProb * other.Damage;
                    if (expected > bestFallbackExpected)
                    {
                        bestFallbackExpected = expected;
                        bestFallbackProb = fallbackProb;
                        bestFallbackObj = other;
                    }
                }

                if (bestFallbackObj != null && bestFallbackExpected > 0)
                {
                    a.FallbackObjectiveName = bestFallbackObj.Name;
                    a.FallbackProbability = bestFallbackProb;
                    a.FallbackExpectedDamage = bestFallbackExpected;
                }
            }
        }

        return advice
            .OrderByDescending(a => a.ExpectedDamage)
            .ThenByDescending(a => a.Probability)
            .ToList();
    }

    /// <summary>
    /// Computes the globally optimal strategy across all damage-dealing objectives,
    /// returning the advice for the objective with the highest expected damage.
    /// </summary>
    public RollAdvice? GetBestOverallStrategy(List<int> currentDice, int rollsRemaining, List<RollObjective> objectives)
    {
        RollAdvice? bestAdvice = null;
        double bestExpectedDamage = double.NegativeInfinity;
        double bestProbability = double.NegativeInfinity;

        foreach (var objective in objectives.Where(o => o.Damage > 0))
        {
            var prob = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);
            var expectedDamage = prob * objective.Damage;

            if (expectedDamage > bestExpectedDamage
                || (Math.Abs(expectedDamage - bestExpectedDamage) < ExpectedDamageTieTolerance && prob > bestProbability))
            {
                bestExpectedDamage = expectedDamage;
                bestProbability = prob;
                var baselineProb = CalculateBaselineProbability(currentDice, rollsRemaining, objective);
                
                bestAdvice = new RollAdvice
                {
                    ObjectiveName = objective.Name,
                    DiceToKeep = toKeep,
                    Probability = prob,
                    CalculationMethod = "Analytic",
                    Damage = objective.Damage,
                    ExpectedDamage = expectedDamage,
                    BaselineProbability = baselineProb,
                    ProbabilityImprovement = prob - baselineProb
                };
            }
        }

        return bestAdvice;
    }

    /// <summary>
    /// Calculates the probability of hitting an objective if all dice are rerolled.
    /// </summary>
    private double CalculateBaselineProbability(List<int> currentDice, int rollsRemaining, RollObjective objective)
    {
        if (rollsRemaining <= 0)
        {
            return 0.0;
        }
        
        var baselineKeep = Enumerable.Repeat(false, currentDice.Count).ToList();
        return _calculator.CalculateWithForcedKeep(currentDice, rollsRemaining, objective, baselineKeep);
    }

    private List<bool> GreedyKeep(List<int> dice, RollObjective objective)
    {
        var toKeep = new List<bool>();

        if (objective.Type == ObjectiveType.Standard)
        {
            var groupNeeds = objective.Groups.Select(g => g.AllowedValues.ToHashSet()).ToList();
            var used = new bool[groupNeeds.Count];

            for (int i = 0; i < dice.Count; i++)
            {
                bool kept = false;
                for (int g = 0; g < groupNeeds.Count; g++)
                {
                    if (!used[g] && groupNeeds[g].Contains(dice[i]))
                    {
                        used[g] = true;
                        kept = true;
                        break;
                    }
                }
                toKeep.Add(kept);
            }
        }
        else if (objective.Type == ObjectiveType.SmallStraight)
        {
            var straightVals = new HashSet<int> { 1, 2, 3, 4, 5 };
            for (int i = 0; i < dice.Count; i++)
            {
                toKeep.Add(straightVals.Contains(dice[i]));
            }
        }
        else if (objective.Type == ObjectiveType.LargeStraight)
        {
            var straightVals = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
            for (int i = 0; i < dice.Count; i++)
            {
                toKeep.Add(straightVals.Contains(dice[i]));
            }
        }

        return toKeep;
    }
}

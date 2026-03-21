using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class DiceRollAdvisor
{
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
            if (method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase))
            {
                var prob = _simulator.Simulate(objective, currentDice.Count, 10000, rollsRemaining);
                var toKeep = GreedyKeep(currentDice, objective);
                
                advice.Add(new RollAdvice
                {
                    ObjectiveName = objective.Name,
                    DiceToKeep = toKeep,
                    Probability = prob,
                    CalculationMethod = "Monte Carlo",
                    Damage = objective.Damage,
                    ExpectedDamage = prob * objective.Damage
                });
            }
            else
            {
                var prob = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);
                
                advice.Add(new RollAdvice
                {
                    ObjectiveName = objective.Name,
                    DiceToKeep = toKeep,
                    Probability = prob,
                    CalculationMethod = "Analytic",
                    Damage = objective.Damage,
                    ExpectedDamage = prob * objective.Damage
                });
            }
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

        return advice.OrderByDescending(a => a.Probability).ThenByDescending(a => a.ExpectedDamage).ToList()  ;
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

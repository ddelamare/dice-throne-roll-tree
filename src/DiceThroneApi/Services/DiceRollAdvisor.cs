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

    public List<RollAdvice> GetAdvice(
        List<int> currentDice,
        int rollsRemaining,
        List<RollObjective> objectives,
        string method = "analytic",
        List<bool>? lockedDiceMask = null,
        DiceThroneApi.Models.EvaluationConfig? eval = null)
    {
        var advice = new List<RollAdvice>();
        eval ??= new DiceThroneApi.Models.EvaluationConfig();

        foreach (var (objective, index) in objectives.Select((o, i) => (o, i)))
        {
            // Always use optimal keep strategy from the analytic calculator
            var optimalProb = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep, lockedDiceMask);
            
            // Calculate baseline probability (if we reroll all dice)
            var baselineProb = CalculateBaselineProbability(currentDice, rollsRemaining, objective, lockedDiceMask);
            
            // Use Monte Carlo for probability if requested, but always use optimal keep strategy.
            // If the current dice already satisfy the objective (optimalProb == 1.0), skip the
            // simulation and return 100% immediately.
            var prob = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase)
                ? (optimalProb >= 1.0 ? 1.0 : _simulator.Simulate(objective, currentDice.Count, MonteCarloConst.StandardIterations, rollsRemaining))
                : optimalProb;
            
            var objectiveDelta = ComputeDelta(objective, eval);

            advice.Add(new RollAdvice
            {
                ObjectiveName = objective.Name,
                ObjectiveNotation = objective.Notation,
                DiceToKeep = toKeep,
                Probability = prob,
                CalculationMethod = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase) ? "Monte Carlo" : "Analytic",
                Damage = objective.Damage,
                Heal = objective.Heal,
                Cards = objective.Cards,
                Cp = objective.Cp,
                Tokens = new List<string>(objective.Tokens),
                ExpectedDelta = prob * objectiveDelta,
                BaselineProbability = baselineProb,
                ProbabilityImprovement = optimalProb - baselineProb,
                Index = index
            });
        }

        // Compute fallback for each objective: given the dice locked in for this objective,
        // which other damage-dealing objective has the best expected value?
        if (rollsRemaining > 0)
        {
            var damageObjectives = objectives.Where(o => ComputeDelta(o, eval) > 0).ToList();

            foreach (var a in advice)
            {
                var thisObjective = objectives.FirstOrDefault(o => o.Name == a.ObjectiveName);
                if (thisObjective == null || ComputeDelta(thisObjective, eval) == 0) continue;

                var others = damageObjectives.Where(o => o.Name != a.ObjectiveName).ToList();
                if (others.Count == 0) continue;

                RollObjective? bestFallbackObj = null;
                double bestFallbackProb = 0.0;
                double bestFallbackExpected = 0.0;

                foreach (var other in others)
                {
                    var fallbackProb = _calculator.CalculateWithForcedKeep(
                        currentDice, rollsRemaining, other, a.DiceToKeep);
                    var expected = fallbackProb * ComputeDelta(other, eval);
                    if (fallbackProb > bestFallbackProb)
                    {
                        bestFallbackExpected = expected;
                        bestFallbackProb = fallbackProb;
                        bestFallbackObj = other;
                    }
                }

                if (bestFallbackObj != null)
                {
                    a.FallbackObjectiveName = bestFallbackObj.Name;
                    a.FallbackProbability = bestFallbackProb;
                    a.FallbackExpectedDelta = bestFallbackExpected;
                }
            }
        }

        return advice
            .OrderByDescending(a => a.ExpectedDelta)
            .ThenByDescending(a => a.Probability)
            .ToList();
    }

    /// <summary>
    /// Computes the globally optimal strategy across all damage-dealing objectives,
    /// returning the advice for the objective with the highest expected damage.
    /// </summary>
    public RollAdvice? GetBestOverallStrategy(List<int> currentDice, int rollsRemaining, List<RollObjective> objectives, DiceThroneApi.Models.EvaluationConfig? eval = null)
    {
        eval ??= new DiceThroneApi.Models.EvaluationConfig();
        RollAdvice? bestAdvice = null;
        double bestExpectedDamage = double.NegativeInfinity;
        double bestProbability = double.NegativeInfinity;

        foreach (var objective in objectives.Where(o => ComputeDelta(o, eval) > 0))
        {
            var prob = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);
            var expectedDamage = prob * ComputeDelta(objective, eval);

            if (expectedDamage > bestExpectedDamage
                || (Math.Abs(expectedDamage - bestExpectedDamage) < ExpectedDamageTieTolerance && prob > bestProbability))
            {
                bestExpectedDamage = expectedDamage;
                bestProbability = prob;
                var baselineProb = CalculateBaselineProbability(currentDice, rollsRemaining, objective, null);
                
                bestAdvice = new RollAdvice
                {
                    ObjectiveName = objective.Name,
                    ObjectiveNotation = objective.Notation,
                    DiceToKeep = toKeep,
                    Probability = prob,
                    CalculationMethod = "Analytic",
                    Damage = objective.Damage,
                    Heal = objective.Heal,
                    Cards = objective.Cards,
                    Cp = objective.Cp,
                    Tokens = new List<string>(objective.Tokens),
                    ExpectedDelta = expectedDamage,
                    BaselineProbability = baselineProb,
                    ProbabilityImprovement = prob - baselineProb
                };
            }
        }

        return bestAdvice;
    }

    private double ComputeDelta(RollObjective objective, DiceThroneApi.Models.EvaluationConfig eval)
    {
        // Sum base components: damage + heal + cards*cardValue + cp*cpValue + per-token values
        double delta = 0.0;
        delta += objective.Damage;
        delta += objective.Heal * eval.HealValue;
        delta += objective.Cards * eval.CardValue;
        delta += objective.Cp * eval.CpValue;

        foreach (var token in objective.Tokens ?? new List<string>())
        {
            if (eval.TokenValues != null && eval.TokenValues.TryGetValue(token, out var val))
            {
                delta += val;
            }
            else
            {
                delta += eval.DefaultTokenValue;
            }
        }

        return delta;
    }

    /// <summary>
    /// Calculates the probability of hitting an objective if all dice are rerolled.
    /// </summary>
    private double CalculateBaselineProbability(
        List<int> currentDice,
        int rollsRemaining,
        RollObjective objective,
        List<bool>? lockedDiceMask)
    {
        if (rollsRemaining <= 0)
        {
            return 0.0;
        }
        
        var baselineKeep = lockedDiceMask != null && lockedDiceMask.Count == currentDice.Count
            ? new List<bool>(lockedDiceMask)
            : Enumerable.Repeat(false, currentDice.Count).ToList();

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

internal class MonteCarloConst
{
    internal const int StandardIterations = 1000;
}
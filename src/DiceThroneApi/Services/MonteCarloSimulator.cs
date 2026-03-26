using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class MonteCarloSimulator
{
    private readonly ObjectiveMatcher _matcher;
    private readonly ProbabilityCalculator? _probabilityCalculator;
    private readonly bool _useOptimalStrategy;

    public MonteCarloSimulator(ObjectiveMatcher matcher)
    {
        _matcher = matcher;
        _useOptimalStrategy = false;
    }

    public MonteCarloSimulator(ObjectiveMatcher matcher, ProbabilityCalculator probabilityCalculator, bool useOptimalStrategy = false)
    {
        _matcher = matcher;
        _probabilityCalculator = probabilityCalculator;
        _useOptimalStrategy = useOptimalStrategy;
    }

    /// <summary>
    /// Simulates the probability of hitting <paramref name="objective"/> when the caller has
    /// already committed to keeping the dice specified by <paramref name="forcedKeep"/> and
    /// re-rolling the remaining dice.  After that forced re-roll, optimal play (when a
    /// <see cref="ProbabilityCalculator"/> was supplied and <c>useOptimalStrategy</c> is
    /// <c>true</c>) or the greedy heuristic continues for the remaining
    /// <c>rollsRemaining - 1</c> rerolls.
    /// </summary>
    public double SimulateWithForcedKeep(
        List<int> currentDice,
        int rollsRemaining,
        RollObjective objective,
        List<bool> forcedKeep,
        int iterations = 50000)
    {
        var successes = 0;
        for (int i = 0; i < iterations; i++)
        {
            if (SimulateFromForcedKeep(currentDice, rollsRemaining, objective, forcedKeep))
                successes++;
        }
        return (double)successes / iterations;
    }

    private bool SimulateFromForcedKeep(
        List<int> currentDice,
        int rollsRemaining,
        RollObjective objective,
        List<bool> forcedKeep)
    {
        var totalDice = currentDice.Count;

        // Apply the forced keep: keep the specified dice, re-roll the rest.
        var kept = currentDice.Zip(forcedKeep, (d, k) => (d, k))
                              .Where(x => x.k).Select(x => x.d).ToList();
        var dice = new List<int>(kept);
        dice.AddRange(RollDice(totalDice - kept.Count));

        if (_matcher.IsMatch(dice, objective)) return true;

        // Play out the remaining rerolls using optimal or greedy strategy.
        for (var rerollsLeft = rollsRemaining - 1; rerollsLeft > 0; rerollsLeft--)
        {
            List<bool> toKeep;
            if (_useOptimalStrategy && _probabilityCalculator != null)
                _probabilityCalculator.CalculateBestKeep(dice, rerollsLeft, objective, out toKeep);
            else
                toKeep = DecideKeep(dice, objective);

            kept = dice.Zip(toKeep, (d, k) => (d, k))
                       .Where(x => x.k).Select(x => x.d).ToList();
            dice = new List<int>(kept);
            dice.AddRange(RollDice(totalDice - kept.Count));

            if (_matcher.IsMatch(dice, objective)) return true;
        }

        return false;
    }

    public double Simulate(RollObjective objective, int totalDice, int iterations = 10000, int rerolls = 2)
    {
        var successes = 0;

        for (int i = 0; i < iterations; i++)
        {
            if (SimulateOneGame(objective, totalDice, rerolls))
            {
                successes++;
            }
        }

        return (double)successes / iterations;
    }

    private bool SimulateOneGame(RollObjective objective, int totalDice, int rerollsLeft)
    {
        var dice = RollDice(totalDice);

        for (int roll = 0; roll <= rerollsLeft; roll++)
        {
            if (_matcher.IsMatch(dice, objective))
            {
                return true;
            }

            if (roll < rerollsLeft)
            {
                List<bool> toKeep;
                
                // Use optimal strategy if enabled and calculator is available
                if (_useOptimalStrategy && _probabilityCalculator != null)
                {
                    _probabilityCalculator.CalculateBestKeep(dice, rerollsLeft - roll, objective, out toKeep);
                }
                else
                {
                    toKeep = DecideKeep(dice, objective);
                }
                
                var kept = new List<int>();
                for (int i = 0; i < dice.Count; i++)
                {
                    if (toKeep[i])
                    {
                        kept.Add(dice[i]);
                    }
                }

                var rerollCount = totalDice - kept.Count;
                var newRolls = RollDice(rerollCount);
                dice = new List<int>(kept);
                dice.AddRange(newRolls);
            }
        }

        return _matcher.IsMatch(dice, objective);
    }

    private List<int> RollDice(int count)
    {
        var result = new List<int>();
        for (int i = 0; i < count; i++)
        {
            result.Add(Random.Shared.Next(1, 7));
        }
        return result;
    }

    private List<bool> DecideKeep(List<int> dice, RollObjective objective)
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
            toKeep = DecideKeepSmallStraight(dice);
        }
        else if (objective.Type == ObjectiveType.LargeStraight)
        {
            toKeep = DecideKeepLargeStraight(dice);
        }

        return toKeep;
    }

    /// <summary>
    /// Improved SmallStraight keep strategy:
    /// 1. Considers all three possible small straights (1234, 2345, 3456)
    /// 2. Picks the one with the most matching dice
    /// 3. Keeps at most one of each value (handles duplicates)
    /// </summary>
    private List<bool> DecideKeepSmallStraight(List<int> dice)
    {
        var candidates = new[]
        {
            new HashSet<int> { 1, 2, 3, 4 },
            new HashSet<int> { 2, 3, 4, 5 },
            new HashSet<int> { 3, 4, 5, 6 }
        };

        // Score each candidate by how many unique values we already have
        var diceSet = dice.ToHashSet();
        var bestCandidate = candidates
            .Select(c => (candidate: c, score: c.Count(v => diceSet.Contains(v))))
            .OrderByDescending(x => x.score)
            .First()
            .candidate;

        // Keep at most one of each value
        var keptValues = new HashSet<int>();
        var toKeep = new List<bool>();

        foreach (var d in dice)
        {
            if (bestCandidate.Contains(d) && !keptValues.Contains(d))
            {
                toKeep.Add(true);
                keptValues.Add(d);
            }
            else
            {
                toKeep.Add(false);
            }
        }

        return toKeep;
    }

    /// <summary>
    /// Improved LargeStraight keep strategy:
    /// 1. Considers both possible large straights (12345, 23456)
    /// 2. Picks the one with the most matching dice
    /// 3. Keeps at most one of each value (handles duplicates)
    /// </summary>
    private List<bool> DecideKeepLargeStraight(List<int> dice)
    {
        var candidates = new[]
        {
            new HashSet<int> { 1, 2, 3, 4, 5 },
            new HashSet<int> { 2, 3, 4, 5, 6 }
        };

        // Score each candidate by how many unique values we already have
        var diceSet = dice.ToHashSet();
        var bestCandidate = candidates
            .Select(c => (candidate: c, score: c.Count(v => diceSet.Contains(v))))
            .OrderByDescending(x => x.score)
            .First()
            .candidate;

        // Keep at most one of each value
        var keptValues = new HashSet<int>();
        var toKeep = new List<bool>();

        foreach (var d in dice)
        {
            if (bestCandidate.Contains(d) && !keptValues.Contains(d))
            {
                toKeep.Add(true);
                keptValues.Add(d);
            }
            else
            {
                toKeep.Add(false);
            }
        }

        return toKeep;
    }
}

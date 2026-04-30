using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class MonteCarloSimulator
{
    private readonly ObjectiveMatcher _matcher;
    private readonly ProbabilityCalculator? _probabilityCalculator;
    private readonly bool _useOptimalStrategy;

    public MonteCarloSimulator(ObjectiveMatcher matcher, ProbabilityCalculator? probabilityCalculator = null)
    {
        _matcher = matcher;
        _probabilityCalculator = probabilityCalculator;
        _useOptimalStrategy = probabilityCalculator != null;
    }

    public double Simulate(RollObjective objective, int totalDice, int iterations = MonteCarloConst.StandardIterations, int rerolls = 2)
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

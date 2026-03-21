using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class MonteCarloSimulator
{
    private readonly ObjectiveMatcher _matcher;

    public MonteCarloSimulator(ObjectiveMatcher matcher)
    {
        _matcher = matcher;
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
                var toKeep = DecideKeep(dice, objective);
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
            var straightVals = new HashSet<int> { 1, 2, 3, 4, 5 };
            for (int i = 0; i < dice.Count; i++)
            {
                toKeep.Add(straightVals.Contains(dice[i]));
            }
        }
        else if (objective.Type == ObjectiveType.LargeStraight)
        {
            var straightVals = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
            var diceSet = dice.ToHashSet();
            bool has12345 = new[] { 1, 2, 3, 4, 5 }.All(v => diceSet.Contains(v));
            bool has23456 = new[] { 2, 3, 4, 5, 6 }.All(v => diceSet.Contains(v));

            if (has12345)
            {
                straightVals = new HashSet<int> { 1, 2, 3, 4, 5 };
            }
            else if (has23456)
            {
                straightVals = new HashSet<int> { 2, 3, 4, 5, 6 };
            }

            for (int i = 0; i < dice.Count; i++)
            {
                toKeep.Add(straightVals.Contains(dice[i]));
            }
        }

        return toKeep;
    }
}

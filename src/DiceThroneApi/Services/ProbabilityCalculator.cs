using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class ProbabilityCalculator
{
    private readonly ObjectiveMatcher _matcher;

    public ProbabilityCalculator(ObjectiveMatcher matcher)
    {
        _matcher = matcher;
    }

    public double Calculate(RollObjective objective, int totalDice, int initialRolls = 1, int rerolls = 2)
    {
        var memo = new Dictionary<string, double>();
        var totalProb = 0.0;
        var totalOutcomes = (long)Math.Pow(6, totalDice);

        foreach (var initialRoll in GenerateAllRolls(totalDice))
        {
            totalProb += OptimalProbability(initialRoll, rerolls, objective, memo);
        }

        return totalProb / totalOutcomes;
    }

    private double OptimalProbability(List<int> dice, int rerollsLeft, RollObjective objective, Dictionary<string, double> memo)
    {
        if (_matcher.IsMatch(dice, objective))
        {
            return 1.0;
        }

        if (rerollsLeft == 0)
        {
            return 0.0;
        }

        var sortedDice = dice.OrderBy(x => x).ToList();
        var key = $"{string.Join(",", sortedDice)}:{rerollsLeft}";

        if (memo.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bestProb = 0.0;

        for (int mask = 0; mask < (1 << dice.Count); mask++)
        {
            var kept = new List<int>();
            var rerollCount = 0;

            for (int i = 0; i < dice.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    kept.Add(dice[i]);
                }
                else
                {
                    rerollCount++;
                }
            }

            if (rerollCount == 0)
            {
                continue;
            }

            var prob = 0.0;
            var totalOutcomes = (long)Math.Pow(6, rerollCount);

            foreach (var rerollResult in GenerateAllRolls(rerollCount))
            {
                var newDice = new List<int>(kept);
                newDice.AddRange(rerollResult);
                prob += OptimalProbability(newDice, rerollsLeft - 1, objective, memo);
            }

            prob /= totalOutcomes;
            bestProb = Math.Max(bestProb, prob);
        }

        memo[key] = bestProb;
        return bestProb;
    }

    private IEnumerable<List<int>> GenerateAllRolls(int diceCount)
    {
        if (diceCount == 0)
        {
            yield return new List<int>();
            yield break;
        }

        foreach (var rest in GenerateAllRolls(diceCount - 1))
        {
            for (int val = 1; val <= 6; val++)
            {
                var roll = new List<int>(rest) { val };
                yield return roll;
            }
        }
    }

    public double CalculateBestKeep(List<int> currentDice, int rerollsLeft, RollObjective objective, out List<bool> bestKeep)
    {
        bestKeep = new List<bool>();
        
        if (_matcher.IsMatch(currentDice, objective))
        {
            bestKeep = Enumerable.Repeat(true, currentDice.Count).ToList();
            return 1.0;
        }

        if (rerollsLeft == 0)
        {
            bestKeep = Enumerable.Repeat(true, currentDice.Count).ToList();
            return 0.0;
        }

        var memo = new Dictionary<string, double>();
        var bestProb = 0.0;
        var bestMask = 0;

        for (int mask = 1; mask < (1 << currentDice.Count); mask++)
        {
            var kept = new List<int>();
            var rerollCount = 0;

            for (int i = 0; i < currentDice.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    kept.Add(currentDice[i]);
                }
                else
                {
                    rerollCount++;
                }
            }

            if (rerollCount == 0)
            {
                continue;
            }

            var prob = 0.0;
            var totalOutcomes = (long)Math.Pow(6, rerollCount);

            foreach (var rerollResult in GenerateAllRolls(rerollCount))
            {
                var newDice = new List<int>(kept);
                newDice.AddRange(rerollResult);
                prob += OptimalProbability(newDice, rerollsLeft - 1, objective, memo);
            }

            prob /= totalOutcomes;

            if (prob > bestProb)
            {
                bestProb = prob;
                bestMask = mask;
            }
        }

        for (int i = 0; i < currentDice.Count; i++)
        {
            bestKeep.Add((bestMask & (1 << i)) != 0);
        }

        return bestProb;
    }
}

using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class ProbabilityCalculator
{
    private readonly ObjectiveMatcher _matcher;

    // Precomputed factorials for multinomial coefficients (0! through 10!).
    // The API allows up to 7 dice per roll, so the maximum total dice count is 7, and each
    // reroll uses at most 7 dice — 7! = 5040 is the largest value needed. The table extends
    // to 10! to accommodate any future increase in the maximum dice count.
    private static readonly long[] _factorials = { 1L, 1L, 2L, 6L, 24L, 120L, 720L, 5040L, 40320L, 362880L, 3628800L };

    public ProbabilityCalculator(ObjectiveMatcher matcher)
    {
        _matcher = matcher;
    }

    public double Calculate(RollObjective objective, int totalDice, int initialRolls = 1, int rerolls = 2)
    {
        var memo = new Dictionary<long, double>();
        var totalProb = 0.0;
        var totalOutcomes = (long)Math.Pow(6, totalDice);

        // Optimization 3: enumerate only distinct initial rolls with multinomial weights
        foreach (var (histogram, multiplicity) in GenerateDistinctRolls(totalDice))
        {
            totalProb += multiplicity * OptimalProbability(histogram, rerolls, objective, memo);
        }

        return totalProb / totalOutcomes;
    }

    public double CalculatePreRoll(RollObjective objective, int totalDice, List<bool>? lockedDiceMask = null, int rerolls = 2)
    {
        var normalizedLockedDiceMask = NormalizeKeepMask(lockedDiceMask, totalDice);
        if (!normalizedLockedDiceMask.Any(isLocked => isLocked))
        {
            return Calculate(objective, totalDice, rerolls: rerolls);
        }

        var lockedIndexes = normalizedLockedDiceMask
            .Select((isLocked, index) => new { isLocked, index })
            .Where(x => x.isLocked)
            .Select(x => x.index)
            .ToList();

        var unlockedDiceCount = totalDice - lockedIndexes.Count;
        var totalProb = 0.0;
        var totalOutcomes = (long)Math.Pow(6, totalDice);

        foreach (var lockedValues in GenerateOrderedRolls(lockedIndexes.Count))
        {
            foreach (var (unlockedHistogram, multiplicity) in GenerateDistinctRolls(unlockedDiceCount))
            {
                var initialDice = BuildInitialDice(totalDice, lockedIndexes, lockedValues, unlockedHistogram);
                var probability = unlockedDiceCount == 0
                    ? (_matcher.IsMatch(initialDice, objective) ? 1.0 : 0.0)
                    : CalculateBestKeep(initialDice, rerolls, objective, out _, normalizedLockedDiceMask);

                totalProb += multiplicity * probability;
            }
        }

        return totalProb / totalOutcomes;
    }

    // Optimization 1: long memo key; Optimization 2: histogram state
    private double OptimalProbability(int[] histogram, int rerollsLeft, RollObjective objective, Dictionary<long, double> memo)
    {
        var dice = HistogramToDice(histogram);

        if (_matcher.IsMatch(dice, objective))
        {
            return 1.0;
        }

        if (rerollsLeft == 0)
        {
            return 0.0;
        }

        // Optimization 1: encode histogram + rerollsLeft into a single long (3 bits per face, 3 bits for rerolls)
        var key = EncodeKey(histogram, rerollsLeft);

        if (memo.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var totalDice = 0;
        for (int f = 0; f < 6; f++) totalDice += histogram[f];

        var bestProb = 0.0;

        // Optimization 2: enumerate keep strategies as histogram choices (∏(cᵢ+1) vs 2ⁿ bitmasks)
        foreach (var keepHistogram in EnumerateKeepStrategies(histogram))
        {
            var keptCount = 0;
            for (int f = 0; f < 6; f++) keptCount += keepHistogram[f];
            var rerollCount = totalDice - keptCount;

            if (rerollCount == 0)
            {
                continue;
            }

            var prob = 0.0;
            var totalOutcomes = (long)Math.Pow(6, rerollCount);

            // Optimization 3: enumerate distinct reroll outcomes with multinomial weights
            foreach (var (rerollHistogram, multiplicity) in GenerateDistinctRolls(rerollCount))
            {
                var newHistogram = new int[6];
                for (int f = 0; f < 6; f++)
                    newHistogram[f] = keepHistogram[f] + rerollHistogram[f];

                prob += multiplicity * OptimalProbability(newHistogram, rerollsLeft - 1, objective, memo);
            }

            prob /= totalOutcomes;
            bestProb = Math.Max(bestProb, prob);
        }

        memo[key] = bestProb;
        return bestProb;
    }

    public double CalculateBestKeep(List<int> currentDice, int rerollsLeft, RollObjective objective, out List<bool> bestKeep)
    {
        return CalculateBestKeep(currentDice, rerollsLeft, objective, out bestKeep, null);
    }

    public double CalculateBestKeep(
        List<int> currentDice,
        int rerollsLeft,
        RollObjective objective,
        out List<bool> bestKeep,
        List<bool>? requiredKeep)
    {
        bestKeep = new List<bool>();
        var requiredKeepMask = NormalizeKeepMask(requiredKeep, currentDice.Count);
        var requiredFaceCounts = GetRequiredFaceCounts(currentDice, requiredKeepMask);

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

        var memo = new Dictionary<long, double>();
        var histogram = DiceToHistogram(currentDice);
        var bestProb = 0.0;
        int[]? bestKeepHistogram = null;

        foreach (var keepHistogram in EnumerateKeepStrategies(histogram))
        {
            if (!SatisfiesRequiredFaceCounts(keepHistogram, requiredFaceCounts))
            {
                continue;
            }

            var keptCount = 0;
            for (int f = 0; f < 6; f++) keptCount += keepHistogram[f];
            var rerollCount = currentDice.Count - keptCount;

            // Rerolling all dice is a valid strategy, but rerolling none is not.
            if (rerollCount == 0)
            {
                continue;
            }

            var prob = 0.0;
            var totalOutcomes = (long)Math.Pow(6, rerollCount);

            foreach (var (rerollHistogram, multiplicity) in GenerateDistinctRolls(rerollCount))
            {
                var newHistogram = new int[6];
                for (int f = 0; f < 6; f++)
                    newHistogram[f] = keepHistogram[f] + rerollHistogram[f];

                prob += multiplicity * OptimalProbability(newHistogram, rerollsLeft - 1, objective, memo);
            }

            prob /= totalOutcomes;

            if (prob > bestProb)
            {
                bestProb = prob;
                bestKeepHistogram = keepHistogram;
            }
        }

        bestKeep = bestKeepHistogram != null
            ? HistogramKeepToMask(currentDice, bestKeepHistogram, requiredKeepMask)
            : new List<bool>(requiredKeepMask);

        return bestProb;
    }

    /// <summary>
    /// Computes the probability of hitting <paramref name="objective"/> given that the caller has
    /// already committed to keeping the dice specified by <paramref name="forcedKeep"/> and will
    /// re-roll the remaining dice.  After that forced re-roll, optimal play continues for the
    /// remaining <c>rollsRemaining - 1</c> rerolls.
    /// </summary>
    public double CalculateWithForcedKeep(
        List<int> currentDice,
        int rollsRemaining,
        RollObjective objective,
        List<bool> forcedKeep)
    {
        var keptDice = currentDice
            .Zip(forcedKeep, (d, k) => (d, k))
            .Where(x => x.k)
            .Select(x => x.d)
            .ToList();

        var rerollCount = currentDice.Count - keptDice.Count;

        if (rerollCount == 0)
        {
            return _matcher.IsMatch(currentDice, objective) ? 1.0 : 0.0;
        }

        if (rollsRemaining == 0)
        {
            return _matcher.IsMatch(currentDice, objective) ? 1.0 : 0.0;
        }

        var memo = new Dictionary<long, double>();
        var keptHistogram = DiceToHistogram(keptDice);
        var totalProb = 0.0;
        var totalOutcomes = (long)Math.Pow(6, rerollCount);

        foreach (var (rerollHistogram, multiplicity) in GenerateDistinctRolls(rerollCount))
        {
            var newHistogram = new int[6];
            for (int f = 0; f < 6; f++)
                newHistogram[f] = keptHistogram[f] + rerollHistogram[f];

            totalProb += multiplicity * OptimalProbability(newHistogram, rollsRemaining - 1, objective, memo);
        }

        return totalProb / totalOutcomes;
    }

    // Convert a list of dice values to a per-face histogram (index = face - 1)
    private static int[] DiceToHistogram(List<int> dice)
    {
        var histogram = new int[6];
        foreach (var d in dice)
            histogram[d - 1]++;
        return histogram;
    }

    // Convert a per-face histogram to a sorted list of dice values
    private static List<int> HistogramToDice(int[] histogram)
    {
        var dice = new List<int>();
        for (int f = 0; f < 6; f++)
            for (int i = 0; i < histogram[f]; i++)
                dice.Add(f + 1);
        return dice;
    }

    // Encode a histogram and rerollsLeft into a long for use as a memo key.
    // Layout: 3 bits per face count (bits 0–17) followed by 3 bits for rerollsLeft (bits 18–20).
    // Constraints: each histogram[f] must be 0–7 (valid for ≤7 total dice) and rerollsLeft
    // must be 0–7. Keys are unique within those bounds, which covers all supported game inputs.
    private static long EncodeKey(int[] h, int rerollsLeft)
    {
        return (long)h[0]
             | ((long)h[1] << 3)
             | ((long)h[2] << 6)
             | ((long)h[3] << 9)
             | ((long)h[4] << 12)
             | ((long)h[5] << 15)
             | ((long)rerollsLeft << 18);
    }

    // Enumerate all keep strategies: for each face f, choose 0..histogram[f] dice to keep.
    // The number of strategies is ∏(histogram[f]+1), which is far smaller than 2^n for
    // dice with repeated values (e.g. [6,6,6,6,6] → 6 choices vs 32 bitmasks).
    private static IEnumerable<int[]> EnumerateKeepStrategies(int[] histogram)
    {
        return EnumerateKeepHelper(histogram, new int[6], 0);
    }

    private static IEnumerable<int[]> EnumerateKeepHelper(int[] histogram, int[] keep, int face)
    {
        if (face == 6)
        {
            yield return (int[])keep.Clone();
            yield break;
        }
        for (int k = 0; k <= histogram[face]; k++)
        {
            keep[face] = k;
            foreach (var result in EnumerateKeepHelper(histogram, keep, face + 1))
                yield return result;
        }
    }

    // Enumerate all distinct sorted outcomes of rolling rerollCount dice, each paired with
    // its multinomial coefficient (the number of ordered arrangements that produce it).
    // This yields C(rerollCount+5, 5) outcomes instead of 6^rerollCount ordered tuples.
    private static IEnumerable<(int[], long)> GenerateDistinctRolls(int rerollCount)
    {
        return GenerateDistinctRollsHelper(new int[6], 0, rerollCount);
    }

    private static IEnumerable<int[]> GenerateOrderedRolls(int diceCount)
    {
        return GenerateOrderedRollsHelper(new int[diceCount], 0);
    }

    private static IEnumerable<(int[], long)> GenerateDistinctRollsHelper(int[] counts, int face, int remaining)
    {
        // Recursion break case when die is a 6
        if (face == 5)
        {
            counts[5] = remaining;
            var n = counts[0] + counts[1] + counts[2] + counts[3] + counts[4] + remaining;
            var multiplicity = Multinomial(n, counts);
            yield return ((int[])counts.Clone(), multiplicity);
            yield break;
        }
        for (int c = 0; c <= remaining; c++)
        {
            counts[face] = c;
            foreach (var result in GenerateDistinctRollsHelper(counts, face + 1, remaining - c))
                yield return result;
        }
    }

    private static IEnumerable<int[]> GenerateOrderedRollsHelper(int[] values, int index)
    {
        if (index >= values.Length)
        {
            yield return (int[])values.Clone();
            yield break;
        }

        for (var face = 1; face <= 6; face++)
        {
            values[index] = face;
            foreach (var result in GenerateOrderedRollsHelper(values, index + 1))
            {
                yield return result;
            }
        }
    }

    // Compute the multinomial coefficient n! / (counts[0]! * counts[1]! * ... * counts[5]!)
    private static long Multinomial(int n, int[] counts)
    {
        long result = _factorials[n];
        foreach (int c in counts)
            result /= _factorials[c];
        return result;
    }

    private static List<int> BuildInitialDice(int totalDice, List<int> lockedIndexes, int[] lockedValues, int[] unlockedHistogram)
    {
        var dice = Enumerable.Repeat(0, totalDice).ToList();
        for (var i = 0; i < lockedIndexes.Count && i < lockedValues.Length; i++)
        {
            dice[lockedIndexes[i]] = lockedValues[i];
        }

        var unlockedValues = HistogramToDice(unlockedHistogram);
        var unlockedIndex = 0;
        for (var i = 0; i < dice.Count && unlockedIndex < unlockedValues.Count; i++)
        {
            if (lockedIndexes.Contains(i))
            {
                continue;
            }

            dice[i] = unlockedValues[unlockedIndex++];
        }

        return dice;
    }

    // Map a keep histogram (how many of each face to keep) back to per-position booleans
    // for the original dice list, marking the first keepHistogram[f] dice of each face as kept.
    private static List<bool> HistogramKeepToMask(List<int> dice, int[] keepHistogram, List<bool>? requiredKeepMask = null)
    {
        requiredKeepMask ??= Enumerable.Repeat(false, dice.Count).ToList();
        var keepCount = new int[6];
        var result = new List<bool>(dice.Count);

        for (int i = 0; i < dice.Count; i++)
        {
            var d = dice[i];
            int face = d - 1;

            if (requiredKeepMask[i])
            {
                result.Add(true);
                keepCount[face]++;
            }
            else if (keepCount[face] < keepHistogram[face])
            {
                result.Add(true);
                keepCount[face]++;
            }
            else
            {
                result.Add(false);
            }
        }
        return result;
    }

    private static List<bool> NormalizeKeepMask(List<bool>? keepMask, int diceCount)
    {
        if (keepMask == null)
        {
            return Enumerable.Repeat(false, diceCount).ToList();
        }

        if (keepMask.Count == diceCount)
        {
            return new List<bool>(keepMask);
        }

        var normalized = Enumerable.Repeat(false, diceCount).ToList();
        var copyCount = Math.Min(keepMask.Count, diceCount);
        for (int i = 0; i < copyCount; i++)
        {
            normalized[i] = keepMask[i];
        }
        return normalized;
    }

    private static int[] GetRequiredFaceCounts(List<int> dice, List<bool> requiredKeepMask)
    {
        var faceCounts = new int[6];
        for (int i = 0; i < dice.Count && i < requiredKeepMask.Count; i++)
        {
            if (requiredKeepMask[i])
            {
                faceCounts[dice[i] - 1]++;
            }
        }
        return faceCounts;
    }

    private static bool SatisfiesRequiredFaceCounts(int[] keepHistogram, int[] requiredFaceCounts)
    {
        for (int f = 0; f < 6; f++)
        {
            if (keepHistogram[f] < requiredFaceCounts[f])
            {
                return false;
            }
        }

        return true;
    }
}

using System.Collections.Concurrent;
using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class ProbabilityCalculator
{
    private readonly ObjectiveMatcher _matcher;

    // Cross-request caches. All probability computations are pure functions of their inputs,
    // so results are safe to share across concurrent requests.
    //
    // Cache sizes are naturally bounded by the game domain: there are a finite number of
    // hero objectives, a small range of dice counts (≤7), and at most a handful of reroll
    // depths — so unbounded growth is not a concern in practice.
    //
    // _globalMemo: caches the inner recursive DP state. The key encodes unlocked and locked
    //   histograms, rerolls, and the objective notation.
    //
    // _calculateCache: caches the final result of Calculate(objective, totalDice, rerolls) so
    //   that repeated fresh-turn lookups (e.g. hero selection screen) are instant.
    private readonly ConcurrentDictionary<(long, string), double> _globalMemo = new();
    private readonly ConcurrentDictionary<(string, int, int), double> _calculateCache = new();

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
        var cacheKey = (objective.Notation, totalDice, rerolls);
        return _calculateCache.GetOrAdd(cacheKey, _ =>
        {
            var totalProb = 0.0;
            var totalOutcomes = (long)Math.Pow(6, totalDice);

            // Optimization 3: enumerate only distinct initial rolls with multinomial weights
            foreach (var (histogram, multiplicity) in GenerateDistinctRolls(totalDice))
            {
                totalProb += multiplicity * OptimalProbability(histogram, rerolls, objective);
            }

            return totalProb / totalOutcomes;
        });
    }

    /// <summary>
    /// Calculates the probability of hitting <paramref name="objective"/> from a fresh turn before
    /// any dice are rolled, optionally forcing specific dice positions to remain locked after the
    /// opening roll. Unlike <see cref="Calculate"/>, this models hero rules such as Psylocke's
    /// manifest die by averaging over the initial locked-die outcomes and then applying optimal play.
    /// </summary>
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
    private double OptimalProbability(int[] histogram, int rerollsLeft, RollObjective objective)
    {
        return OptimalProbability(histogram, new int[6], rerollsLeft, objective);
    }

    /// <summary>
    /// Calculates the optimal probability while preserving dice that cannot be rerolled.
    /// The locked histogram is kept in every recursive state; only the unlocked histogram
    /// participates in future keep/reroll choices.
    /// </summary>
    private double OptimalProbability(
        int[] unlockedHistogram,
        int[] lockedHistogram,
        int rerollsLeft,
        RollObjective objective)
    {
        var dice = MergeHistograms(unlockedHistogram, lockedHistogram);

        if (_matcher.IsMatch(dice, objective))
        {
            return 1.0;
        }

        if (rerollsLeft == 0)
        {
            return 0.0;
        }

        // Optimization 1: encode both histograms and rerollsLeft into a single long memo key.
        var key = (EncodeKey(unlockedHistogram, lockedHistogram, rerollsLeft), objective.Notation);

        if (_globalMemo.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var totalUnlockedDice = unlockedHistogram.Sum();
        if (totalUnlockedDice == 0)
        {
            return 0.0;
        }

        var bestProb = 0.0;

        // Optimization 2: enumerate keep strategies as histogram choices (∏(cᵢ+1) vs 2ⁿ bitmasks)
        foreach (var keepHistogram in EnumerateKeepStrategies(unlockedHistogram))
        {
            var keptCount = 0;
            for (int f = 0; f < 6; f++) keptCount += keepHistogram[f];
            var rerollCount = totalUnlockedDice - keptCount;

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

                prob += multiplicity * OptimalProbability(newHistogram, lockedHistogram, rerollsLeft - 1, objective);
            }

            prob /= totalOutcomes;
            bestProb = Math.Max(bestProb, prob);
        }

        _globalMemo[key] = bestProb;
        return bestProb;
    }

    public double CalculateBestKeep(List<int> currentDice, int rerollsLeft, RollObjective objective, out List<bool> bestKeep)
    {
        return CalculateBestKeep(currentDice, rerollsLeft, objective, out bestKeep, null, null);
    }

    public double CalculateBestKeep(
        List<int> currentDice,
        int rerollsLeft,
        RollObjective objective,
        out List<bool> bestKeep,
        List<bool>? requiredKeep = null,
        List<RollObjective>? fallbacks = null)
    {
        bestKeep = new List<bool>();
        var requiredKeepMask = NormalizeKeepMask(requiredKeep, currentDice.Count);
        var lockedDice = currentDice
            .Where((_, index) => requiredKeepMask[index])
            .ToList();
        var unlockedDice = currentDice
            .Where((_, index) => !requiredKeepMask[index])
            .ToList();
        var lockedHistogram = DiceToHistogram(lockedDice);
        var unlockedHistogram = DiceToHistogram(unlockedDice);

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

        var bestProb = 0.0;
        int[]? bestKeepHistogram = null;

        foreach (var keepHistogram in EnumerateKeepStrategies(unlockedHistogram))
        {
            var keptCount = 0;
            for (int f = 0; f < 6; f++) keptCount += keepHistogram[f];
            var rerollCount = unlockedDice.Count - keptCount;

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

                prob += multiplicity * OptimalProbability(newHistogram, lockedHistogram, rerollsLeft - 1, objective);
            }

            prob /= totalOutcomes;

            if (prob > bestProb)
            {
                bestProb = prob;
                bestKeepHistogram = keepHistogram;
            }
            else if (prob == bestProb && fallbacks != null)
            {
                // Tiebreaker: Check the dice against the fallback objective, and prefer the keep that yields a higher fallback probability.
                var fallbackProb = 0.0;
                foreach (var fallback in fallbacks)
                {
                    fallbackProb = CalculateWithForcedKeep(
                        currentDice,
                        rerollsLeft,
                        fallback,
                        HistogramKeepToMask(currentDice, keepHistogram, requiredKeepMask),
                        requiredKeepMask);
                    if (fallbackProb > bestProb)
                    {
                        bestKeepHistogram = keepHistogram;
                    }
                }
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
        List<bool> forcedKeep,
        List<bool>? lockedDiceMask = null)
    {
        var normalizedForcedKeep = NormalizeKeepMask(forcedKeep, currentDice.Count);
        var normalizedLockedMask = NormalizeKeepMask(lockedDiceMask, currentDice.Count);
        for (int i = 0; i < currentDice.Count; i++)
        {
            normalizedForcedKeep[i] |= normalizedLockedMask[i];
        }

        var lockedDice = currentDice
            .Where((_, index) => normalizedLockedMask[index])
            .ToList();
        var keptUnlockedDice = currentDice
            .Where((_, index) => normalizedForcedKeep[index] && !normalizedLockedMask[index])
            .ToList();

        var rerollCount = currentDice.Count - normalizedForcedKeep.Count(keep => keep);

        if (rerollCount == 0)
        {
            return _matcher.IsMatch(currentDice, objective) ? 1.0 : 0.0;
        }

        if (rollsRemaining == 0)
        {
            return _matcher.IsMatch(currentDice, objective) ? 1.0 : 0.0;
        }

        var keptHistogram = DiceToHistogram(keptUnlockedDice);
        var lockedHistogram = DiceToHistogram(lockedDice);
        var totalProb = 0.0;
        var totalOutcomes = (long)Math.Pow(6, rerollCount);

        foreach (var (rerollHistogram, multiplicity) in GenerateDistinctRolls(rerollCount))
        {
            var newHistogram = new int[6];
            for (int f = 0; f < 6; f++)
                newHistogram[f] = keptHistogram[f] + rerollHistogram[f];

            totalProb += multiplicity * OptimalProbability(newHistogram, lockedHistogram, rollsRemaining - 1, objective);
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

    // Encode unlocked and locked histograms plus rerollsLeft into a long for use as a memo key.
    // Layout: 3 bits per face for unlocked (bits 0–17), locked (bits 18–35), then rerolls
    // (bits 36–38). Each count and rerollsLeft is 0–7 for supported game inputs.
    private static long EncodeKey(int[] unlocked, int[] locked, int rerollsLeft)
    {
        var key = 0L;
        for (int f = 0; f < 6; f++)
        {
            key |= (long)unlocked[f] << (f * 3);
            key |= (long)locked[f] << (18 + f * 3);
        }

        return key | ((long)rerollsLeft << 36);
    }

    private static List<int> MergeHistograms(int[] first, int[] second)
    {
        var dice = new List<int>();
        for (int f = 0; f < 6; f++)
        {
            for (int i = 0; i < first[f] + second[f]; i++)
            {
                dice.Add(f + 1);
            }
        }

        return dice;
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

    /// <summary>
    /// Generates all ordered roll combinations for <paramref name="diceCount"/> dice (6^n outcomes).
    /// This differs from <see cref="GenerateDistinctRolls(int)"/>, which collapses permutations into
    /// histogram counts paired with multiplicities.
    /// </summary>
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

    /// <summary>
    /// Builds an initial dice list by placing the ordered locked values at their fixed indexes and
    /// filling the remaining positions from the unlocked histogram values.
    /// </summary>
    private static List<int> BuildInitialDice(int totalDice, List<int> lockedIndexes, int[] lockedValues, int[] unlockedHistogram)
    {
        if (lockedIndexes.Count != lockedValues.Length)
        {
            throw new ArgumentException("Locked index count must match locked value count.", nameof(lockedValues));
        }

        var dice = Enumerable.Repeat(0, totalDice).ToList();
        for (var i = 0; i < lockedIndexes.Count; i++)
        {
            dice[lockedIndexes[i]] = lockedValues[i];
        }

        var unlockedValues = HistogramToDice(unlockedHistogram);
        if (unlockedValues.Count != totalDice - lockedIndexes.Count)
        {
            throw new ArgumentException("Unlocked histogram does not match the expected dice count.", nameof(unlockedHistogram));
        }

        var lockedIndexSet = lockedIndexes.ToHashSet();
        var unlockedIndex = 0;
        for (var i = 0; i < dice.Count; i++)
        {
            if (lockedIndexSet.Contains(i))
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
        var unlockedKeepCount = new int[6];
        var result = new List<bool>(dice.Count);

        for (int i = 0; i < dice.Count; i++)
        {
            var d = dice[i];
            int face = d - 1;

            if (requiredKeepMask[i])
            {
                result.Add(true);
            }
            else if (unlockedKeepCount[face] < keepHistogram[face])
            {
                result.Add(true);
                unlockedKeepCount[face]++;
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

}

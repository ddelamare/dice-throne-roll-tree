using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiceThroneApi.Tests;

/// <summary>
/// Investigation: why does the advisor suggest keeping only [5, 6] (and not [1, 3, 5, 6])
/// when rolling [1, 1, 3, 5, 6] on the first roll while targeting Spider Reflexes?
///
/// Spider Reflexes notation: [(123)(45)(45)6]
///   Group 1: one die showing 1, 2, or 3
///   Group 2: one die showing 4 or 5
///   Group 3: one die showing 4 or 5
///   Group 4: one die showing exactly 6
///
/// Intuition says: "keep the 1 and 3 because they satisfy group 1, they are needed for the
/// objective".  The algorithm, however, recommends keeping only [5, 6].
///
/// The tests below confirm this is mathematically OPTIMAL, not a bug.
/// The explanation: with two rerolls remaining, keeping fewer dice leaves more dice in the
/// reroll pool, which gives additional flexibility to simultaneously satisfy the missing
/// groups.  In particular:
///
///   Keep [5, 6]    → reroll 3 dice → P ≈ 84.6 %   (best)
///   Keep [1, 5, 6] → reroll 2 dice → P ≈ 81.5 %
///   Keep [1,3,5,6] → reroll 1 die  → P ≈ 72.2 %
///
/// With only 1 die to reroll you need it to land on {4,5} both times – a narrow window.
/// With 3 dice to reroll you have a much better chance of covering both missing groups
/// ({1,2,3} and {4,5}) across the two reroll opportunities.
/// </summary>
public class SpiderReflexesInvestigationTests
{
    private readonly ITestOutputHelper _output;
    private readonly ProbabilityCalculator _calculator;
    private readonly DiceRollAdvisor _advisor;
    private readonly DiceNotationParser _parser;
    private readonly RollObjective _spiderReflexes;

    // Dice rolled on the first roll
    private static readonly List<int> Dice11356 = new() { 1, 1, 3, 5, 6 };

    // With 2 rerolls remaining (standard Dice Throne turn: initial roll + 2 rerolls)
    private const int TwoRerolls = 2;

    public SpiderReflexesInvestigationTests(ITestOutputHelper output)
    {
        _output = output;
        var matcher = new ObjectiveMatcher();
        _calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        _advisor = new DiceRollAdvisor(_calculator, simulator);
        _parser = new DiceNotationParser();
        _spiderReflexes = _parser.Parse("Spider Reflexes", "[(123)(45)(45)6]");
        _spiderReflexes.Damage = 7;
    }

    /// <summary>
    /// Confirm the advisor returns a suggestion that keeps only indices for the 5 and 6,
    /// i.e. DiceToKeep = [false, false, false, true, true] for dice [1,1,3,5,6].
    /// </summary>
    [Fact]
    public void Advisor_SpiderReflexes_Dice11356_SuggestsKeepOnly5And6()
    {
        var advice = _advisor.GetAdvice(Dice11356, TwoRerolls, new List<RollObjective> { _spiderReflexes });

        Assert.Single(advice);
        var result = advice[0];

        _output.WriteLine($"Suggested keep: [{string.Join(", ", result.DiceToKeep)}]");
        _output.WriteLine($"Probability   : {result.Probability:P2}");

        // The dice are [1,1,3,5,6] at positions 0-4.
        // Optimal strategy keeps only the 5 (index 3) and the 6 (index 4).
        Assert.Equal(new List<bool> { false, false, false, true, true }, result.DiceToKeep);
    }

    /// <summary>
    /// Keeping [5, 6] (P ≈ 84.6 %) is strictly better than keeping [1, 5, 6] (P ≈ 81.5 %).
    /// Although the 1 contributes to group (123), locking it in reduces the reroll pool from
    /// 3 dice to 2, which significantly lowers the chance of simultaneously satisfying the
    /// missing (123) and second (45) groups.
    /// </summary>
    [Fact]
    public void ProbabilityComparison_Keep56_IsBetterThan_Keep156()
    {
        // Keep [5, 6]: dice at indices 3 and 4
        var keep56 = new List<bool> { false, false, false, true, true };
        // Keep [1, 5, 6]: dice at indices 0, 3, and 4
        var keep156 = new List<bool> { true, false, false, true, true };

        var probKeep56 = _calculator.CalculateWithForcedKeep(Dice11356, TwoRerolls, _spiderReflexes, keep56);
        var probKeep156 = _calculator.CalculateWithForcedKeep(Dice11356, TwoRerolls, _spiderReflexes, keep156);

        _output.WriteLine($"P(keep [5,6])   = {probKeep56:F6}  (reroll 3 dice)");
        _output.WriteLine($"P(keep [1,5,6]) = {probKeep156:F6}  (reroll 2 dice)");

        Assert.True(probKeep56 > probKeep156,
            $"Expected P(keep [5,6]) {probKeep56:F6} > P(keep [1,5,6]) {probKeep156:F6}");

        // Spot-check the known exact values
        Assert.InRange(probKeep56, 0.845, 0.848);
        Assert.InRange(probKeep156, 0.813, 0.816);
    }

    /// <summary>
    /// Keeping [1, 3, 5, 6] (P ≈ 72.2 %) is the worst of the three strategies even though
    /// it already satisfies three of the four groups – because it leaves only one die to
    /// reroll, which must land on {4,5} in each attempt.
    /// </summary>
    [Fact]
    public void ProbabilityComparison_Keep56_IsBetterThan_Keep1356()
    {
        // Keep [5, 6]: dice at indices 3 and 4
        var keep56 = new List<bool> { false, false, false, true, true };
        // Keep [1, 3, 5, 6]: dice at indices 0, 2, 3, and 4
        var keep1356 = new List<bool> { true, false, true, true, true };

        var probKeep56 = _calculator.CalculateWithForcedKeep(Dice11356, TwoRerolls, _spiderReflexes, keep56);
        var probKeep1356 = _calculator.CalculateWithForcedKeep(Dice11356, TwoRerolls, _spiderReflexes, keep1356);

        _output.WriteLine($"P(keep [5,6])     = {probKeep56:F6}  (reroll 3 dice)");
        _output.WriteLine($"P(keep [1,3,5,6]) = {probKeep1356:F6}  (reroll 1 die)");

        Assert.True(probKeep56 > probKeep1356,
            $"Expected P(keep [5,6]) {probKeep56:F6} > P(keep [1,3,5,6]) {probKeep1356:F6}");

        // Spot-check the known exact values
        Assert.InRange(probKeep56, 0.845, 0.848);
        Assert.InRange(probKeep1356, 0.721, 0.724);
    }

    /// <summary>
    /// Full ranking of all meaningful keep strategies to document which is optimal
    /// and confirm the ordering is: [5,6] > [1,5,6] ≈ [3,5,6] > [1,3,5,6] ≈ [1,1,5,6].
    /// </summary>
    [Fact]
    public void ProbabilityRanking_AllMeaningfulStrategies_Keep56IsHighest()
    {
        var strategies = new Dictionary<string, List<bool>>
        {
            ["Keep [5,6] (reroll 3)"]     = new() { false, false, false, true,  true  },
            ["Keep [1,5,6] (reroll 2)"]   = new() { true,  false, false, true,  true  },
            ["Keep [3,5,6] (reroll 2)"]   = new() { false, false, true,  true,  true  },
            ["Keep [1,3,5,6] (reroll 1)"] = new() { true,  false, true,  true,  true  },
            ["Keep [1,1,5,6] (reroll 1)"] = new() { true,  true,  false, true,  true  },
        };

        var results = strategies.ToDictionary(
            kvp => kvp.Key,
            kvp => _calculator.CalculateWithForcedKeep(Dice11356, TwoRerolls, _spiderReflexes, kvp.Value));

        _output.WriteLine("Strategy probabilities for Spider Reflexes with dice [1,1,3,5,6] (2 rerolls):");
        foreach (var (name, prob) in results.OrderByDescending(x => x.Value))
        {
            _output.WriteLine($"  {name}: {prob:P2}");
        }

        // [5,6] is strictly the best
        var probKeep56 = results["Keep [5,6] (reroll 3)"];
        foreach (var (name, prob) in results.Where(x => x.Key != "Keep [5,6] (reroll 3)"))
        {
            Assert.True(probKeep56 > prob,
                $"Expected P(keep [5,6]) {probKeep56:F6} > P({name}) {prob:F6}");
        }

        // [1,5,6] and [3,5,6] tie (both reroll a different pair that excludes a 1-or-3)
        Assert.Equal(results["Keep [1,5,6] (reroll 2)"], results["Keep [3,5,6] (reroll 2)"], precision: 10);

        // [1,3,5,6] and [1,1,5,6] tie (both keep one extra die that doesn't change the missing need)
        Assert.Equal(results["Keep [1,3,5,6] (reroll 1)"], results["Keep [1,1,5,6] (reroll 1)"], precision: 10);
    }

    /// <summary>
    /// Confirm that CalculateBestKeep independently arrives at the [5,6] keep histogram
    /// (i.e. zero 1s, zero 3s, one 5, one 6) as its optimal strategy.
    /// </summary>
    [Fact]
    public void CalculateBestKeep_Dice11356_ReturnsKeep56AsBestHistogram()
    {
        var prob = _calculator.CalculateBestKeep(Dice11356, TwoRerolls, _spiderReflexes, out var bestKeep);

        _output.WriteLine($"Best keep mask: [{string.Join(", ", bestKeep)}]");
        _output.WriteLine($"Probability   : {prob:P2}");

        // The keep mask for dice [1,1,3,5,6]: only indices 3 (=5) and 4 (=6) should be true
        Assert.Equal(5, bestKeep.Count);
        Assert.Equal(new List<bool> { false, false, false, true, true }, bestKeep);
        Assert.InRange(prob, 0.845, 0.848);
    }

    /// <summary>
    /// Root-cause summary: even with just 1 reroll remaining, keeping [5,6] and rolling
    /// 3 dice (P ≈ 58.3 %) still outperforms keeping [1,5,6] and rolling 2 dice (P ≈ 55.6 %).
    ///
    /// Why? Rolling 3 dice covers BOTH missing groups ({1,2,3} and {4,5}) simultaneously:
    ///   P(≥1 of {1,2,3} AND ≥1 of {4,5} from 3 dice) = 1 – (3/6)³ – (4/6)³ + (1/6)³ ≈ 58.3 %
    /// Rolling 2 dice when {1,2,3} is already covered needs only one {4,5}:
    ///   P(≥1 of {4,5} from 2 dice) = 1 – (4/6)² ≈ 55.6 %
    ///
    /// The reroll-pool advantage of [5,6] is maintained regardless of rerolls remaining.
    /// </summary>
    [Fact]
    public void WithOneRerollRemaining_Keep56_IsStillBetterThan_Keep156()
    {
        const int oneReroll = 1;

        var keep56 = new List<bool> { false, false, false, true, true };
        var keep156 = new List<bool> { true, false, false, true, true };

        var probKeep56 = _calculator.CalculateWithForcedKeep(Dice11356, oneReroll, _spiderReflexes, keep56);
        var probKeep156 = _calculator.CalculateWithForcedKeep(Dice11356, oneReroll, _spiderReflexes, keep156);

        _output.WriteLine($"With 1 reroll remaining:");
        _output.WriteLine($"  P(keep [5,6])   = {probKeep56:F6}  (reroll 3 dice)");
        _output.WriteLine($"  P(keep [1,5,6]) = {probKeep156:F6}  (reroll 2 dice)");

        // Even with a single reroll, keeping [5,6] and rolling 3 dice is better
        Assert.True(probKeep56 > probKeep156,
            $"Expected P(keep [5,6]) {probKeep56:F6} > P(keep [1,5,6]) {probKeep156:F6} when 1 reroll remains");

        // Spot-check the known exact values
        Assert.InRange(probKeep56, 0.582, 0.585);    // ≈ 126/216
        Assert.InRange(probKeep156, 0.554, 0.557);   // ≈ 20/36
    }
}

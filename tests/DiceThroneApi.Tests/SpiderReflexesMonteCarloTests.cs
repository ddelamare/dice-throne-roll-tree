using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Xunit;
using Xunit.Abstractions;

namespace DiceThroneApi.Tests;

/// <summary>
/// Monte Carlo verification for every keep strategy analysed in
/// <see cref="SpiderReflexesInvestigationTests"/>.
///
/// Each scenario runs 50,000 simulated games (using optimal play for subsequent rerolls,
/// matching the behaviour of <see cref="ProbabilityCalculator.CalculateWithForcedKeep"/>)
/// and compares the observed frequency against the exact analytic probability.
///
/// A tolerance of ±0.02 is used; with 50,000 iterations the standard deviation of the
/// estimate is at most ~0.0022, so this threshold corresponds to roughly a 9-sigma band —
/// the test will essentially never fail due to sampling noise alone.
/// </summary>
public class SpiderReflexesMonteCarloTests
{
    // Dice rolled on the first roll
    private static readonly List<int> Dice11356 = new() { 1, 1, 3, 5, 6 };

    private const int Iterations = 50_000;
    private const double Tolerance = 0.02;

    private readonly ITestOutputHelper _output;
    private readonly ProbabilityCalculator _calculator;
    private readonly MonteCarloSimulator _simulator;
    private readonly RollObjective _spiderReflexes;

    public SpiderReflexesMonteCarloTests(ITestOutputHelper output)
    {
        _output = output;
        var matcher = new ObjectiveMatcher();
        _calculator = new ProbabilityCalculator(matcher);
        // Use optimal strategy so subsequent rerolls mirror CalculateWithForcedKeep behaviour.
        _simulator = new MonteCarloSimulator(matcher, _calculator, useOptimalStrategy: true);
        _spiderReflexes = new DiceNotationParser().Parse("Spider Reflexes", "[(123)(45)(45)6]");
        _spiderReflexes.Damage = 7;
    }

    // ── Scenarios ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies every keep strategy listed in the investigation against Monte Carlo
    /// simulation and prints a comparison table to the test output.
    ///
    /// Scenarios with 2 rerolls remaining:
    ///   Keep [5,6]     → reroll 3 dice  (optimal — analytic ≈ 84.6 %)
    ///   Keep [1,5,6]   → reroll 2 dice               (analytic ≈ 81.5 %)
    ///   Keep [3,5,6]   → reroll 2 dice               (analytic ≈ 81.5 %)
    ///   Keep [1,3,5,6] → reroll 1 die                (analytic ≈ 72.2 %)
    ///   Keep [1,1,5,6] → reroll 1 die                (analytic ≈ 72.2 %)
    ///
    /// Scenarios with 1 reroll remaining:
    ///   Keep [5,6]     → reroll 3 dice  (still best  — analytic ≈ 58.3 %)
    ///   Keep [1,5,6]   → reroll 2 dice               (analytic ≈ 55.6 %)
    /// </summary>
    [Fact]
    public void MonteCarloVerification_AllStrategies_MatchAnalyticProbabilities()
    {
        var scenarios = new[]
        {
            new { Label = "Keep [5,6]     (reroll 3, 2 rerolls)",
                  Keep = new List<bool> { false, false, false, true,  true  }, Rerolls = 2 },
            new { Label = "Keep [1,5,6]   (reroll 2, 2 rerolls)",
                  Keep = new List<bool> { true,  false, false, true,  true  }, Rerolls = 2 },
            new { Label = "Keep [3,5,6]   (reroll 2, 2 rerolls)",
                  Keep = new List<bool> { false, false, true,  true,  true  }, Rerolls = 2 },
            new { Label = "Keep [1,3,5,6] (reroll 1, 2 rerolls)",
                  Keep = new List<bool> { true,  false, true,  true,  true  }, Rerolls = 2 },
            new { Label = "Keep [1,1,5,6] (reroll 1, 2 rerolls)",
                  Keep = new List<bool> { true,  true,  false, true,  true  }, Rerolls = 2 },
            new { Label = "Keep [5,6]     (reroll 3, 1 reroll) ",
                  Keep = new List<bool> { false, false, false, true,  true  }, Rerolls = 1 },
            new { Label = "Keep [1,5,6]   (reroll 2, 1 reroll) ",
                  Keep = new List<bool> { true,  false, false, true,  true  }, Rerolls = 1 },
        };

        const string Sep = "------------------------------------------------------------------------";

        _output.WriteLine(Sep);
        _output.WriteLine("Spider Reflexes [(123)(45)(45)6]  —  dice [1, 1, 3, 5, 6]");
        _output.WriteLine($"Monte Carlo: {Iterations:N0} iterations per scenario  |  tolerance ±{Tolerance:P0}");
        _output.WriteLine(Sep);
        _output.WriteLine($"{"Strategy",-44}  {"Analytic",9}  {"MC",9}  {"Delta",9}");
        _output.WriteLine(Sep);

        var failures = new List<string>();

        foreach (var s in scenarios)
        {
            var analytic = _calculator.CalculateWithForcedKeep(
                Dice11356, s.Rerolls, _spiderReflexes, s.Keep);

            var mc = _simulator.SimulateWithForcedKeep(
                Dice11356, s.Rerolls, _spiderReflexes, s.Keep, Iterations);

            var delta = mc - analytic;
            _output.WriteLine(
                $"{s.Label,-44}  {analytic,9:P2}  {mc,9:P2}  {delta,10:+0.0000;-0.0000}");

            if (Math.Abs(delta) > Tolerance)
                failures.Add($"{s.Label}: analytic={analytic:F6}, mc={mc:F6}, |delta|={Math.Abs(delta):F6} > {Tolerance}");
        }

        _output.WriteLine(Sep);

        Assert.True(failures.Count == 0,
            "Monte Carlo results deviated from analytic values beyond tolerance:\n  " +
            string.Join("\n  ", failures));
    }

    // ── Individual scenario assertions ───────────────────────────────────────
    // These give xUnit a separate pass/fail entry for each keep strategy,
    // making it easy to spot which one diverges if a regression is introduced.

    [Theory]
    [InlineData("Keep [5,6]  — 2 rerolls", false, false, false, true,  true,  2, 0.845, 0.848)]
    [InlineData("Keep [1,5,6] — 2 rerolls", true,  false, false, true,  true,  2, 0.813, 0.816)]
    [InlineData("Keep [3,5,6] — 2 rerolls", false, false, true,  true,  true,  2, 0.813, 0.816)]
    [InlineData("Keep [1,3,5,6] — 2 rerolls", true,  false, true,  true,  true,  2, 0.721, 0.724)]
    [InlineData("Keep [1,1,5,6] — 2 rerolls", true,  true,  false, true,  true,  2, 0.721, 0.724)]
    [InlineData("Keep [5,6]  — 1 reroll",  false, false, false, true,  true,  1, 0.582, 0.585)]
    [InlineData("Keep [1,5,6] — 1 reroll",  true,  false, false, true,  true,  1, 0.554, 0.557)]
    public void MonteCarlo_PerScenario_WithinToleranceOfAnalytic(
        string label,
        bool k0, bool k1, bool k2, bool k3, bool k4,
        int rerolls,
        double expectedLo, double expectedHi)
    {
        var keep = new List<bool> { k0, k1, k2, k3, k4 };
        var analytic = _calculator.CalculateWithForcedKeep(Dice11356, rerolls, _spiderReflexes, keep);
        var mc       = _simulator.SimulateWithForcedKeep(Dice11356, rerolls, _spiderReflexes, keep, Iterations);

        _output.WriteLine($"{label}");
        _output.WriteLine($"  Analytic : {analytic:P4}");
        _output.WriteLine($"  MC       : {mc:P4}");
        _output.WriteLine($"  Delta    : {mc - analytic:+0.0000;-0.0000}");

        // The analytic value must fall in the documented range (spot-check the exact formula).
        Assert.InRange(analytic, expectedLo, expectedHi);

        // The MC estimate must be within ±Tolerance of the exact value.
        Assert.InRange(mc, analytic - Tolerance, analytic + Tolerance);
    }
}

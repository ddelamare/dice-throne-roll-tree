using System.Linq;
using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace DiceThroneApi.Tests;

public class DiceRollAdvisorTests
{
    private readonly DiceRollAdvisor _advisor;
    private readonly ProbabilityCalculator _calculator;
    private readonly DiceNotationParser _parser;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly ObjectiveMatcher _matcher = new ObjectiveMatcher();

    public DiceRollAdvisorTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        var matcher = new ObjectiveMatcher();
        _matcher = matcher;
        _calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        _advisor = new DiceRollAdvisor(_calculator, simulator);
        _parser = new DiceNotationParser();
    }

    private IWebHostEnvironment CreateTestEnvironment()
    {
        var contentRootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "DiceThroneApi"));
        return new FakeWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            EnvironmentName = "Development",
            ApplicationName = "DiceThroneApi",
            WebRootPath = Path.Combine(contentRootPath, "wwwroot")
        };
    }

    [Fact]
    public void GetAdvice_WithDamage_PopulatesExpectedDelta()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 5;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        var result = advice[0];
        Assert.Equal(5, result.Damage);
        Assert.InRange(result.ExpectedDelta, 0.0, 5.0);
        Assert.Equal(result.Probability * 5, result.ExpectedDelta, precision: 10);
    }

    [Fact]
    public void GetAdvice_WithZeroDamage_ExpectedDeltaIsZero()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 0;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        Assert.Equal(0, advice[0].ExpectedDelta);
    }

    [Fact]
    public void GetAdvice_MatchingDice_ExpectedDeltaEqualsFullDamage()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 4;

        // All four sixes kept, one reroll remaining — probability is very high
        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 1, new List<RollObjective> { objective });

        Assert.Single(advice);
        var result = advice[0];
        // With 4 sixes already, expected delta should be close to the full 4 damage
        Assert.InRange(result.ExpectedDelta, 3.0, 4.0);
    }

    [Fact]
    public void GetAdvice_ImpossibleObjective_ExpectedDeltaIsZero()
    {
        // 6 sixes with only 5 dice — impossible
        var objective = _parser.Parse("Test", "[666666]");
        objective.Damage = 10;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        Assert.Equal(0.0, advice[0].ExpectedDelta);
    }

    [Fact]
    public void GetAdvice_MonteCarlo_PopulatesExpectedDelta()
    {
        var objective = _parser.Parse("Test", "[6]");
        objective.Damage = 3;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective }, method: "montecarlo");

        Assert.Single(advice);
        var result = advice[0];
        Assert.Equal(3, result.Damage);
        Assert.Equal(result.Probability * 3, result.ExpectedDelta, precision: 10);
        Assert.InRange(result.ExpectedDelta, 0.0, 3.0);
    }

    [Fact]
    public void GetAdvice_MonteCarlo_AlreadySatisfied_Returns100Percent()
    {
        // If current dice already satisfy the objective, Monte Carlo should return 1.0 immediately
        // without running the simulation.
        var objective = _parser.Parse("Test", "[66]");
        objective.Damage = 4;

        var dice = new List<int> { 6, 6, 1, 2, 3 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective }, method: "montecarlo");

        Assert.Single(advice);
        Assert.Equal(1.0, advice[0].Probability);
    }

    // ── Fallback / chase tests ────────────────────────────────────────────────

    [Fact]
    public void GetAdvice_WithMultipleDamageObjectives_PopulatesFallback()
    {
        // Chase: [66666] (hard, 8 dmg) — fallback should be [6666] (easier, 3 dmg)
        var chaseObj = _parser.Parse("Ultimate Attack", "[66666]");
        chaseObj.Damage = 8;
        var fallbackObj = _parser.Parse("Barbaric Roar", "[6666]");
        fallbackObj.Damage = 3;

        var dice = new List<int> { 6, 6, 6, 1, 2 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { chaseObj, fallbackObj });

        var chaseAdvice = advice.First(a => a.ObjectiveName == "Ultimate Attack");
        // Chase has a fallback because it's hard and another damage objective exists
        Assert.NotNull(chaseAdvice.FallbackObjectiveName);
        Assert.True(chaseAdvice.FallbackExpectedDelta > 0);
    }

    [Fact]
    public void GetAdvice_NoFallbackWhenSingleObjective_FallbackIsNull()
    {
        var obj = _parser.Parse("Solo", "[6666]");
        obj.Damage = 4;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { obj });

        Assert.Single(advice);
        Assert.Null(advice[0].FallbackObjectiveName);
    }

    [Fact]
    public void GetAdvice_NoFallbackWhenZeroRollsRemaining()
    {
        var chaseObj = _parser.Parse("Chase", "[66666]");
        chaseObj.Damage = 8;
        var fallbackObj = _parser.Parse("Fallback", "[6666]");
        fallbackObj.Damage = 3;

        var dice = new List<int> { 6, 6, 6, 1, 2 };
        // 0 rolls remaining — no rerolling, so no fallback calculation
        var advice = _advisor.GetAdvice(dice, 0, new List<RollObjective> { chaseObj, fallbackObj });

        Assert.All(advice, a => Assert.Null(a.FallbackObjectiveName));
    }

    [Fact]
    public void CalculateWithForcedKeep_AllKept_ReturnsMatchResult()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var keepAll = new List<bool> { true, true, true, true, true };

        // All dice kept, no rerolling — result is just whether current dice match
        var prob = _calculator.CalculateWithForcedKeep(dice, 2, objective, keepAll);

        // [6,6,6,6,1] already matches [6666], so probability = 1.0
        Assert.Equal(1.0, prob);
    }

    [Fact]
    public void CalculateWithForcedKeep_ForcedKeepOfSixes_FallbackProbabilityIsReasonable()
    {
        // Keep 3 sixes towards [6666]; probability of hitting [6666] with 2 rerolls of remaining 2 dice
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 6, 6, 6, 1, 2 };
        var keep3Sixes = new List<bool> { true, true, true, false, false };

        var prob = _calculator.CalculateWithForcedKeep(dice, 2, objective, keep3Sixes);

        // Need one more 6 from 2 dice with 2 rerolls — should be a moderate probability
        Assert.InRange(prob, 0.3, 1.0);
    }

    [Fact]
    public void CalculateBestKeep_AllowsRerollingAllDice_WhenThatIsOptimal()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 1, 2, 3, 4, 5 };

        _calculator.CalculateBestKeep(dice, 2, objective, out var toKeep);

        Assert.Equal(new List<bool> { false, false, false, false, false }, toKeep);
    }

    // ── New Improvement Tests ────────────────────────────────────────────────

    [Fact]
    public void GetAdvice_MonteCarloUsesOptimalKeepSuggestions()
    {
        // Test that Monte Carlo now uses optimal keep strategy instead of greedy
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 4;

        var dice = new List<int> { 6, 6, 1, 2, 3 };
        var analyticAdvice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective }, method: "analytic");
        var monteCarloAdvice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective }, method: "montecarlo");

        // Both should suggest the same dice to keep (optimal strategy)
        Assert.Equal(analyticAdvice[0].DiceToKeep, monteCarloAdvice[0].DiceToKeep);
    }

    [Fact]
    public void GetAdvice_PopulatesBaselineProbability()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 4;

        var dice = new List<int> { 6, 6, 6, 1, 2 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        // Baseline probability should be > 0 (probability if rerolling all dice)
        Assert.True(advice[0].BaselineProbability >= 0);
        // Probability improvement should be positive when keeping 3 sixes vs rerolling all
        Assert.True(advice[0].ProbabilityImprovement >= 0);
    }

    [Fact]
    public void GetAdvice_ProbabilityImprovementEqualsProb_MinusBaseline()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 4;

        var dice = new List<int> { 6, 6, 6, 1, 2 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        // ProbabilityImprovement should equal Probability - BaselineProbability
        var expectedImprovement = advice[0].Probability - advice[0].BaselineProbability;
        Assert.Equal(expectedImprovement, advice[0].ProbabilityImprovement, precision: 10);
    }

    [Fact]
    public void GetBestOverallStrategy_ReturnsHighestExpectedDelta()
    {
        // High damage but hard objective
        var hardObj = _parser.Parse("Hard Attack", "[66666]");
        hardObj.Damage = 10;

        // Lower damage but easier objective
        var easyObj = _parser.Parse("Easy Attack", "[66]");
        easyObj.Damage = 2;

        var dice = new List<int> { 6, 6, 1, 2, 3 };
        var objectives = new List<RollObjective> { hardObj, easyObj };

        var best = _advisor.GetBestOverallStrategy(dice, 2, objectives);

        Assert.NotNull(best);
        // With 2 sixes already, the easy [66] objective has 100% probability
        // Easy: 1.0 * 2 = 2.0 expected damage
        // Hard: low probability * 10 < 2.0 expected damage
        Assert.Equal("Easy Attack", best.ObjectiveName);
    }

    [Fact]
    public void GetAdvice_OrdersByExpectedDelta_BeforeProbability()
    {
        var highProbabilityLowDamage = _parser.Parse("Safe Attack", "[66]");
        highProbabilityLowDamage.Damage = 2;

        var lowerProbabilityHighDamage = _parser.Parse("Big Swing", "[66666]");
        lowerProbabilityHighDamage.Damage = 40;

        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 1, new List<RollObjective> { highProbabilityLowDamage, lowerProbabilityHighDamage });

        Assert.Equal("Big Swing", advice[0].ObjectiveName);
        Assert.True(advice[0].ExpectedDelta > advice[1].ExpectedDelta);
        Assert.True(advice[0].Probability < advice[1].Probability);
    }

    [Fact]
    public void GetBestOverallStrategy_ReturnsNull_WhenNoDamageObjectives()
    {
        var obj = _parser.Parse("No Damage", "[6666]");
        obj.Damage = 0;

        var dice = new List<int> { 6, 6, 1, 2, 3 };
        var objectives = new List<RollObjective> { obj };

        var best = _advisor.GetBestOverallStrategy(dice, 2, objectives);

        Assert.Null(best);
    }

    [Fact]
    public void GetBestOverallStrategy_IncludesProbabilityDelta()
    {
        var obj = _parser.Parse("Attack", "[6666]");
        obj.Damage = 4;

        var dice = new List<int> { 6, 6, 6, 1, 2 };
        var objectives = new List<RollObjective> { obj };

        var best = _advisor.GetBestOverallStrategy(dice, 2, objectives);

        Assert.NotNull(best);
        Assert.True(best.BaselineProbability >= 0);
        Assert.True(best.ProbabilityImprovement >= 0);
    }

    // ── ExpectedDelta / EvaluationConfig tests ────────────────────────────────

    [Fact]
    public void GetAdvice_ExpectedDelta_IncludesTokensWithDefaultValue()
    {
        // Objective with 2 tokens — default token value = 2 each, so total delta = 5 damage + 2+2 = 9
        var objective = _parser.Parse("Crit Bash", "[6666]");
        objective.Damage = 5;
        objective.Tokens = new List<string> { "Stun", "Stun" };

        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 0, new List<RollObjective> { objective });

        var result = advice[0];
        // prob * (5 + 2*2) = 1.0 * 9 = 9
        Assert.Equal(result.Probability * 9, result.ExpectedDelta, precision: 10);
    }

    [Fact]
    public void GetAdvice_ExpectedDelta_IncludesHealAndCards()
    {
        var objective = _parser.Parse("The Cure", "[(45)(45)(45)6]");
        objective.Damage = 0;
        objective.Heal = 3;
        objective.Cards = 1;

        var dice = new List<int> { 4, 4, 4, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 0, new List<RollObjective> { objective });

        var result = advice[0];
        // delta = 0 + 3*1 + 1*1 = 4
        Assert.Equal(result.Probability * 4, result.ExpectedDelta, precision: 10);
    }

    [Fact]
    public void GetAdvice_ExpectedDelta_CustomEvalConfig_PerTokenOverride()
    {
        var objective = _parser.Parse("Stun Attack", "[6666]");
        objective.Damage = 5;
        objective.Tokens = new List<string> { "Stun" };

        var eval = new DiceThroneApi.Models.EvaluationConfig
        {
            TokenValues = new Dictionary<string, double> { ["Stun"] = 5.0 },
            DefaultTokenValue = 2.0
        };

        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 0, new List<RollObjective> { objective }, eval: eval);

        var result = advice[0];
        // delta = 5 + 5 (Stun override) = 10
        Assert.Equal(result.Probability * 10, result.ExpectedDelta, precision: 10);
    }

    [Fact]
    public void GetAdvice_ExpectedDelta_ZeroTokenValue_ExcludesFromDelta()
    {
        var objective = _parser.Parse("Stun Attack", "[6666]");
        objective.Damage = 5;
        objective.Tokens = new List<string> { "Stun" };

        var eval = new DiceThroneApi.Models.EvaluationConfig
        {
            TokenValues = new Dictionary<string, double> { ["Stun"] = 0.0 }
        };

        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 0, new List<RollObjective> { objective }, eval: eval);

        var result = advice[0];
        // delta = 5 + 0 = 5
        Assert.Equal(result.Probability * 5, result.ExpectedDelta, precision: 10);
    }

    [Fact]
    public void GetAdvice_ExpectedDelta_ZeroDamage_ButNonZeroTokens_IsVisibleToBestStrategy()
    {
        // Objective with no damage but tokens should still surface in GetBestOverallStrategy
        var objective = _parser.Parse("Buff", "[6666]");
        objective.Damage = 0;
        objective.Tokens = new List<string> { "Rage" };

        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var eval = new DiceThroneApi.Models.EvaluationConfig { DefaultTokenValue = 3.0 };

        var best = _advisor.GetBestOverallStrategy(dice, 0, new List<RollObjective> { objective }, eval);

        Assert.NotNull(best);
        Assert.Equal("Buff", best.ObjectiveName);
        // delta = 0 + 3 = 3 for the token
        Assert.Equal(best.Probability * 3, best.ExpectedDelta, precision: 10);
    }

    public async Task AnalyticKeepOutperformsGreedyKeep_Top10Improvements()
    {
        var env = CreateTestEnvironment();
        var heroService = new HeroService(env, _parser);
        var heroes = await heroService.GetAllHeroesAsync();
        heroes = heroes.Where(h => h.Id.Contains("headless")).ToList();
        var allObjectives = heroes.SelectMany(h => h.Objectives).ToList();

        var greedyKeepMethod = typeof(DiceRollAdvisor).GetMethod("GreedyKeep", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(greedyKeepMethod);

        const double requiredDelta = 0.05;
        var deltas = new List<(string ObjectiveName, string ObjNotation, string Dice, double Improvement, double GreedyProb, string OptimalKeep)>();

        foreach (var objective in allObjectives)
        {
            if (deltas.Any(d => d.ObjNotation == objective.Notation))
                continue; // Skip duplicates

            for (int d1 = 1; d1 <= 6; d1++)
            for (int d2 = 1; d2 <= 6; d2++)
            for (int d3 = 1; d3 <= 6; d3++)
            for (int d4 = 1; d4 <= 6; d4++)
            for (int d5 = 1; d5 <= 6; d5++)
            {
                var dice = new List<int> { d1, d2, d3, d4, d5 };
                var diceString = string.Join("", dice.Order());
                if (deltas.Any(d => d.ObjNotation == objective.Notation && d.Dice == diceString))
                continue; // Skip duplicates

                if (_matcher.IsMatch(dice, objective))
                    continue; // Skip cases where no rerolling is needed

                var bestProb = _calculator.CalculateBestKeep(dice, 2, objective, out var optimalKeep);

                var optimalKeepDescr = string.Join("", dice.Where((die, idx) => optimalKeep[idx]));

                var greedyKeep = (List<bool>)greedyKeepMethod.Invoke(_advisor, new object[] { dice, objective })!;
                var greedyProb = _calculator.CalculateWithForcedKeep(dice, 2, objective, greedyKeep);

                var delta = bestProb - greedyProb;
                if (delta > 0 && greedyProb > 0) // Only consider cases where greedy is suboptimal and has a non-zero probability
                {
                    deltas.Add((objective.Name, objective.Notation, diceString, delta, greedyProb, optimalKeepDescr));
                }
            }
        }

        var top1000 = deltas
            .OrderByDescending(x => x.Improvement)
            .Take(1000)
            .ToList();

        _output.WriteLine("Top 10 analytic vs greedy improvement (probability delta):");
        foreach (var entry in top1000)
        {
            var percent = entry.Improvement * 100.0;
            _output.WriteLine($"{entry.ObjectiveName} [{entry.ObjNotation}] dice [{entry.Dice}] => {percent:F2}% (greedy {entry.GreedyProb:P2} vs optimal {entry.GreedyProb + entry.Improvement:P2}, keep [{entry.OptimalKeep}])");
        }

        Assert.NotEmpty(top1000);
        Assert.True(top1000.First().Improvement >= requiredDelta, $"Top improvement {top1000.First().Improvement:F4} is smaller than required {requiredDelta:F4}");
    }
}


internal class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string WebRootPath { get; set; } = string.Empty;
    public string ContentRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider? WebRootFileProvider { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider? ContentRootFileProvider { get; set; }
}

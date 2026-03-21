using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class DiceRollAdvisorTests
{
    private readonly DiceRollAdvisor _advisor;
    private readonly ProbabilityCalculator _calculator;
    private readonly DiceNotationParser _parser;

    public DiceRollAdvisorTests()
    {
        var matcher = new ObjectiveMatcher();
        _calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        _advisor = new DiceRollAdvisor(_calculator, simulator);
        _parser = new DiceNotationParser();
    }

    [Fact]
    public void GetAdvice_WithDamage_PopulatesExpectedDamage()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 5;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        var result = advice[0];
        Assert.Equal(5, result.Damage);
        Assert.InRange(result.ExpectedDamage, 0.0, 5.0);
        Assert.Equal(result.Probability * 5, result.ExpectedDamage, precision: 10);
    }

    [Fact]
    public void GetAdvice_WithZeroDamage_ExpectedDamageIsZero()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 0;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        Assert.Equal(0, advice[0].ExpectedDamage);
    }

    [Fact]
    public void GetAdvice_MatchingDice_ExpectedDamageEqualsFullDamage()
    {
        var objective = _parser.Parse("Test", "[6666]");
        objective.Damage = 4;

        // All four sixes kept, one reroll remaining — probability is very high
        var dice = new List<int> { 6, 6, 6, 6, 1 };
        var advice = _advisor.GetAdvice(dice, 1, new List<RollObjective> { objective });

        Assert.Single(advice);
        var result = advice[0];
        // With 4 sixes already, expected damage should be close to the full 4 damage
        Assert.InRange(result.ExpectedDamage, 3.0, 4.0);
    }

    [Fact]
    public void GetAdvice_ImpossibleObjective_ExpectedDamageIsZero()
    {
        // 6 sixes with only 5 dice — impossible
        var objective = _parser.Parse("Test", "[666666]");
        objective.Damage = 10;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective });

        Assert.Single(advice);
        Assert.Equal(0.0, advice[0].ExpectedDamage);
    }

    [Fact]
    public void GetAdvice_MonteCarlo_PopulatesExpectedDamage()
    {
        var objective = _parser.Parse("Test", "[6]");
        objective.Damage = 3;

        var dice = new List<int> { 1, 2, 3, 4, 5 };
        var advice = _advisor.GetAdvice(dice, 2, new List<RollObjective> { objective }, method: "montecarlo");

        Assert.Single(advice);
        var result = advice[0];
        Assert.Equal(3, result.Damage);
        Assert.Equal(result.Probability * 3, result.ExpectedDamage, precision: 10);
        Assert.InRange(result.ExpectedDamage, 0.0, 3.0);
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
        Assert.True(chaseAdvice.FallbackExpectedDamage > 0);
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
}

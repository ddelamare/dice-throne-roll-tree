using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class DiceRollAdvisorTests
{
    private readonly DiceRollAdvisor _advisor;
    private readonly DiceNotationParser _parser;

    public DiceRollAdvisorTests()
    {
        var matcher = new ObjectiveMatcher();
        var calculator = new ProbabilityCalculator(matcher);
        var simulator = new MonteCarloSimulator(matcher);
        _advisor = new DiceRollAdvisor(calculator, simulator);
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
}

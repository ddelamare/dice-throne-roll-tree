using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class ProbabilityCalculatorTests
{
    private readonly ProbabilityCalculator _calculator;
    private readonly DiceNotationParser _parser;

    public ProbabilityCalculatorTests()
    {
        var matcher = new ObjectiveMatcher();
        _calculator = new ProbabilityCalculator(matcher);
        _parser = new DiceNotationParser();
    }

    [Fact]
    public void Calculate_OneSix_WithOneDie_ReturnsCorrectProbability()
    {
        var objective = _parser.Parse("Test", "[6]");
        var probability = _calculator.Calculate(objective, 1);
        
        // With 2 rerolls: 1/6 + (5/6)*(1/6) + (5/6)^2*(1/6) = 0.4213
        Assert.InRange(probability, 0.42, 0.43);
    }

    [Fact]
    public void Calculate_TwoSixes_WithTwoDice_ReturnsCorrectProbability()
    {
        var objective = _parser.Parse("Test", "[66]");
        var probability = _calculator.Calculate(objective, 2);
        
        // With optimal play and 2 rerolls, probability is much higher than 1/36
        Assert.InRange(probability, 0.15, 0.20);
    }

    [Fact]
    public void Calculate_Probability_IsBetweenZeroAndOne()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var probability = _calculator.Calculate(objective, 5);
        
        Assert.InRange(probability, 0.0, 1.0);
    }

    [Fact]
    public void Calculate_FourSixes_WithFiveDice_ReturnsReasonableProbability()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var probability = _calculator.Calculate(objective, 5);
        
        Assert.InRange(probability, 0.0, 1.0);
        Assert.True(probability > 0);
    }

    [Fact]
    public void Calculate_ImpossibleObjective_ReturnsZero()
    {
        var objective = _parser.Parse("Test", "[666666]");
        var probability = _calculator.Calculate(objective, 5);
        
        Assert.Equal(0.0, probability);
    }

    [Fact]
    public void Calculate_WithNoRerolls_ReturnsOneSixth()
    {
        var objective = _parser.Parse("Test", "[6]");
        var probability = _calculator.Calculate(objective, 1, rerolls: 0);
        
        // With no rerolls, should be exactly 1/6
        Assert.InRange(probability, 0.165, 0.168);
    }

    [Fact]
    public void CalculatePreRoll_WithNoLockedDice_MatchesStandardCalculation()
    {
        var objective = _parser.Parse("Test", "[66]");

        var probability = _calculator.CalculatePreRoll(objective, 2);
        var baselineProbability = _calculator.Calculate(objective, 2);

        Assert.Equal(baselineProbability, probability, precision: 10);
    }

    [Fact]
    public void CalculatePreRoll_WithLockedSingleDie_DoesNotUseRerollsForLockedDie()
    {
        var objective = _parser.Parse("Test", "[6]");

        var probability = _calculator.CalculatePreRoll(objective, 1, new List<bool> { true });

        Assert.InRange(probability, 0.165, 0.168);
    }

    [Fact]
    public void CalculateBestKeep_WithMatchingDice_SuggestsKeepAll()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var currentDice = new List<int> { 6, 6, 6, 6, 1 };
        
        var probability = _calculator.CalculateBestKeep(currentDice, 1, objective, out var bestKeep);
        
        Assert.True(probability >= 0.9);
        Assert.Equal(5, bestKeep.Count);
    }

    [Fact]
    public void CalculateBestKeep_ReturnsValidKeepList()
    {
        var objective = _parser.Parse("Test", "[666]");
        var currentDice = new List<int> { 6, 6, 1, 2, 3 };
        
        var probability = _calculator.CalculateBestKeep(currentDice, 1, objective, out var bestKeep);
        
        Assert.Equal(currentDice.Count, bestKeep.Count);
        Assert.InRange(probability, 0.0, 1.0);
    }

    [Fact]
    public void CalculateBestKeep_WithRequiredKeep_AlwaysKeepsRequiredDie()
    {
        var objective = _parser.Parse("Test", "[66]");
        var currentDice = new List<int> { 1, 6, 2, 3, 4, 5 };
        var requiredKeep = new List<bool> { true, false, false, false, false, false };

        var probability = _calculator.CalculateBestKeep(currentDice, 2, objective, out var bestKeep, requiredKeep);

        Assert.Equal(currentDice.Count, bestKeep.Count);
        Assert.True(bestKeep[0]);
        Assert.InRange(probability, 0.0, 1.0);
    }
}

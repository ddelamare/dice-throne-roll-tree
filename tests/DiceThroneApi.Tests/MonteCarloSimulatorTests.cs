using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class MonteCarloSimulatorTests
{
    private readonly MonteCarloSimulator _simulator;
    private readonly ProbabilityCalculator _calculator;
    private readonly DiceNotationParser _parser;

    public MonteCarloSimulatorTests()
    {
        var matcher = new ObjectiveMatcher();
        _calculator = new ProbabilityCalculator(matcher);
        _simulator = new MonteCarloSimulator(matcher);
        _parser = new DiceNotationParser();
    }

    [Fact]
    public void Simulate_OneSix_WithOneDie_ReturnsCorrectProbability()
    {
        var objective = _parser.Parse("Test", "[6]");
        var probability = _simulator.Simulate(objective, 1, iterations: 10000);
        
        // With 2 rerolls: approximately 0.42
        Assert.InRange(probability, 0.38, 0.45);
    }

    [Fact]
    public void Simulate_ReturnsProbabilityBetweenZeroAndOne()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var probability = _simulator.Simulate(objective, 5, iterations: 1000);
        
        Assert.InRange(probability, 0.0, 1.0);
    }

    [Fact]
    public void Simulate_CloseToAnalytic_ForSimpleCase()
    {
        var objective = _parser.Parse("Test", "[66]");
        
        var analyticProb = _calculator.Calculate(objective, 2);
        var monteCarloProb = _simulator.Simulate(objective, 2, iterations: 10000);
        
        var diff = Math.Abs(analyticProb - monteCarloProb);
        Assert.True(diff < 0.05, $"Difference {diff} is too large. Analytic: {analyticProb}, Monte Carlo: {monteCarloProb}");
    }

    [Fact]
    public void Simulate_ImpossibleObjective_ReturnsZero()
    {
        var objective = _parser.Parse("Test", "[666666]");
        var probability = _simulator.Simulate(objective, 5, iterations: 1000);
        
        Assert.Equal(0.0, probability);
    }

    [Fact]
    public void Simulate_SmallStraight_ReturnsReasonableProbability()
    {
        var objective = _parser.Parse("Test", "SmallStraight");
        var probability = _simulator.Simulate(objective, 5, iterations: 5000);
        
        Assert.InRange(probability, 0.0, 1.0);
        Assert.True(probability > 0);
    }

    // ── Improved Straight Handling Tests ────────────────────────────────────

    [Fact]
    public void Simulate_LargeStraight_ReturnsReasonableProbability()
    {
        var objective = _parser.Parse("Test", "LargeStraight");
        var probability = _simulator.Simulate(objective, 5, iterations: 5000);
        
        Assert.InRange(probability, 0.0, 1.0);
        Assert.True(probability > 0);
    }

    [Fact]
    public void Simulate_SmallStraight_ImprovedStrategyWorks()
    {
        // Test that the improved SmallStraight strategy works
        var objective = _parser.Parse("Test", "SmallStraight");
        var probability = _simulator.Simulate(objective, 5, iterations: 10000);
        
        // SmallStraight with 5 dice and 2 rerolls should have decent probability
        // The improved strategy considers all three straights (1234, 2345, 3456)
        Assert.InRange(probability, 0.3, 1.0);
    }

    [Fact]
    public void Simulate_WithOptimalStrategy_IsAvailable()
    {
        // Test that optimal strategy mode can be constructed
        var matcher = new ObjectiveMatcher();
        var calculatorForTest = new ProbabilityCalculator(matcher);
        var simulatorWithOptimal = new MonteCarloSimulator(matcher, calculatorForTest, useOptimalStrategy: true);
        
        var objective = _parser.Parse("Test", "[66]");
        
        // Just verify it runs without error on small iterations
        var probability = simulatorWithOptimal.Simulate(objective, 2, iterations: 100);
        Assert.InRange(probability, 0.0, 1.0);
    }
}

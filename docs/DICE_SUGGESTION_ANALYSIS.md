# Dice Suggestion Algorithm Analysis

This document describes the dice suggestion (roll advisor) algorithm and the improvements that have been implemented.

## Table of Contents

1. [Current Implementation Overview](#current-implementation-overview)
2. [Algorithm Analysis](#algorithm-analysis)
3. [Implemented Improvements](#implemented-improvements)
4. [Testing](#testing)

---

## Current Implementation Overview

The dice suggestion system consists of two main components:

### 1. DiceRollAdvisor (Advice Generation)

Located in `Services/DiceRollAdvisor.cs`, this service provides roll advice by:

1. **For All Methods (Analytic and Monte Carlo):**
   - Uses `ProbabilityCalculator.CalculateBestKeep()` to find the optimal keep strategy
   - Returns optimal dice selection suggestions regardless of probability method

2. **Probability Calculation:**
   - For **Analytic**: Uses exact probability calculation via dynamic programming
   - For **Monte Carlo**: Uses statistical simulation with configurable iteration count

3. **Fallback Calculation:**
   - For each damage-dealing objective, computes the best fallback objective
   - Uses `CalculateWithForcedKeep()` to determine probability of hitting a secondary objective when committed to a primary strategy

4. **Best Overall Strategy:**
   - `GetBestOverallStrategy()` method finds the objective with highest expected damage across all damage-dealing objectives
   - Returns a unified recommendation for strategic play

### 2. Keep Strategies

The `MonteCarloSimulator` uses improved keep logic for straights:

```csharp
// For SmallStraight - considers all three possible straights
var candidates = new[]
{
    new HashSet<int> { 1, 2, 3, 4 },
    new HashSet<int> { 2, 3, 4, 5 },
    new HashSet<int> { 3, 4, 5, 6 }
};
// Picks the candidate with most matching dice
// Keeps at most one of each value (handles duplicates)

// For LargeStraight - considers both possible straights
var candidates = new[]
{
    new HashSet<int> { 1, 2, 3, 4, 5 },
    new HashSet<int> { 2, 3, 4, 5, 6 }
};
// Picks the candidate with most matching dice
// Keeps at most one of each value (handles duplicates)
```

---

## Algorithm Analysis

### Analytic Method: Optimal Strategy

The `CalculateBestKeep()` provides **provably optimal** dice suggestions:

```
Optimal P = max over all keep strategies K of:
    E[P(outcome | K)]
```

**Strengths:**
- Mathematically optimal
- Considers all possible keep combinations
- Accounts for future reroll opportunities

**Algorithm in CalculateBestKeep:**
```
1. If dice already match → keep all, return 1.0
2. If no rerolls left → keep all, return 0.0
3. Enumerate all keep histograms
4. For each strategy:
   a. Compute expected probability over all reroll outcomes
   b. Use memoized OptimalProbability for future states
5. Return best strategy and its probability

The keep-strategy search includes the "reroll everything" option when that maximizes the
chance of converting into damage. This avoids forced keeps on dice that do not help the
current plan.
```

### Monte Carlo Method: Improved Heuristics

The Monte Carlo simulation uses improved heuristics for better accuracy:

1. **Optimal play mode**: Optional `useOptimalStrategy` parameter for simulations
2. **Smart straight handling**: Considers all possible straights and picks the best candidate
3. **Duplicate elimination**: Keeps at most one of each value for straight objectives

---

## Implemented Improvements

The following improvements have been implemented to enhance the dice suggestion algorithm:

### ✅ Improvement 1: Optimal Strategy for All Methods

**Implementation:** `GetAdvice()` now uses `ProbabilityCalculator.CalculateBestKeep()` for dice suggestions regardless of whether using analytic or Monte Carlo for probability estimation.

```csharp
// Always use optimal keep strategy from the analytic calculator
var optimalProb = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);

// Use Monte Carlo for probability if requested
var prob = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase)
    ? _simulator.Simulate(objective, currentDice.Count, 10000, rollsRemaining)
    : optimalProb;
```

**Benefit:** Users always get optimal keep suggestions, including full-reroll lines when no
current dice are worth anchoring.

### ✅ Improvement 2: Fixed Straight Duplicate Handling

**Implementation:** For straights, the algorithm now keeps at most one of each value.

```csharp
// Keep at most one of each value
var keptValues = new HashSet<int>();
var toKeep = new List<bool>();

foreach (var d in dice)
{
    if (bestCandidate.Contains(d) && !keptValues.Contains(d))
    {
        toKeep.Add(true);
        keptValues.Add(d);
    }
    else
    {
        toKeep.Add(false);
    }
}
```

**Example:** For SmallStraight with dice `[1, 1, 2, 3, 5]`:
- Now correctly keeps `[1, 2, 3, 5]` and rerolls the duplicate 1

### ✅ Improvement 3: SmallStraight Considers All Three Straights

**Implementation:** Analyzes which of the three small straights (1234, 2345, 3456) is most achievable.

```csharp
var candidates = new[]
{
    new HashSet<int> { 1, 2, 3, 4 },
    new HashSet<int> { 2, 3, 4, 5 },
    new HashSet<int> { 3, 4, 5, 6 }
};

// Score each candidate by how many unique values we already have
var diceSet = dice.ToHashSet();
var bestCandidate = candidates
    .Select(c => (candidate: c, score: c.Count(v => diceSet.Contains(v))))
    .OrderByDescending(x => x.score)
    .First()
    .candidate;
```

**Example:** For dice `[2, 3, 5, 6, 6]`:
- Now correctly targets {3,4,5,6} straight and keeps `[3, 5, 6]`
- Rerolls to find a 4, rather than targeting low values

### ✅ Improvement 4: Best Overall Strategy Method

**Implementation:** Added `GetBestOverallStrategy()` to compute the globally optimal strategy across all objectives.

```csharp
public RollAdvice? GetBestOverallStrategy(
    List<int> currentDice, 
    int rollsRemaining, 
    List<RollObjective> objectives)
{
    RollAdvice? bestAdvice = null;
    double bestExpectedDamage = 0;

    foreach (var objective in objectives.Where(o => o.Damage > 0))
    {
        var prob = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out var toKeep);
        var expectedDamage = prob * objective.Damage;

        if (expectedDamage > bestExpectedDamage)
        {
            bestExpectedDamage = expectedDamage;
            // ... create bestAdvice with all fields
        }
    }

    return bestAdvice;
}
```

**Benefit:** Strategic players can now maximize expected damage across all objectives, while
preferring the higher-probability line when two options have the same expected damage.

### ✅ Improvement 5: Monte Carlo Optimal Play Mode

**Implementation:** Added optional `useOptimalStrategy` parameter to Monte Carlo simulation.

```csharp
public MonteCarloSimulator(ObjectiveMatcher matcher, ProbabilityCalculator probabilityCalculator, bool useOptimalStrategy = false)
{
    _matcher = matcher;
    _probabilityCalculator = probabilityCalculator;
    _useOptimalStrategy = useOptimalStrategy;
}

// In SimulateOneGame:
if (_useOptimalStrategy && _probabilityCalculator != null)
{
    _probabilityCalculator.CalculateBestKeep(dice, rerollsLeft - roll, objective, out toKeep);
}
else
{
    toKeep = DecideKeep(dice, objective);  // Improved greedy
}
```

**Trade-off:** Optimal play mode is slower but more accurate.

### ✅ Improvement 6: Probability Delta Fields

**Implementation:** Added `BaselineProbability` and `ProbabilityImprovement` fields to `RollAdvice`.

```csharp
public class RollAdvice
{
    // ... existing properties ...
    
    /// <summary>
    /// Probability of hitting the objective if all dice are rerolled (baseline comparison).
    /// </summary>
    public double BaselineProbability { get; set; }
    
    /// <summary>
    /// Improvement in probability from optimal keep strategy vs rerolling all dice.
    /// </summary>
    public double ProbabilityImprovement { get; set; }
}
```

**Benefit:** Users can see the value of their keep decisions compared to rerolling everything.

---

## Testing

The following tests verify the implemented improvements:

### Unit Tests for Straight Handling
- `Simulate_SmallStraight_ImprovedStrategyWorks` - Tests that SmallStraight considers all three straights
- `Simulate_LargeStraight_ReturnsReasonableProbability` - Tests LargeStraight handling

### Optimal Strategy Tests
- `GetAdvice_MonteCarloUsesOptimalKeepSuggestions` - Verifies Monte Carlo uses optimal keep strategy
- `GetBestOverallStrategy_ReturnsHighestExpectedDamage` - Tests best overall strategy selection
- `GetBestOverallStrategy_ReturnsNull_WhenNoDamageObjectives` - Edge case handling

### Probability Delta Tests
- `GetAdvice_PopulatesBaselineProbability` - Verifies baseline probability is calculated
- `GetAdvice_ProbabilityImprovementEqualsProb_MinusBaseline` - Verifies improvement calculation

### Performance Tests
- `Simulate_WithOptimalStrategy_IsAvailable` - Verifies optimal strategy mode can be used

---

## Summary

The dice suggestion algorithm now provides:

1. **Optimal keep suggestions** for all probability methods (analytic and Monte Carlo)
2. **Smart straight handling** that considers all possible straights and eliminates duplicates
3. **Best overall strategy** recommendation across multiple objectives
4. **Probability improvement metrics** to help users understand the value of keep decisions
5. **Optional optimal play mode** in Monte Carlo for higher accuracy

These improvements maintain backward compatibility while providing significantly better recommendations for strategic play.

# Dice Suggestion Algorithm Analysis

This document analyzes the current dice suggestion (roll advisor) algorithm, identifies its limitations, and proposes improvements.

## Table of Contents

1. [Current Implementation Overview](#current-implementation-overview)
2. [Algorithm Analysis](#algorithm-analysis)
3. [Identified Limitations](#identified-limitations)
4. [Proposed Improvements](#proposed-improvements)
5. [Implementation Recommendations](#implementation-recommendations)

---

## Current Implementation Overview

The dice suggestion system consists of two main components:

### 1. DiceRollAdvisor (Advice Generation)

Located in `Services/DiceRollAdvisor.cs`, this service provides roll advice by:

1. **For Analytic Method:**
   - Uses `ProbabilityCalculator.CalculateBestKeep()` to find the optimal keep strategy
   - Returns exact probabilities with optimal dice selection

2. **For Monte Carlo Method:**
   - Uses `MonteCarloSimulator.Simulate()` to estimate success probability
   - Uses `GreedyKeep()` to suggest which dice to keep (NOT optimal)

3. **Fallback Calculation:**
   - For each damage-dealing objective, computes the best fallback objective
   - Uses `CalculateWithForcedKeep()` to determine probability of hitting a secondary objective when committed to a primary strategy

### 2. Greedy Keep Strategies

Both `MonteCarloSimulator` and `DiceRollAdvisor` use identical greedy keep logic:

```csharp
// For Standard objectives
for each die d in dice:
    for each group g in objective.Groups:
        if not used[g] and d in group.AllowedValues:
            used[g] = true  // Claim this group
            keep(d)
            break
    else:
        reroll(d)

// For SmallStraight
keep d if d in {1, 2, 3, 4, 5}

// For LargeStraight  
keep d if d in {1, 2, 3, 4, 5, 6}  // or committed straight if complete
```

---

## Algorithm Analysis

### Analytic Method: Optimal Strategy

When using the analytic method, `CalculateBestKeep()` provides **provably optimal** dice suggestions:

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
```

### Monte Carlo Method: Greedy Heuristic

The Monte Carlo method uses a **greedy** heuristic that does NOT compute optimal keep strategies:

**Key Issue:** Even when using Monte Carlo, the `GetAdvice()` method uses `GreedyKeep()` for suggestions, which may not match what the optimal strategy would be.

---

## Identified Limitations

### Limitation 1: Greedy Strategy is Suboptimal

**Problem:** The greedy matching can make poor decisions.

**Example:** Objective `[(12)(12)(12)]` (need three dice, each showing 1 or 2)

Current dice: `[1, 2, 1, 3, 4]`

**Greedy behavior:**
1. Die 1 (value=1): matches group 1 → KEEP
2. Die 2 (value=2): matches group 2 → KEEP  
3. Die 3 (value=1): matches group 3 → KEEP
4. Die 4 (value=3): no match → REROLL
5. Die 5 (value=4): no match → REROLL

**Result:** Keep `[1, 2, 1]`, reroll 2 dice. Correct!

**But consider:** Objective `[(123)(456)(456)]` (one low, two high)

Current dice: `[3, 5, 2, 6, 1]`

**Greedy behavior (order-dependent):**
1. Die 1 (value=3): matches group 1 (123) → KEEP
2. Die 2 (value=5): matches group 2 (456) → KEEP
3. Die 3 (value=2): no remaining match → REROLL ❌
4. Die 4 (value=6): matches group 3 (456) → KEEP
5. Die 5 (value=1): no remaining match → REROLL ❌

**Greedy keeps:** `[3, 5, 6]` — CORRECT (matches all groups)

**Alternative bad case:** Current dice: `[1, 2, 3, 5, 4]`

1. Die 1 (value=1): matches group 1 (123) → KEEP
2. Die 2 (value=2): no remaining match (group 1 taken) → REROLL ❌
3. Die 3 (value=3): no remaining match → REROLL ❌
4. Die 4 (value=5): matches group 2 (456) → KEEP
5. Die 5 (value=4): matches group 3 (456) → KEEP

**Greedy keeps:** `[1, 5, 4]` — CORRECT (but COULD have kept [3, 5, 4] too)

Actually, the greedy algorithm works correctly here because it only needs ONE die per group.

### Limitation 2: SmallStraight Always Prefers Low Values

**Problem:** The greedy strategy always keeps dice in `{1, 2, 3, 4, 5}` for SmallStraight, ignoring the possibility that `{2, 3, 4, 5, 6}` might be better based on current dice.

**Example:** Current dice: `[2, 3, 5, 6, 6]`

**Current behavior:** 
- Keep `[2, 3, 5]` (values in {1,2,3,4,5})
- Reroll `[6, 6]`

**Optimal behavior:**
- Keep `[2, 3, 5, 6]` (values in {2,3,4,5,6} straight)
- Reroll only 1 die (need a 4)
- Probability is HIGHER keeping 4 useful dice than 3

### Limitation 3: LargeStraight Commits Too Early

**Problem:** For LargeStraight, the greedy strategy keeps ALL dice in `{1,2,3,4,5,6}`, even when some should be rerolled.

**Example:** Current dice: `[1, 1, 2, 3, 6]`

**Current behavior:**
- Keep all 5 dice (all in {1,2,3,4,5,6})
- But wait... we have TWO 1s! One must be rerolled.

Actually, looking at the code:
```csharp
var straightVals = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
for (int i = 0; i < dice.Count; i++)
{
    toKeep.Add(straightVals.Contains(dice[i]));
}
```

This keeps ALL dice that could be part of A straight, but doesn't account for **duplicates**.

**Correct behavior:** 
- Keep one 1, the 2, the 3, and the 6
- Reroll the second 1
- We need 4 or 5 to complete either straight

### Limitation 4: Monte Carlo Uses Greedy for Suggestions

**Problem:** When a user requests Monte Carlo method, `GetAdvice()` returns `GreedyKeep()` suggestions instead of computing the optimal strategy.

```csharp
if (method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase))
{
    var prob = _simulator.Simulate(objective, ...);
    var toKeep = GreedyKeep(currentDice, objective);  // ← Greedy, not optimal!
    ...
}
```

The probability is estimated via simulation, but the **suggested dice to keep** come from the simple greedy heuristic, which may not match the optimal strategy.

### Limitation 5: No Consideration of Multiple Objectives

**Problem:** The advisor optimizes each objective independently, but strategic players might want to:
- Hedge between multiple objectives
- Maximize expected damage across all objectives
- Consider the probability of hitting ANY objective

**Example:** Current dice: `[6, 6, 2, 3, 4]`

**Objective A:** `[6666]` (four 6s) - Damage: 8
**Objective B:** `SmallStraight` - Damage: 3

Current advisor:
- For Objective A: Keep `[6, 6]`, probability ~5%
- For Objective B: Keep `[2, 3, 4]`, probability ~50%

A more sophisticated advisor might suggest:
- Keep `[2, 3, 4]` for a 50% × 3 = 1.5 expected damage
- vs. Keep `[6, 6]` for a 5% × 8 = 0.4 expected damage
- **Recommendation: Go for the straight!**

The fallback mechanism partially addresses this, but it doesn't provide a unified "best overall strategy" recommendation.

### Limitation 6: Duplicate Handling in Straights

**Problem:** The straight detection keeps duplicate values, when only one of each value is useful.

For SmallStraight with dice `[1, 1, 2, 3, 5]`:
- Current: Keeps all 5 (all values in {1,2,3,4,5})
- Optimal: Keep `[1, 2, 3, 5]`, reroll one 1

---

## Proposed Improvements

### Improvement 1: Use Optimal Strategy for All Methods

**Change:** Use `ProbabilityCalculator.CalculateBestKeep()` for dice suggestions even when using Monte Carlo for probability estimation.

```csharp
// In GetAdvice()
var toKeep = _calculator.CalculateBestKeep(currentDice, rollsRemaining, objective, out _);
var prob = method == "montecarlo" 
    ? _simulator.Simulate(objective, currentDice.Count, 10000, rollsRemaining)
    : _calculator.Calculate(objective, currentDice.Count, rerolls: rollsRemaining);
```

**Benefit:** Users always get optimal keep suggestions, regardless of probability method.

### Improvement 2: Fix Straight Duplicate Handling

**Change:** For straights, keep at most one of each value.

```csharp
private List<bool> GreedyKeepSmallStraight(List<int> dice)
{
    var toKeep = new List<bool>();
    var keptValues = new HashSet<int>();
    var straightVals = new HashSet<int> { 1, 2, 3, 4, 5 };
    
    for (int i = 0; i < dice.Count; i++)
    {
        if (straightVals.Contains(dice[i]) && !keptValues.Contains(dice[i]))
        {
            toKeep.Add(true);
            keptValues.Add(dice[i]);
        }
        else
        {
            toKeep.Add(false);
        }
    }
    return toKeep;
}
```

### Improvement 3: SmallStraight Should Consider Both Straights

**Change:** Analyze which of the three small straights (1234, 2345, 3456) is most achievable.

```csharp
private List<bool> GreedyKeepSmallStraight(List<int> dice)
{
    var candidates = new[]
    {
        new HashSet<int> { 1, 2, 3, 4 },
        new HashSet<int> { 2, 3, 4, 5 },
        new HashSet<int> { 3, 4, 5, 6 }
    };
    
    // Score each candidate by how many values we already have
    var bestCandidate = candidates
        .Select(c => (candidate: c, score: dice.Count(d => c.Contains(d))))
        .OrderByDescending(x => x.score)
        .First()
        .candidate;
    
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
    
    return toKeep;
}
```

### Improvement 4: Add "Best Overall Strategy" Recommendation

**Change:** Add a method to compute the globally optimal strategy across all objectives.

```csharp
public RollAdvice GetBestOverallStrategy(
    List<int> currentDice, 
    int rollsRemaining, 
    List<RollObjective> objectives)
{
    RollAdvice? bestAdvice = null;
    double bestExpectedDamage = 0;
    
    foreach (var objective in objectives.Where(o => o.Damage > 0))
    {
        var prob = _calculator.CalculateBestKeep(
            currentDice, rollsRemaining, objective, out var toKeep);
        var expectedDamage = prob * objective.Damage;
        
        if (expectedDamage > bestExpectedDamage)
        {
            bestExpectedDamage = expectedDamage;
            bestAdvice = new RollAdvice
            {
                ObjectiveName = objective.Name,
                DiceToKeep = toKeep,
                Probability = prob,
                CalculationMethod = "Analytic",
                Damage = objective.Damage,
                ExpectedDamage = expectedDamage
            };
        }
    }
    
    return bestAdvice ?? new RollAdvice();
}
```

### Improvement 5: Improve Monte Carlo Simulation Strategy

**Change:** Use optimal play in Monte Carlo simulation instead of greedy.

```csharp
private bool SimulateOneGame(RollObjective objective, int totalDice, int rerollsLeft)
{
    var dice = RollDice(totalDice);
    
    for (int roll = 0; roll <= rerollsLeft; roll++)
    {
        if (_matcher.IsMatch(dice, objective))
            return true;
        
        if (roll < rerollsLeft)
        {
            // Use optimal strategy instead of greedy
            var _ = _calculator.CalculateBestKeep(dice, rerollsLeft - roll, objective, out var toKeep);
            var kept = dice.Zip(toKeep, (d, k) => (d, k)).Where(x => x.k).Select(x => x.d).ToList();
            var rerolled = RollDice(totalDice - kept.Count);
            dice = kept.Concat(rerolled).ToList();
        }
    }
    
    return _matcher.IsMatch(dice, objective);
}
```

**Trade-off:** This makes Monte Carlo slower but more accurate. The results will match the analytic method more closely.

### Improvement 6: Add Probability Delta Display

**Change:** Show how much probability changes based on each keep decision.

```csharp
public class RollAdvice
{
    // ... existing properties ...
    
    public double BaselineProbability { get; set; }  // Probability if reroll all
    public double ProbabilityImprovement { get; set; }  // Improvement from optimal keep
}
```

This helps users understand the value of their keep decisions.

---

## Implementation Recommendations

### Priority Order

1. **High Priority:**
   - Fix straight duplicate handling (Improvement 2)
   - Use optimal strategy for Monte Carlo suggestions (Improvement 1)
   
2. **Medium Priority:**
   - SmallStraight should consider all three straights (Improvement 3)
   - Add best overall strategy recommendation (Improvement 4)

3. **Lower Priority:**
   - Improve Monte Carlo simulation strategy (Improvement 5)
   - Add probability delta display (Improvement 6)

### Backward Compatibility

All improvements can be implemented without breaking the existing API:
- Add new optional response fields
- Maintain existing behavior as default
- Add new optional parameters to enable enhanced features

### Testing Recommendations

1. **Unit tests for straight handling:**
   - Test SmallStraight with duplicates: `[1, 1, 2, 3, 4]`
   - Test LargeStraight with duplicates: `[1, 1, 2, 3, 4, 5]`
   
2. **Comparison tests:**
   - Verify Monte Carlo with optimal play matches analytic results
   - Ensure greedy improvements don't regress existing test cases

3. **Performance tests:**
   - Ensure optimal play in Monte Carlo doesn't make it too slow
   - Benchmark with 10K iterations

---

## Summary

The current dice suggestion algorithm works well for the analytic method, which provides provably optimal recommendations. However, the greedy heuristics used for Monte Carlo mode and straight objectives have several limitations:

1. **Straight objectives keep duplicates** that should be rerolled
2. **SmallStraight always prefers low values** even when high values are better
3. **Monte Carlo uses suboptimal suggestions** despite computing simulation probabilities
4. **No unified "best strategy" recommendation** across multiple objectives

The proposed improvements address these issues while maintaining backward compatibility and reasonable performance characteristics.

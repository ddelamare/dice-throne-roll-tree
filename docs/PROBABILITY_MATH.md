# Probability Calculation Mathematics

This document describes the mathematical foundations of the probability calculations in the Dice Throne Roll Tree application.

## Table of Contents

1. [Overview](#overview)
2. [State Representation](#state-representation)
3. [Exact Probability Calculation](#exact-probability-calculation)
4. [Monte Carlo Simulation](#monte-carlo-simulation)
5. [Comparison of Methods](#comparison-of-methods)
6. [Mathematical Proofs](#mathematical-proofs)

---

## Overview

The application calculates the probability of achieving various dice roll objectives in Dice Throne, where players:
- Roll 5 dice (configurable from 1 to 7 dice)
- Get 1 initial roll + 2 rerolls per turn
- Can choose which dice to keep between rolls
- Must achieve specific patterns (e.g., four 6s, small straight, etc.)

Two calculation methods are provided:

1. **Analytic (Exact)**: Uses dynamic programming to compute exact probabilities assuming optimal play
2. **Monte Carlo**: Uses statistical simulation with a greedy heuristic

---

## State Representation

### Histogram Encoding

Instead of tracking individual dice values, the system uses a **histogram representation** where dice are grouped by face value:

```
State = [c₁, c₂, c₃, c₄, c₅, c₆]
```

Where `cᵢ` is the count of dice showing face value `i`.

**Example:**
- Dice roll `[6, 6, 3, 3, 1]` → Histogram `[1, 0, 2, 0, 0, 2]`
- Dice roll `[1, 1, 1, 1, 1]` → Histogram `[5, 0, 0, 0, 0, 0]`

### Why Histograms?

1. **Canonical form**: Multiple orderings of the same dice map to the same histogram
2. **Smaller state space**: For `n` dice, there are only `C(n+5, 5)` distinct histograms vs `6^n` ordered tuples
3. **Efficient memoization**: Reduces cache collisions and memory usage

### Memo Key Encoding

The histogram and rerolls remaining are encoded into a single 64-bit integer:

```
key = c₁ | (c₂ << 3) | (c₃ << 6) | (c₄ << 9) | (c₅ << 12) | (c₆ << 15) | (rerollsLeft << 18)
```

Each face count uses 3 bits (max value 7), and rerolls uses 3 bits. This gives a unique key for each game state.

---

## Exact Probability Calculation

### Algorithm Overview

The `ProbabilityCalculator` uses dynamic programming with memoization to compute the exact probability of achieving an objective with optimal play.

### Core Recursion

For a given state `(histogram, rerollsLeft)`, the optimal probability is:

```
P(histogram, rerollsLeft) = max over all keep strategies K of:
    Σ P(histogram', rerollsLeft-1) × P(outcome)
    for all possible reroll outcomes histogram'
```

**Base cases:**
- If the current dice match the objective: `P = 1.0`
- If `rerollsLeft = 0` and objective not met: `P = 0.0`

### Step 1: Initial Roll Enumeration

Rather than enumerate all `6ⁿ` ordered initial rolls, we enumerate **distinct histograms** with their **multiplicities**:

```
P_total = (1/6ⁿ) × Σ multiplicity(h) × P(h, rerolls)
          for all distinct histograms h
```

The number of distinct histograms is `C(n+5, 5)`, which is:
- 252 for n=5 dice (vs 7,776 ordered outcomes)
- 462 for n=6 dice (vs 46,656 ordered outcomes)

### Step 2: Keep Strategy Enumeration

For each face value `f`, we can keep anywhere from 0 to `histogram[f]` dice of that face.

**Total strategies:**
```
Π (histogram[f] + 1) for f = 1 to 6
```

**Example:** For histogram `[0, 0, 2, 0, 0, 3]` (two 3s, three 6s):
- Strategies = (0+1) × (0+1) × (2+1) × (0+1) × (0+1) × (3+1) = 12

This is much smaller than `2⁵ = 32` bitmask strategies for 5 dice.

### Step 3: Reroll Outcome Computation

After keeping some dice, we reroll `r` dice. Instead of enumerating `6ʳ` outcomes:

1. Enumerate distinct reroll histograms: `C(r+5, 5)` outcomes
2. Compute the **multinomial coefficient** for each outcome
3. Weight probabilities by multiplicity

### Multinomial Coefficient

The number of ways to roll a specific histogram `[c₁, ..., c₆]` with `n = Σcᵢ` dice is:

```
         n!
M = ─────────────────
    c₁! × c₂! × ... × c₆!
```

**Example:** Rolling `[2, 0, 0, 0, 0, 3]` (two 1s, three 6s) with 5 dice:
```
M = 5! / (2! × 0! × 0! × 0! × 0! × 3!) = 120 / (2 × 1 × 1 × 1 × 1 × 6) = 10
```

### Algorithm Pseudocode

```
function Calculate(objective, totalDice, rerolls):
    memo = {}
    totalProb = 0
    
    for each distinct histogram h with multiplicity m:
        totalProb += m × OptimalProbability(h, rerolls, objective, memo)
    
    return totalProb / 6^totalDice

function OptimalProbability(histogram, rerollsLeft, objective, memo):
    dice = HistogramToDice(histogram)
    
    if IsMatch(dice, objective):
        return 1.0
    
    if rerollsLeft == 0:
        return 0.0
    
    key = Encode(histogram, rerollsLeft)
    if key in memo:
        return memo[key]
    
    bestProb = 0
    totalDice = sum(histogram)
    
    for each keepHistogram in EnumerateKeepStrategies(histogram):
        keptCount = sum(keepHistogram)
        rerollCount = totalDice - keptCount
        
        if rerollCount == 0:
            continue
        
        prob = 0
        for each rerollHistogram with multiplicity m:
            newHistogram = keepHistogram + rerollHistogram
            prob += m × OptimalProbability(newHistogram, rerollsLeft-1, objective, memo)
        
        prob /= 6^rerollCount
        bestProb = max(bestProb, prob)
    
    memo[key] = bestProb
    return bestProb
```

### Time Complexity

Let `n` be the number of dice and `r` be the number of rerolls.

- **State space**: `O(C(n+5,5) × r)` states. The binomial coefficient `C(n+5,5) = (n+5)!/(5!×n!)` is polynomial in `n`, specifically `O(n^5/120)` for large `n`.
- **Keep strategies per state**: `O(∏(cᵢ+1))` — this depends on the histogram distribution. In the worst case (e.g., one die per face), this is `O(2^n)`, but for typical histograms (many duplicates), it's much smaller.
- **Reroll outcomes per strategy**: `O(C(m+5,5))` where `m` is the number of rerolled dice.

**Practical complexity analysis:**

For realistic game scenarios with 5-7 dice:
- State space: ~250-460 distinct histograms × `r` reroll stages
- Keep strategies: typically 10-100 per state (due to duplicate dice)
- With memoization, each state is computed only once

For n=5 dice with r=2 rerolls, this is manageable with typical computation times of 10-50ms.

**Note:** The theoretical worst-case complexity is higher, but memoization and the structure of dice histograms (often with duplicates) keep practical performance fast.

### Space Complexity

- **Memoization table**: `O(C(n+5,5) × r)` entries — approximately 252 states for n=5, 462 for n=6
- Each entry stores one `double`: 8 bytes
- For n=5, r=2: ~250 × 2 × 8 ≈ 4 KB

---

## Monte Carlo Simulation

### Algorithm Overview

The `MonteCarloSimulator` estimates probabilities by running many random simulations and counting successes.

### Core Algorithm

```
function Simulate(objective, totalDice, iterations, rerolls):
    successes = 0
    
    for i = 1 to iterations:
        if SimulateOneGame(objective, totalDice, rerolls):
            successes++
    
    return successes / iterations

function SimulateOneGame(objective, totalDice, rerollsLeft):
    dice = RollDice(totalDice)  // Random roll
    
    for roll = 0 to rerollsLeft:
        if IsMatch(dice, objective):
            return true
        
        if roll < rerollsLeft:
            toKeep = DecideKeep(dice, objective)  // Greedy heuristic
            kept = FilterKept(dice, toKeep)
            rerolled = RollDice(totalDice - |kept|)
            dice = kept ∪ rerolled
    
    return IsMatch(dice, objective)
```

### Greedy Keep Heuristic

The Monte Carlo simulator uses a **greedy** dice selection strategy:

#### For Standard Objectives (e.g., `[6666]`, `[(123)(123)(123)]`)

```
function DecideKeep(dice, objective):
    groupNeeds = objective.Groups.Select(g => g.AllowedValues)
    used = [false] × groupNeeds.Count
    toKeep = []
    
    for each die d in dice:
        for each group g in groupNeeds:
            if not used[g] and d in groupNeeds[g]:
                used[g] = true
                toKeep.Add(true)
                break
        else:
            toKeep.Add(false)
    
    return toKeep
```

**Key behavior:**
- Greedily matches dice to objective groups in order
- First matching group "claims" a die
- Remaining dice are rerolled

#### For Small Straight

```
function DecideKeep(dice, objective):
    straightVals = {1, 2, 3, 4, 5}  // Prefer low values
    return [d in straightVals for d in dice]
```

#### For Large Straight

```
function DecideKeep(dice, objective):
    straightVals = {1, 2, 3, 4, 5, 6}
    diceSet = ToSet(dice)
    
    # Check if already have a complete straight
    if {1,2,3,4,5} ⊆ diceSet:
        straightVals = {1, 2, 3, 4, 5}
    else if {2,3,4,5,6} ⊆ diceSet:
        straightVals = {2, 3, 4, 5, 6}
    
    return [d in straightVals for d in dice]
```

### Statistical Properties

#### Confidence Interval

For `n` iterations with estimated probability `p̂`, the 95% confidence interval is approximately:

```
p̂ ± 1.96 × √(p̂(1-p̂)/n)
```

**Example:** For n=10,000 and p̂=0.5:
```
CI = 0.5 ± 1.96 × √(0.5 × 0.5 / 10000) = 0.5 ± 0.0098
```

This gives a ±1% margin of error.

#### Accuracy vs. Iterations

| Iterations | Max Error (95% CI) |
|------------|-------------------|
| 1,000      | ±3.1%            |
| 10,000     | ±1.0%            |
| 100,000    | ±0.3%            |
| 1,000,000  | ±0.1%            |

### Bias from Greedy Heuristic

The Monte Carlo simulation uses a **greedy** heuristic rather than optimal play. This introduces systematic bias:

1. **Underestimation**: Greedy play is generally suboptimal, so estimated probabilities are lower than true optimal probabilities
2. **Bias magnitude**: Depends on objective complexity; simple objectives have minimal bias
3. **Mitigation**: Use analytic method for precise calculations

---

## Comparison of Methods

| Aspect | Analytic | Monte Carlo |
|--------|----------|-------------|
| **Accuracy** | Exact | Statistical estimate |
| **Optimality** | Assumes optimal play | Uses greedy heuristic |
| **Speed (5 dice)** | Fast (~10-50ms) | Medium (~100ms for 10K iterations) |
| **Speed (6-7 dice)** | Slower (~100-500ms) | Same (~100ms) |
| **Memory** | Higher (memoization) | Low (constant) |
| **Determinism** | Deterministic | Random (varies between runs) |

*Note: Speed estimates are approximate and depend on hardware and JIT compilation state.*

### When to Use Each Method

**Use Analytic when:**
- You need exact probabilities
- You want to know the best possible outcome
- Dice count is ≤ 5
- Making strategic decisions

**Use Monte Carlo when:**
- You want fast estimation
- Dice count is high (6-7)
- You want to understand "realistic" play with a greedy strategy
- Exact computation is too slow

---

## Mathematical Proofs

### Theorem 1: Histogram Equivalence

**Claim:** Two dice configurations with the same histogram have the same optimal probability.

**Proof:** 
The objective matching function only depends on the multiset of dice values, not their order. The keep strategy enumeration considers how many of each face to keep, not which specific dice. Therefore, any two configurations with the same histogram will have:
1. The same match/no-match status
2. The same set of possible keep strategies
3. The same distribution of outcomes after rerolling

Thus, `P(config1) = P(config2)` when `histogram(config1) = histogram(config2)`. ∎

### Theorem 2: Multiplicity Formula

**Claim:** The number of ordered dice configurations producing histogram `[c₁, ..., c₆]` is the multinomial coefficient `n! / (c₁! × ... × c₆!)`.

**Proof:**
This is the multinomial theorem. We count the number of ways to:
1. Place `n = Σcᵢ` dice into positions `1, 2, ..., n`
2. Subject to the constraint that exactly `cᵢ` dice show face `i`

This is equivalent to counting permutations of a multiset with `cᵢ` copies of symbol `i`. The formula is:
```
C(n; c₁, ..., c₆) = n! / (c₁! × ... × c₆!)
```
∎

### Theorem 3: Optimality of Keep Strategy

**Claim:** The algorithm finds the keep strategy that maximizes the probability of success.

**Proof:**
By strong induction on `rerollsLeft`:

**Base case (rerollsLeft = 0):** No keep decision matters; the objective is either met or not.

**Inductive step:** Assume the algorithm correctly computes optimal probabilities for all states with `rerollsLeft < k`. For a state with `rerollsLeft = k`:

1. The algorithm enumerates ALL possible keep strategies (exhaustive search over histogram keep choices)
2. For each strategy, it correctly computes the expected probability by:
   - Summing over all possible reroll outcomes
   - Weighting by their probabilities (multiplicities / total outcomes)
   - Recursively computing optimal continuation with `rerollsLeft - 1`
3. It selects the strategy with maximum expected probability

By the inductive hypothesis, the recursive calls return optimal values. The exhaustive search over strategies ensures optimality at the current level. ∎

---

## Implementation Notes

### Numerical Stability

1. **Factorial precomputation**: Factorials up to 10! are precomputed to avoid repeated computation
2. **Integer arithmetic**: Multinomial coefficients are computed using integer division to avoid floating-point errors
3. **Probability summation**: Probabilities are accumulated as doubles with sufficient precision for game-relevant calculations

### Performance Optimizations

1. **Histogram state**: Reduces state space from `6ⁿ` to `C(n+5,5)`
2. **Memoization**: Avoids recomputing identical subproblems
3. **Efficient key encoding**: Single 64-bit integer key for fast hash table lookup
4. **Strategy enumeration**: Histogram-based enumeration avoids redundant bitmask strategies

### Edge Cases

1. **Impossible objectives**: Return 0.0 (e.g., need 6 of a kind with only 5 dice)
2. **Already matched**: Return 1.0 (objective already achieved)
3. **No rerolls**: Probability equals match status of current dice (0 or 1)

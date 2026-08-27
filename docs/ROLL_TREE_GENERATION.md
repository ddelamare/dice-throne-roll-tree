# Generating Dice Throne roll trees

This document describes how to generate roll trees from the project’s hero data and probability engine. The goal is to make roll trees reusable for every hero, while keeping the generated recommendations deterministic, inspectable, and suitable for a future diagram UI.

The current reference implementations are Forgemaster, Headless Horseman, and Loki:

```powershell
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- rolltree `
  --hero forgemaster --dice-count 5 --rerolls 2
```

Headless Horseman is generated with the same command and schema:

```powershell
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- rolltree `
  --hero headlesshorseman --dice-count 5 --rerolls 2
```

Loki is generated with the same command:

```powershell
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- rolltree `
  --hero loki --dice-count 5 --rerolls 2
```

The checked-in [Headless Horseman roll tree](HEADLESS_HORSEMAN_ROLL_TREE.md) is the corresponding diagram and summary. Regenerate it from the command above whenever hero data, notation parsing, or evaluation rules change; the CLI output is the source of truth for the diagram.

The checked-in [Loki roll tree](LOKI_ROLL_TREE.md) adds a strategy overlay for straight chasing, fallback coverage, and Loki's resource/control choices.

The command writes one JSON document to stdout. It does not require the API server to be running.

## What a roll tree represents

A roll tree maps a current dice state and rolls remaining to:

1. the ability or strategic line with the highest configured expected value;
2. the dice to keep for the next roll;
3. the probability of completing that line;
4. a fallback line that remains available after making the primary keep decision;
5. the next-roll branches and their conditional probabilities.

The tree is a decision aid, not a complete game solver. It currently models dice objectives and configured outcome values. For Headless Horseman in particular, it does not model Dreadful count, Grim Pursuit spending, Haunted Head position, Terrorize timing, cards in hand, dice-modification cards, opponent choices, armor state, or matchup-specific strategy unless those are added to the evaluation model. The [Headless Horseman roll tree](HEADLESS_HORSEMAN_ROLL_TREE.md) therefore adds a clearly labeled strategy overlay instead of presenting raw objective ranking as a complete turn solver.

## Inputs

### Hero data

Hero objectives are loaded from:

```text
src/DiceThroneApi/Data/heroes/<hero-id>.json
```

Each objective supplies a name, notation, damage, and optional effects such as cards, tokens, or defense bypass. Adding a valid hero JSON file makes the hero available to the CLI and API hero services.

The notation parser currently supports:

- exact face groups, such as `[6666]`;
- allowed-value groups, such as `[(123)(123)(123)]`;
- `SmallStraight`;
- `LargeStraight`.

If a hero requires a new dice pattern, extend `DiceNotationParser`, `ObjectiveType`, and `ObjectiveMatcher`, then add parser and matcher tests before generating the tree.

### Roll configuration

The generator accepts:

| Input | Default | Meaning |
| --- | ---: | --- |
| `--hero` | `forgemaster` | Hero ID whose objectives are loaded. |
| `--dice-count` | `5` | Dice in the roll. |
| `--rerolls` | `2` | Rolls remaining after the current state. |
| `--start-dice` | none | Generate one tree entry for a specific current roll. |
| `--include-next-roll` | off | Expand second-roll branches for each generated entry. |

Use `--start-dice` with `--include-next-roll` when exploring one branch. Expanding every second-roll branch for every opening pattern is much larger than the compact first-roll tree.

Examples:

```powershell
# Full first-roll tree
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- rolltree `
  --hero forgemaster --dice-count 5 --rerolls 2

# One branch with conditional next-roll percentages
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- rolltree `
  --hero forgemaster --start-dice 1,2,6,6,6 `
  --rerolls 2 --include-next-roll
```

## Generation algorithm

### 1. Enumerate distinct current states

For five six-sided dice there are 252 distinct sorted histograms, rather than 7,776 ordered rolls. A state such as `1,2,6,6,6` represents every positional permutation of that histogram.

The generator uses the sorted form for tree keys and display. The `frequency` field retains the number of ordered rolls represented by the histogram, which is useful for unconditional first-roll percentages:

```text
frequency = 5! / (count(1)! × count(2)! × ... × count(6)!)
```

### 2. Score every objective

For each current state, the advisor asks `ProbabilityCalculator.CalculateBestKeep` for every objective. The calculator uses dynamic programming to choose the keep strategy that maximizes the probability of eventually matching that objective.

The result includes a positional keep mask. The tree converts that mask into `keepFaces`, which is easier for a human and a diagram renderer to consume.

### 3. Convert outcomes into expected value

The default expected-delta model is:

```text
damage
+ heal × healValue
+ cards × cardValue
+ CP × cpValue
+ token values
- enemyDefenseDelta, when the attack is defendable
```

The current defaults are:

| Value | Default |
| --- | ---: |
| Heal | 1 |
| Card | 3 |
| CP | 1 |
| Unspecified token | 2 |
| Defendable damage reduction | 3 |

Expected delta is calculated as:

```text
completion probability × objective delta
```

This means a lower-damage ability can outrank a higher-damage ability when it is much more likely to complete. The evaluation is intentionally configurable; different heroes may need different token values or resource assumptions.

### 4. Select and rank the primary line

The current ranking is:

1. highest expected delta;
2. highest completion probability as the tie-breaker;
3. highest opening-roll frequency as the final tie-breaker.

This is a value-first tree. It should not be presented as the only possible view. A future UI should also offer a reliability-first mode that ranks by the probability of completing any useful ability, especially for players who prioritize avoiding a missed offensive roll.

Already-completed abilities receive probability 1 and normally win immediately unless another completed ability has a higher evaluated effect.

### 5. Calculate fallbacks

The fallback is the best alternative damage objective after preserving the primary objective’s recommended keep mask. This answers questions such as:

```text
“If I keep these three 6s for Smelting Time, what useful line remains
if the next roll does not cooperate?”
```

Fallbacks are especially important for heroes with bridge abilities. For Forgemaster, A Good Haul often bridges a 6-based line and a mining/economy outcome.

### 6. Expand next-roll signals

With `--include-next-roll`, the generator rolls only the dice marked for reroll, evaluates the resulting state with one roll remaining, and groups equivalent sorted outcomes.

Each signal contains:

```json
{
  "dice": [1, 4, 6, 6, 6],
  "objective": "Smelting Time",
  "count": 2,
  "probability": 0.05555555555555555,
  "completionProbability": 1.0,
  "expectedDelta": 12.0
}
```

`count` is the number of ordered reroll outcomes represented by the sorted branch. The conditional branch probability is:

```text
count / 6^numberOfDiceRerolled
```

Branch probabilities for one parent node should sum to 1. This is a useful validation invariant.

## Forgemaster example

With `1,2,6,6,6` and two rerolls:

```text
primaryObjective       = Smelting Time
keepFaces              = 6,6,6
completionProbability  = 0.5177469136
expectedDelta          = 6.2129629630
fallbackObjective      = A Good Haul
```

The two rerolled dice produce these grouped signals:

| Next result | Conditional probability | Recommended line |
| --- | ---: | --- |
| Two additional 6s | 2.78% | Final Touches! |
| Exactly one additional 6 | 27.78% | Smelting Time |
| One 4/5 and one 1/2/3 | 33.33% | A Good Haul, complete |
| Two 4/5s | 11.11% | A Good Haul reassessment |
| Two 1/2/3s | 25.00% | A Good Haul reassessment |

These are conditional probabilities after the initial three 6s have already appeared. They must not be confused with the unconditional probability of opening with exactly three 6s and two non-6s.

## Headless Horseman example

The generated Headless Horseman tree contains 252 distinct five-dice opening patterns. Under the default value-first evaluation, the primary recommendations are Ride Down for 103 patterns, Reap for 50, Spectral Assault for 43, Cleave (5) for 21, Sow Despair for 18, Horrify for 14, Sow Despair (Large) for 2, and Dreadful Charge! for 1. See [HEADLESS_HORSEMAN_ROLL_TREE.md](HEADLESS_HORSEMAN_ROLL_TREE.md) for the Mermaid diagram and representative expanded branch.

## Output contract for future UI work

The current root object contains:

```json
{
  "command": "rolltree",
  "hero": "forgemaster",
  "diceCount": 5,
  "rollsRemaining": 2,
  "evaluation": {},
  "priorityDefinition": "...",
  "entries": []
}
```

Each entry contains:

```json
{
  "priority": 1,
  "dice": [1, 2, 6, 6, 6],
  "frequency": 10,
  "rerollCount": 2,
  "keepFaces": [6, 6, 6],
  "primaryObjective": "Smelting Time",
  "completionProbability": 0.5177469135802468,
  "expectedDelta": 6.212962962962962,
  "fallbackObjective": "A Good Haul",
  "nextRollSignals": []
}
```

The diagram renderer should use `dice` as the node key, `keepFaces` and `primaryObjective` as the action label, and `nextRollSignals` as outgoing edges. It should display percentages from `probability` on edges and reserve `frequency / 6^diceCount` for optional first-roll likelihood labels.

## Making this work for another hero

Use this workflow:

1. Confirm the hero’s objectives in the hero JSON file.
2. Run `heroes` to verify the hero ID and parsed objective names.
3. Run `advice` on representative rolls for each objective family.
4. Run `rolltree --hero <id>` and inspect the distribution of primary objectives. For the built-in reference heroes, use `--hero forgemaster` or `--hero headlesshorseman`.
5. Identify surprising states and compare them with custom evaluation values.
6. Add or update notation/matcher tests if the hero uses a new pattern.
7. Add hero-specific value overrides only when the default evaluation is demonstrably misleading.
8. Generate targeted branches with `--start-dice ... --include-next-roll`.
9. Add a compact hero-specific diagram or summary document only after verifying the generated JSON.

Useful commands:

```powershell
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- heroes
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- advice `
  --hero <hero-id> --dice 1,2,3,4,5 --rerolls 2
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- compare `
  --dice 1,2,6,6,6 --rerolls 2 --hero <hero-id>
```

## Validation checklist

Before using a generated tree in the website:

- Build the CLI project successfully.
- Confirm every generated entry has the requested dice count.
- Confirm each keep decision has one face per retained die and the reroll count is consistent.
- Confirm probabilities are between 0 and 1.
- Confirm every expanded parent’s branch probabilities sum to 1 within floating-point tolerance.
- Check that a completed objective has completion probability 1.
- Compare a few results against direct `advice` calls.
- Review high-value, low-probability branches manually; these are the most likely places where a user may prefer a reliability-first alternative.
- Run the full test project and distinguish new failures from known advisor-test failures.

## Known limitations and planned extensions

The current generator is intentionally dice-centric. The next useful extensions are:

- a reliability-first objective ranking;
- a configurable “any ability” success probability;
- hero-specific resource valuation, such as Ore, CP, or armor state;
- card and dice-mod assumptions;
- compact pattern grouping for the UI, so equivalent histograms share a visual rule;
- persisted generated JSON for fast website loading;
- UI filters for value-first versus reliability-first recommendations;
- multi-stage expansion beyond the next roll when a branch remains unresolved.

Keep the raw generated tree as the source of truth. Hand-authored diagrams should summarize or label it, not replace the underlying calculation.

# Agent Help: Dice Throne Roll Tree

## What this project is

This is a .NET 8 ASP.NET Core Web API that recommends which dice to keep in a Dice Throne turn. It also serves a vanilla JavaScript web UI from `src/DiceThroneApi/wwwroot`.

The main flow is:

1. `RollController` receives a hero, dice state, roll count, and calculation method.
2. `HeroService` loads hero/objective definitions from `src/DiceThroneApi/Data/heroes/*.json`.
3. `DiceNotationParser` turns objective notation into typed objectives.
4. `ObjectiveMatcher` checks completed objectives.
5. `ProbabilityCalculator` computes exact probabilities with dynamic programming and optimal keep decisions.
6. `MonteCarloSimulator` provides simulation-based estimates.
7. `DiceRollAdvisor` ranks objective/keep choices using probability and configured evaluation values.

`Program.cs` registers these services as singletons. `TelemetryService` is also a singleton and keeps development telemetry in memory or in its flat-file fallback, depending on the environment and filesystem availability.

## Important files

- `src/DiceThroneApi/Program.cs` - dependency injection, middleware, Swagger, static files, and route setup.
- `src/DiceThroneApi/Controllers/RollController.cs` - simulation, probability, pre-roll, advice, and set-dice endpoints.
- `src/DiceThroneApi/Controllers/HeroesController.cs` - hero listing and lookup.
- `src/DiceThroneApi/Services/ProbabilityCalculator.cs` - exact probability and optimal strategy logic.
- `src/DiceThroneApi/Services/DiceRollAdvisor.cs` - recommendation ranking and expected-value scoring.
- `src/DiceThroneApi/Services/DiceNotationParser.cs` - supported objective notation.
- `src/DiceThroneApi/Data/heroes/` - runtime hero data; adding a valid JSON file adds a hero.
- `src/DiceThroneApi/wwwroot/index.html` and `wwwroot/js/` - browser UI.
- `tests/DiceThroneApi.Tests/` - xUnit unit/controller tests.
- `docs/PROBABILITY_MATH.md` - probability algorithm details.
- `docs/DICE_SUGGESTION_ANALYSIS.md` - advisor algorithm details.
- `src/DiceThroneCli/Program.cs` - agent-facing CLI commands and JSON output contract.

## Build, test, and run

Use the project files directly with the installed .NET 8 SDK:

```powershell
dotnet build src\DiceThroneApi\DiceThroneApi.csproj
dotnet test tests\DiceThroneApi.Tests\DiceThroneApi.Tests.csproj
dotnet run --project src\DiceThroneApi\DiceThroneApi.csproj
```

## Agent CLI for probability and strategy questions

Use the CLI for agent calculations instead of reimplementing probability math in prompts, scripts, or ad hoc code. It reuses the API's `ProbabilityCalculator`, `DiceRollAdvisor`, `MonteCarloSimulator`, notation parser, and hero data without requiring the web server to be running.

Run commands from the repository root:

```powershell
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- probability --notation '[6666]' --dice-count 5 --rerolls 2
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- advice --dice 6,6,1,2,3 --rerolls 2 --hero barbarian
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- compare --dice 6,6,1,2,3 --rerolls 2 `
  --objective 'Attack|[6666]|4' --objective 'Big Attack|[66666]|10'
dotnet run --project src\DiceThroneCli\DiceThroneCli.csproj -- heroes
```

The CLI is intended for machine consumption:

- Successful commands emit one indented JSON document on stdout and exit `0`.
- Invalid input emits `{ "error": "..." }` on stderr and exits `2`.
- Keep stdout reserved for the result; do not parse human-readable help as data.
- Use `--help` for the command summary.

### CLI commands

`probability` calculates the chance of completing one objective from a fresh roll with optimal reroll decisions. Required input is `--notation`; defaults are `--dice-count 5` and `--rerolls 2`. Use `--method analytic` for the exact dynamic-programming result. Use `--method montecarlo --iterations 10000` for a stochastic estimate.

`advice` evaluates the supplied current dice against every objective. Provide either `--hero <id>` to load objectives from `src/DiceThroneApi/Data/heroes/`, or one or more custom objectives using `--objective 'name|notation|damage'`. The output includes the optimal `diceToKeep` boolean mask, probability, baseline probability from rerolling all unlocked dice, probability improvement, expected delta, and any fallback objective.

`compare` has the same objective inputs as `advice`, but requires at least two objectives. It returns all ranked strategies plus `best`, the objective with the highest analytic expected delta. Ranking uses expected delta first and probability as the tie-breaker.

`heroes` lists all bundled hero IDs and their objective definitions. Use this to discover valid `--hero` values before asking for advice.

### Objective notation and interpretation

- `[6666]` means four dice assigned to face 6; additional dice may be irrelevant.
- `[(123)(123)(123)]` means three dice, each allowed to show 1, 2, or 3.
- `SmallStraight` and `LargeStraight` are built-in objective types.
- Custom objective syntax is `name|notation|damage`; omit the damage field for non-damage objectives.
- Dice input is a comma-separated list such as `--dice 6,6,1,2,3`; each value must be 1 through 6.
- Supported CLI inputs are 1-7 dice and 0-7 remaining rerolls.

Probability is the chance of eventually matching the objective when the calculator chooses the best keep strategy at every state. It is not the probability of the current dice already matching. `diceToKeep` is positional and has one boolean per supplied die. `true` means keep that die for the next reroll; `false` means reroll it.

Expected delta converts outcomes into a common decision value. By default, damage is reduced by enemy defense (`3`), healing is worth `1`, cards `3`, CP `1`, and an unspecified token `2`. Override these with `--enemy-defense`, `--heal-value`, `--card-value`, `--cp-value`, and `--token-value` when the problem requires a different valuation. A negative or zero expected delta can be intentional when defense outweighs the objective's damage.

Hero JSON may include a `tokenValues` object, such as `{ "Stun": 4 }`. These values override the default token value for that hero. They are applied when a hero is selected/loaded, while explicitly supplied `EvaluationConfig.TokenValues` entries take precedence. The browser resets token overrides to the selected hero's defaults; the API, CLI, and MCP flows merge hero defaults in the same way.

When comparing strategies, prefer `expectedDelta` when the task asks what to pursue. Prefer `probability` when the task asks for the safest way to complete a specific objective. `baselineProbability` is the all-reroll reference, while `probabilityImprovement` measures the value of the recommended keep decision over that baseline.

### Recommended agent workflow

1. Identify the number of dice and remaining rerolls; use the standard 5 dice / 2 rerolls only when the task does not specify otherwise.
2. Use `heroes` or inspect hero JSON to obtain the exact objective notation and effects.
3. Run `advice` for one objective set or `compare` for competing lines.
4. Read the JSON fields rather than relying on rounded display values.
5. For a probability claim, report whether it is analytic or Monte Carlo and preserve enough precision to distinguish close strategies.
6. If a result seems surprising, rerun with custom objectives and compare the returned keep mask and baseline; then inspect `docs/PROBABILITY_MATH.md` and `docs/DICE_SUGGESTION_ANALYSIS.md` before changing production logic.

The normal development URL is `http://localhost:5022`. The root URL serves the UI. Swagger is available at `/swagger` only when the environment is `Development`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src\DiceThroneApi\DiceThroneApi.csproj
```

For a clean verification, restore first if `obj/project.assets.json` is missing:

```powershell
dotnet restore tests\DiceThroneApi.Tests\DiceThroneApi.Tests.csproj
```

The checked-in `DiceThroneRollTree.slnx` is not understood by the currently installed .NET SDK 8.0.301 (`MSB4068`). Do not use it as the build command on this machine; build/test the `.csproj` files above, or use an SDK that supports the `.slnx` format.

## API smoke checks

With the app running:

```powershell
Invoke-WebRequest http://localhost:5022/
Invoke-WebRequest http://localhost:5022/api/heroes

$body = '{"notation":"[6666]","diceCount":5,"method":"analytic"}'
Invoke-WebRequest -Method Post -Uri http://localhost:5022/api/roll/probability `
  -ContentType 'application/json' -Body $body
```

Important routes:

- `GET /api/heroes`
- `GET /api/heroes/{id}`
- `POST /api/roll/simulate`
- `POST /api/roll/setdice`
- `POST /api/roll/probability`
- `POST /api/roll/preroll`
- `POST /api/roll/advice`
- `GET /api/telemetry`
- `POST /api/telemetry/visit`

## Domain notes

- Standard turns use five dice and up to two re-rolls by default, but the API supports configurable dice counts.
- Supported notation includes exact face groups such as `[6666]`, grouped allowed values such as `[(123)(123)(123)]`, `SmallStraight`, and `LargeStraight`.
- `method` is normally `analytic`; `montecarlo` uses the simulator where supported.
- Psylocke has a special manifest die. `RollController` locks it at index 0 and includes it in the total dice count.
- Hero data is case-insensitively looked up by `id`. Keep JSON property names and objective notation consistent with existing files.
- The README's three-hero description is stale: the current data directory contains 25 hero files.

## Current verification status

As of 2026-08-25 on Windows with .NET SDK 8.0.301:

- `dotnet build src\DiceThroneApi\DiceThroneApi.csproj` succeeds with 0 warnings and 0 errors.
- The app starts and serves the UI, hero list, and analytic probability endpoint successfully.
- The test suite runs 76 tests: 74 pass and 2 fail in `DiceRollAdvisorTests`.
- The failing tests are `GetAdvice_ExpectedDelta_IncludesHealAndCards` (expected 4, actual 6) and `GetAdvice_WithMultipleDamageObjectives_PopulatesFallback` (expected a non-null fallback, received null). Investigate advisor scoring/default evaluation semantics before changing production behavior.

Do not treat the two known test failures as resolved without first deciding whether the tests or the current advisor behavior represent the intended contract.

## Change guidance for agents

- Preserve user changes in a dirty worktree; inspect `git status` before editing.
- Prefer focused service/unit tests for probability or advisor changes, then run the full test project.
- When changing hero JSON, validate that the file loads at startup and add/adjust parser or controller coverage as needed.
- Keep static UI changes in `wwwroot`; no frontend build step is required.
- Avoid changing telemetry behavior while working on probability/advisor logic unless the task explicitly concerns telemetry.

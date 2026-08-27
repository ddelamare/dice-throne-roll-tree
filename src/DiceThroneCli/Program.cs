using System.Text.Json;
using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace DiceThroneCli;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = CliOptions.Parse(args[1..]);
            var parser = new DiceNotationParser();
            var calculator = new ProbabilityCalculator(new ObjectiveMatcher());
            var simulator = new MonteCarloSimulator(new ObjectiveMatcher(), calculator);
            var advisor = new DiceRollAdvisor(calculator, simulator);

            object result = command switch
            {
                "probability" or "prob" => CalculateProbability(options, parser, calculator, simulator),
                "advice" => await GetAdvice(options, parser, advisor),
                "compare" => await Compare(options, parser, advisor),
                "rolltree" => await BuildRollTree(options, parser, advisor),
                "heroes" => await ListHeroes(parser),
                _ => throw new CliException($"Unknown command '{command}'. Use 'help' for usage.")
            };

            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return 0;
        }
        catch (Exception ex) when (ex is CliException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, Json));
            return 2;
        }
    }

    private static object CalculateProbability(CliOptions o, DiceNotationParser parser, ProbabilityCalculator calculator, MonteCarloSimulator simulator)
    {
        var notation = o.Required("notation");
        var dice = o.Int("dice-count", 5);
        var rerolls = o.Int("rerolls", 2);
        ValidateRollInputs(dice, rerolls);
        var method = o.String("method", "analytic");
        ValidateMethod(method);
        var objective = parser.Parse("Custom", notation);
        var probability = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase)
            ? simulator.Simulate(objective, dice, o.Int("iterations", 1000), rerolls)
            : calculator.Calculate(objective, dice, rerolls: rerolls);

        return new { command = "probability", notation, diceCount = dice, rerolls, method, probability };
    }

    private static async Task<object> GetAdvice(CliOptions o, DiceNotationParser parser, DiceRollAdvisor advisor)
    {
        var dice = ParseDice(o.Required("dice"));
        ValidateRollInputs(dice.Count, o.Int("rerolls", 2));
        ValidateMethod(o.String("method", "analytic"));
        var objectives = await LoadObjectives(o, parser);
        var advice = advisor.GetAdvice(dice, o.Int("rerolls", 2), objectives, o.String("method", "analytic"), eval: ParseEvaluation(o));
        return new { command = "advice", dice, rollsRemaining = o.Int("rerolls", 2), advice };
    }

    private static async Task<object> Compare(CliOptions o, DiceNotationParser parser, DiceRollAdvisor advisor)
    {
        var dice = ParseDice(o.Required("dice"));
        ValidateRollInputs(dice.Count, o.Int("rerolls", 2));
        ValidateMethod(o.String("method", "analytic"));
        var objectives = await LoadObjectives(o, parser);
        if (objectives.Count < 2)
            throw new CliException("compare requires at least two --objective values.");

        var rolls = o.Int("rerolls", 2);
        var advice = advisor.GetAdvice(dice, rolls, objectives, o.String("method", "analytic"), eval: ParseEvaluation(o));
        var best = advisor.GetBestOverallStrategy(dice, rolls, objectives, ParseEvaluation(o));
        return new { command = "compare", dice, rollsRemaining = rolls, best, strategies = advice };
    }

    private static async Task<object> BuildRollTree(CliOptions o, DiceNotationParser parser, DiceRollAdvisor advisor)
    {
        var diceCount = o.Int("dice-count", 5);
        var rolls = o.Int("rerolls", 2);
        ValidateRollInputs(diceCount, rolls);
        if (rolls < 2)
            throw new CliException("rolltree requires at least 2 rolls remaining.");

        var heroId = o.String("hero", "forgemaster");
        var objectives = await LoadObjectives(CliOptions.Parse(new[] { "--hero", heroId }), parser);
        var eval = ParseEvaluation(o);
        var entries = new List<RollTreeEntry>();

        var startingDice = o.Has("start-dice")
            ? new[] { ParseDice(o.Required("start-dice")) }
            : GenerateDistinctOrderedDice(diceCount).ToArray();
        if (startingDice.Any(dice => dice.Count != diceCount))
            throw new CliException("--start-dice must contain exactly --dice-count values.");

        foreach (var dice in startingDice)
        {
            var best = advisor.GetAdvice(dice, rolls, objectives, eval: eval).FirstOrDefault()
                ?? throw new InvalidOperationException("No objective advice was found.");

            var rerollCount = best.DiceToKeep.Count(keep => !keep);
            entries.Add(new RollTreeEntry
            {
                Dice = dice,
                Frequency = MultinomialFrequency(dice),
                RerollCount = rerollCount,
                KeepFaces = dice.Zip(best.DiceToKeep, (face, keep) => (face, keep))
                    .Where(x => x.keep).Select(x => x.face).ToList(),
                PrimaryObjective = best.ObjectiveName,
                CompletionProbability = best.Probability,
                ExpectedDelta = best.ExpectedDelta,
                FallbackObjective = best.FallbackObjectiveName,
                NextRollSignals = o.Has("include-next-roll")
                    ? BuildNextRollSignals(dice, best.DiceToKeep, objectives, advisor, eval)
                    : new()
            });
        }

        var ordered = entries
            .OrderByDescending(e => e.ExpectedDelta)
            .ThenByDescending(e => e.CompletionProbability)
            .ThenByDescending(e => e.Frequency)
            .Select((entry, index) => { entry.Priority = index + 1; return entry; })
            .ToList();

        return new
        {
            command = "rolltree",
            hero = heroId,
            diceCount,
            rollsRemaining = rolls,
            evaluation = eval,
            priorityDefinition = "Expected delta first, completion probability second, opening-roll frequency third.",
            entries = ordered
        };
    }

    private static List<RollTreeSignal> BuildNextRollSignals(
        List<int> dice,
        List<bool> keep,
        List<RollObjective> objectives,
        DiceRollAdvisor advisor,
        EvaluationConfig eval)
    {
        var kept = dice.Zip(keep, (face, locked) => (face, locked))
            .Where(x => x.locked).Select(x => x.face).ToList();
        var rerollCount = dice.Count - kept.Count;
        if (rerollCount == 0)
            return new();

        var groups = new Dictionary<string, RollTreeSignal>(StringComparer.Ordinal);
        foreach (var reroll in GenerateDistinctOrderedDice(rerollCount))
        {
            var nextDice = kept.Concat(reroll).OrderBy(x => x).ToList();
            var next = advisor.GetBestOverallStrategy(nextDice, 1, objectives, eval);
            if (next == null) continue;
            var key = string.Join(",", nextDice);
            if (!groups.TryGetValue(key, out var signal))
            {
                signal = new RollTreeSignal { Dice = nextDice, Objective = next.ObjectiveName };
                groups[key] = signal;
            }
            signal.Count += checked((int)MultinomialFrequency(reroll));
            signal.Probability = signal.Count / Math.Pow(6, rerollCount);
            signal.ExpectedDelta = next.ExpectedDelta;
            signal.CompletionProbability = next.Probability;
        }

        // Equal outcomes from distinct ordered rerolls are one visual branch.
        return groups.Values
            .OrderByDescending(s => s.ExpectedDelta)
            .ThenByDescending(s => s.CompletionProbability)
            .ThenBy(s => string.Join(",", s.Dice))
            .ToList();
    }

    private static IEnumerable<List<int>> GenerateDistinctOrderedDice(int count)
    {
        var values = new int[count];
        return Generate(0, 1);

        IEnumerable<List<int>> Generate(int index, int minimumFace)
        {
            if (index == values.Length)
            {
                yield return values.OrderBy(x => x).ToList();
                yield break;
            }
            for (var face = minimumFace; face <= 6; face++)
            {
                values[index] = face;
                foreach (var result in Generate(index + 1, face)) yield return result;
            }
        }
    }

    private static long MultinomialFrequency(List<int> dice)
    {
        var factorial = new[] { 1L, 1L, 2L, 6L, 24L, 120L, 720L, 5040L };
        var result = factorial[dice.Count];
        foreach (var count in dice.GroupBy(x => x).Select(g => g.Count())) result /= factorial[count];
        return result;
    }

    private static async Task<object> ListHeroes(DiceNotationParser parser)
    {
        var service = new HeroService(new CliEnvironment(AppContext.BaseDirectory), parser);
        var heroes = await service.GetAllHeroesAsync();
        return new { command = "heroes", heroes = heroes.Select(h => new { h.Id, h.Name, objectives = h.Objectives.Select(o => new { o.Name, o.Notation, o.Damage, o.Heal, o.Cards, o.Cp, o.Tokens }) }) };
    }

    private static async Task<List<RollObjective>> LoadObjectives(CliOptions o, DiceNotationParser parser)
    {
        if (o.Has("hero"))
        {
            var service = new HeroService(new CliEnvironment(AppContext.BaseDirectory), parser);
            var hero = await service.GetHeroByIdAsync(o.Required("hero"));
            if (hero == null) throw new CliException($"Hero '{o.Required("hero")}' was not found.");
            return hero.Objectives;
        }

        var values = o.All("objective");
        if (values.Count == 0)
            throw new CliException("Provide --hero <id> or one or more --objective 'name|notation|damage' values.");

        return values.Select((value, index) =>
        {
            var parts = value.Split('|');
            if (parts.Length < 2 || parts.Length > 3)
                throw new CliException($"Invalid objective '{value}'. Expected name|notation|damage.");
            var objective = parser.Parse(parts[0], parts[1]);
            if (parts.Length == 3)
            {
                if (!int.TryParse(parts[2], out var damage))
                    throw new CliException($"Invalid damage in objective '{value}'.");
                objective.Damage = damage;
            }
            return objective;
        }).ToList();
    }

    private static EvaluationConfig ParseEvaluation(CliOptions o) => new()
    {
        HealValue = o.Double("heal-value", 1),
        CardValue = o.Double("card-value", 3),
        CpValue = o.Double("cp-value", 1),
        DefaultTokenValue = o.Double("token-value", 2),
        EnemyDefenseDelta = o.Double("enemy-defense", 3)
    };

    private static void ValidateRollInputs(int diceCount, int rerolls)
    {
        if (diceCount is < 1 or > 7)
            throw new CliException("Dice count must be between 1 and 7.");
        if (rerolls is < 0 or > 7)
            throw new CliException("Rerolls must be between 0 and 7.");
    }

    private static void ValidateMethod(string method)
    {
        if (!method.Equals("analytic", StringComparison.OrdinalIgnoreCase) &&
            !method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase))
            throw new CliException("Method must be 'analytic' or 'montecarlo'.");
    }

    private static List<int> ParseDice(string value)
    {
        var dice = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var die) ? die : 0).ToList();
        if (dice.Count == 0 || dice.Any(d => d is < 1 or > 6))
            throw new CliException("--dice must be a comma-separated list of values from 1 through 6.");
        return dice;
    }

    private static void PrintHelp() => Console.WriteLine(@"dice-throne: agent-facing probability and roll-strategy CLI

Commands:
  probability --notation [6666] --dice-count 5 --rerolls 2 [--method analytic|montecarlo]
  advice --dice 6,6,1,2,3 --rerolls 2 --hero barbarian
  advice --dice 6,6,1,2,3 --rerolls 2 --objective ""Attack|[6666]|4""
  compare --dice 6,6,1,2,3 --rerolls 2 --objective ""Attack|[6666]|4"" --objective ""Big|[66666]|10""
  rolltree --hero forgemaster --dice-count 5 --rerolls 2 [--include-next-roll] [--start-dice 6,6,6,1,2]
  heroes

All successful commands emit JSON on stdout. Errors emit a JSON error on stderr and exit 2.
");
}

internal sealed class RollTreeEntry
{
    public int Priority { get; set; }
    public List<int> Dice { get; set; } = new();
    public long Frequency { get; set; }
    public int RerollCount { get; set; }
    public List<int> KeepFaces { get; set; } = new();
    public string PrimaryObjective { get; set; } = string.Empty;
    public double CompletionProbability { get; set; }
    public double ExpectedDelta { get; set; }
    public string? FallbackObjective { get; set; }
    public List<RollTreeSignal> NextRollSignals { get; set; } = new();
}

internal sealed class RollTreeSignal
{
    public List<int> Dice { get; set; } = new();
    public string Objective { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Probability { get; set; }
    public double CompletionProbability { get; set; }
    public double ExpectedDelta { get; set; }
}

internal sealed class CliOptions
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (!raw.StartsWith("--")) throw new CliException($"Unexpected argument '{raw}'.");
            var pair = raw[2..].Split('=', 2);
            var key = pair[0];
            var value = pair.Length == 2 ? pair[1] : (i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true");
            result.values.TryAdd(key, new List<string>());
            result.values[key].Add(value);
        }
        return result;
    }
    public bool Has(string key) => values.ContainsKey(key);
    public string Required(string key) => String(key, null) ?? throw new CliException($"Missing --{key}.");
    public string String(string key, string? fallback) => values.TryGetValue(key, out var v) ? v[^1] : fallback!;
    public List<string> All(string key) => values.TryGetValue(key, out var v) ? v : new();
    public int Int(string key, int fallback)
    {
        var raw = String(key, null);
        if (raw == null) return fallback;
        if (int.TryParse(raw, out var value)) return value;
        throw new CliException($"--{key} must be an integer.");
    }
    public double Double(string key, double fallback)
    {
        var raw = String(key, null);
        if (raw == null) return fallback;
        if (double.TryParse(raw, out var value)) return value;
        throw new CliException($"--{key} must be a number.");
    }
}

internal sealed class CliException(string message) : Exception(message);

internal sealed class CliEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "DiceThroneCli";
    public string EnvironmentName { get; set; } = "Production";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

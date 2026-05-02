using Microsoft.AspNetCore.Mvc;
using DiceThroneApi.Models;
using DiceThroneApi.Services;

namespace DiceThroneApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RollController : ControllerBase
{
    private readonly HeroService _heroService;
    private readonly DiceRollAdvisor _advisor;
    private readonly ProbabilityCalculator _calculator;
    private readonly MonteCarloSimulator _simulator;
    private readonly DiceNotationParser _parser;

    public RollController(
        HeroService heroService,
        DiceRollAdvisor advisor,
        ProbabilityCalculator calculator,
        MonteCarloSimulator simulator,
        DiceNotationParser parser)
    {
        _heroService = heroService;
        _advisor = advisor;
        _calculator = calculator;
        _simulator = simulator;
        _parser = parser;
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request)
    {
        var hero = await _heroService.GetHeroByIdAsync(request.HeroId);
        if (hero == null)
        {
            return NotFound("Hero not found");
        }

        var hasManifestDie = HasManifestDie(hero.Id);
        var totalDiceToRoll = request.DiceCount + (hasManifestDie ? 1 : 0);
        var dice = request.CurrentDice ?? RollDice(totalDiceToRoll);
        var rollsRemaining = request.RollsRemaining ?? 2;
        var lockedDiceMask = BuildLockedDiceMask(dice.Count, hasManifestDie);

        var evaluation = request.Evaluation ?? new DiceThroneApi.Models.EvaluationConfig();
        var suggestions = _advisor.GetAdvice(dice, rollsRemaining, hero.Objectives, request.Method ?? "analytic", lockedDiceMask, evaluation);

        return Ok(new
        {
            dice,
            rollsRemaining,
            suggestions,
            hasManifestDie,
            manifestDieIndex = hasManifestDie ? 0 : -1
        });
    }

    [HttpPost("setdice")]
    public async Task<IActionResult> SetDice([FromBody] SetDiceRequest request)
    {
        var hero = await _heroService.GetHeroByIdAsync(request.HeroId);
        if (hero == null)
        {
            return NotFound("Hero not found");
        }

        if (request.CurrentDice == null || request.CurrentDice.Count == 0)
        {
            return BadRequest("CurrentDice must be provided with at least one value.");
        }

        var hasManifestDie = HasManifestDie(hero.Id);
        var dice = request.CurrentDice;
        var rollsRemaining = request.RollsRemaining ?? 2;
        var lockedDiceMask = BuildLockedDiceMask(dice.Count, hasManifestDie);

        var evaluation = request.Evaluation ?? new DiceThroneApi.Models.EvaluationConfig();
        var suggestions = _advisor.GetAdvice(dice, rollsRemaining, hero.Objectives, request.Method ?? "analytic", lockedDiceMask, evaluation);

        return Ok(new
        {
            dice,
            rollsRemaining,
            suggestions,
            hasManifestDie,
            manifestDieIndex = hasManifestDie ? 0 : -1
        });
    }

    [HttpPost("probability")]
    public IActionResult CalculateProbability([FromBody] ProbabilityRequest request)
    {
        try
        {
            var objective = _parser.Parse("Custom", request.Notation);
            double probability;

            if (request.Method?.Equals("montecarlo", StringComparison.OrdinalIgnoreCase) == true)
            {
                probability = _simulator.Simulate(objective, request.DiceCount, MonteCarloConst.StandardIterations);
            }
            else
            {
                probability = _calculator.Calculate(objective, request.DiceCount);
            }

            return Ok(new
            {
                probability,
                method = request.Method ?? "analytic"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("preroll")]
    public async Task<IActionResult> GetPreRollAdvice([FromBody] PreRollAdviceRequest request)
    {
        var hero = await _heroService.GetHeroByIdAsync(request.HeroId);
        if (hero == null)
        {
            return NotFound("Hero not found");
        }

        var hasManifestDie = HasManifestDie(hero.Id);
        var totalDice = request.DiceCount + (hasManifestDie ? 1 : 0);
        var lockedDiceMask = BuildLockedDiceMask(totalDice, hasManifestDie);
        var requestedMethod = request.Method ?? "analytic";
        var useMonteCarlo = requestedMethod.Equals("montecarlo", StringComparison.OrdinalIgnoreCase) && !hasManifestDie;
        var calculationMethod = useMonteCarlo ? "Monte Carlo" : "Analytic";

        var advice = hero.Objectives
            .Select((objective, index) =>
            {
                var evaluation = request.Evaluation ?? new DiceThroneApi.Models.EvaluationConfig();
                var probability = useMonteCarlo
                    ? _simulator.Simulate(objective, totalDice, MonteCarloConst.StandardIterations)
                    : _calculator.CalculatePreRoll(objective, totalDice, lockedDiceMask);

                double delta = objective.Damage;
                delta += objective.Heal * evaluation.HealValue;
                delta += objective.Cards * evaluation.CardValue;
                delta += objective.Cp * evaluation.CpValue;
                foreach (var token in objective.Tokens ?? new List<string>())
                {
                    if (evaluation.TokenValues != null && evaluation.TokenValues.TryGetValue(token, out var val))
                        delta += val;
                    else
                        delta += evaluation.DefaultTokenValue;
                }

                return new RollAdvice
                {
                    ObjectiveName = objective.Name,
                    ObjectiveNotation = objective.Notation,
                    Probability = probability,
                    CalculationMethod = calculationMethod,
                    Damage = objective.Damage,
                    ExpectedDelta = probability * delta,
                    Index = index
                };
            })
.OrderByDescending(a => a.ExpectedDelta)
            .ThenByDescending(a => a.Probability)
            .ToList();

        return Ok(advice);
    }

    [HttpPost("advice")]
    public async Task<IActionResult> GetAdvice([FromBody] AdviceRequest request)
    {
        var hero = await _heroService.GetHeroByIdAsync(request.HeroId);
        if (hero == null)
        {
            return NotFound("Hero not found");
        }

        var lockedDiceMask = BuildLockedDiceMask(request.CurrentDice.Count, HasManifestDie(hero.Id));
        var evaluation = request.Evaluation ?? new DiceThroneApi.Models.EvaluationConfig();

        var advice = _advisor.GetAdvice(request.CurrentDice, request.RollsRemaining, hero.Objectives, request.Method ?? "analytic", lockedDiceMask, evaluation);

        return Ok(advice);
    }

    private static bool HasManifestDie(string heroId)
    {
        return heroId.Equals("psylocke", StringComparison.OrdinalIgnoreCase);
    }

    private static List<bool> BuildLockedDiceMask(int diceCount, bool hasManifestDie)
    {
        var lockedDiceMask = Enumerable.Repeat(false, diceCount).ToList();
        if (hasManifestDie && diceCount > 0)
        {
            lockedDiceMask[0] = true;
        }

        return lockedDiceMask;
    }

    private List<int> RollDice(int count)
    {
        var result = new List<int>();
        for (int i = 0; i < count; i++)
        {
            result.Add(Random.Shared.Next(1, 7));
        }
        return result;
    }
}

public class SimulateRequest
{
    public string HeroId { get; set; } = string.Empty;
    public int DiceCount { get; set; } = 5;
    public List<int>? CurrentDice { get; set; }
    public int? RollsRemaining { get; set; }
    public string? Method { get; set; }
    public DiceThroneApi.Models.EvaluationConfig? Evaluation { get; set; }
}

public class ProbabilityRequest
{
    public string Notation { get; set; } = string.Empty;
    public int DiceCount { get; set; } = 5;
    public string? Method { get; set; }
    public DiceThroneApi.Models.EvaluationConfig? Evaluation { get; set; }
}

public class AdviceRequest
{
    public string HeroId { get; set; } = string.Empty;
    public List<int> CurrentDice { get; set; } = new();
    public int RollsRemaining { get; set; }
    public string? Method { get; set; }
    public DiceThroneApi.Models.EvaluationConfig? Evaluation { get; set; }
}

public class SetDiceRequest
{
    public string HeroId { get; set; } = string.Empty;
    public List<int> CurrentDice { get; set; } = new();
    public int? RollsRemaining { get; set; }
    public string? Method { get; set; }
    public DiceThroneApi.Models.EvaluationConfig? Evaluation { get; set; }
}

public class PreRollAdviceRequest
{
    public string HeroId { get; set; } = string.Empty;
    public int DiceCount { get; set; } = 5;
    public string? Method { get; set; }
    public DiceThroneApi.Models.EvaluationConfig? Evaluation { get; set; }
}

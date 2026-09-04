using System.ComponentModel;
using DiceThroneApi.Models;
using DiceThroneApi.Services;
using ModelContextProtocol.Server;

namespace DiceThroneApi.Mcp;

/// <summary>
/// Agent-facing tools for the same roll operations exposed by the web API.
/// The HTTP MCP transport is stateless, so each call contains its complete input state.
/// </summary>
[McpServerToolType]
public sealed class DiceThroneMcpTools
{
    [McpServerTool(Name = "create_deck", UseStructuredContent = true)]
    [Description("Create a tracked deck from cards. Each card has a name, cost, playPhase, and effect. The returned deckId is required for every later deck operation. Card order is preserved until shuffled.")]
    public static DeckSnapshot CreateDeck(DeckService decks, [Description("Cards to add to the deck. Example: { name: 'So Wild', cost: 2, playPhase: 'Any', effect: 'Change any 1 die value' }.")] List<Card> cards)
        => decks.Create(cards);

    [McpServerTool(Name = "shuffle_deck", UseStructuredContent = true)]
    [Description("Shuffle a deck. Shuffling hides and invalidates all previously revealed top cards.")]
    public static DeckSnapshot ShuffleDeck(DeckService decks, string deckId) => decks.Shuffle(deckId);

    [McpServerTool(Name = "draw_cards", UseStructuredContent = true)]
    [Description("Draw cards from a deck. Drawn cards are returned and placed in a pending-draw zone until put into hand or discard.")]
    public static IReadOnlyList<Card> DrawCards(DeckService decks, string deckId, int count = 1) => decks.Draw(deckId, count);

    [McpServerTool(Name = "put_cards", UseStructuredContent = true)]
    [Description("Put cards from the pending-draw zone into hand, discard, or inPlay. Cards must have been drawn from this deck and not already placed.")]
    public static DeckSnapshot PutCards(DeckService decks, string deckId, List<string> cards, string destination = "hand")
        => decks.Put(deckId, cards, destination);

    [McpServerTool(Name = "move_cards", UseStructuredContent = true)]
    [Description("Move cards between deck, pending, hand, discard, and inPlay. For deck as source, provide count to move cards from the top without revealing the remaining deck. For other sources, provide card names. Use position top or bottom when the destination is deck.")]
    public static DeckSnapshot MoveCards(DeckService decks, string deckId, string source, string destination, List<string>? cards = null, int? count = null, string position = "top")
        => decks.Move(deckId, source, destination, cards, count, position);

    [McpServerTool(Name = "move_discard_into_deck", UseStructuredContent = true)]
    [Description("Move every discarded card into the deck. Optionally shuffle afterward.")]
    public static DeckSnapshot MoveDiscardIntoDeck(DeckService decks, string deckId, bool shuffle = false)
        => decks.MoveDiscardIntoDeck(deckId, shuffle);

    [McpServerTool(Name = "place_cards", UseStructuredContent = true)]
    [Description("Move cards from hand, discard, or inPlay to the top or bottom of the deck. This changes deck order and clears reveal knowledge.")]
    public static DeckSnapshot PlaceCards(DeckService decks, string deckId, List<string> cards, string source, string position)
        => decks.Place(deckId, cards, source, position);

    [McpServerTool(Name = "reveal_cards", UseStructuredContent = true)]
    [Description("Reveal the next cards without removing them. Only cards explicitly revealed remain visible in later deck status results, and shuffling clears that knowledge.")]
    public static IReadOnlyList<Card> RevealCards(DeckService decks, string deckId, int count = 1) => decks.Reveal(deckId, count);

    [McpServerTool(Name = "deck_status", UseStructuredContent = true)]
    [Description("Return deck zone counts and cards previously revealed from the top. It never exposes unrevealed deck contents or the next card.")]
    public static DeckSnapshot DeckStatus(DeckService decks, string deckId) => decks.GetSnapshot(deckId);

    [McpServerTool(Name = "simulate_roll", UseStructuredContent = true)]
    [Description("Roll Dice Throne dice for a hero and return the resulting dice plus ranked objective suggestions.")]
    public static async Task<RollToolResult> SimulateRoll(
        HeroService heroService,
        DiceRollAdvisor advisor,
        TelemetryService telemetry,
        [Description("Hero id, for example barbarian or psylocke.")] string heroId,
        [Description("Number of normal dice to roll. Defaults to 5.")] int diceCount = 5,
        [Description("Optional current dice. Omit to generate a random roll.")] List<int>? currentDice = null,
        [Description("Number of rerolls remaining. Defaults to 2.")] int rollsRemaining = 2,
        [Description("Probability method: analytic or montecarlo. Defaults to analytic.")] string method = "analytic",
        EvaluationConfig? evaluation = null)
    {
        var hero = await GetHeroOrThrowAsync(heroService, heroId);
        var hasManifestDie = hero.Id.Equals("psylocke", StringComparison.OrdinalIgnoreCase);
        var dice = currentDice ?? RollDice(diceCount + (hasManifestDie ? 1 : 0));
        var lockedDiceMask = BuildLockedDiceMask(dice.Count, hasManifestDie);
        evaluation ??= new EvaluationConfig();
        evaluation.ApplyHeroDefaults(hero.TokenValues);
        var suggestions = advisor.GetAdvice(dice, rollsRemaining, hero.Objectives, method, lockedDiceMask, evaluation);
        await telemetry.RecordOperationAsync(null, "mcp-simulate", heroId);

        return new RollToolResult(dice, rollsRemaining, suggestions, hasManifestDie, hasManifestDie ? 0 : -1);
    }

    [McpServerTool(Name = "calculate_probability", UseStructuredContent = true)]
    [Description("Calculate the chance of completing a Dice Throne objective from a fresh roll with optimal reroll decisions.")]
    public static async Task<ProbabilityToolResult> CalculateProbability(
        DiceNotationParser parser,
        ProbabilityCalculator calculator,
        MonteCarloSimulator simulator,
        TelemetryService telemetry,
        [Description("Objective notation such as [6666], [(123)(123)(123)], SmallStraight, or LargeStraight.")] string notation,
        [Description("Number of dice. Defaults to 5.")] int diceCount = 5,
        [Description("Probability method: analytic or montecarlo. Defaults to analytic.")] string method = "analytic")
    {
        var objective = parser.Parse("Custom", notation);
        var normalizedMethod = method.Equals("montecarlo", StringComparison.OrdinalIgnoreCase) ? "montecarlo" : "analytic";
        var probability = normalizedMethod == "montecarlo"
            ? simulator.Simulate(objective, diceCount, MonteCarloConst.StandardIterations)
            : calculator.Calculate(objective, diceCount);
        await telemetry.RecordOperationAsync(null, "mcp-probability");

        return new ProbabilityToolResult(probability, normalizedMethod, notation, diceCount);
    }

    [McpServerTool(Name = "set_dice", UseStructuredContent = true)]
    [Description("Set a hero's current dice to explicit values and return ranked objective suggestions for the remaining rerolls.")]
    public static async Task<RollToolResult> SetDice(
        HeroService heroService,
        DiceRollAdvisor advisor,
        TelemetryService telemetry,
        [Description("Hero id, for example barbarian or psylocke.")] string heroId,
        [Description("Explicit die values, each from 1 through 6.")] List<int> currentDice,
        [Description("Number of rerolls remaining. Defaults to 2.")] int rollsRemaining = 2,
        [Description("Probability method: analytic or montecarlo. Defaults to analytic.")] string method = "analytic",
        EvaluationConfig? evaluation = null)
    {
        if (currentDice.Count == 0)
            throw new ArgumentException("currentDice must contain at least one value.");
        if (currentDice.Any(die => die is < 1 or > 6))
            throw new ArgumentException("Each currentDice value must be between 1 and 6.");

        var hero = await GetHeroOrThrowAsync(heroService, heroId);
        var hasManifestDie = hero.Id.Equals("psylocke", StringComparison.OrdinalIgnoreCase);
        var lockedDiceMask = BuildLockedDiceMask(currentDice.Count, hasManifestDie);
        evaluation ??= new EvaluationConfig();
        evaluation.ApplyHeroDefaults(hero.TokenValues);
        var suggestions = advisor.GetAdvice(currentDice, rollsRemaining, hero.Objectives, method, lockedDiceMask, evaluation);
        await telemetry.RecordOperationAsync(null, "mcp-setdice", heroId);

        return new RollToolResult(currentDice, rollsRemaining, suggestions, hasManifestDie, hasManifestDie ? 0 : -1);
    }

    private static async Task<Hero> GetHeroOrThrowAsync(HeroService heroService, string heroId)
        => await heroService.GetHeroByIdAsync(heroId) ?? throw new ArgumentException($"Hero not found: {heroId}");

    private static List<int> RollDice(int count)
        => Enumerable.Range(0, count).Select(_ => Random.Shared.Next(1, 7)).ToList();

    private static List<bool> BuildLockedDiceMask(int diceCount, bool hasManifestDie)
    {
        var mask = Enumerable.Repeat(false, diceCount).ToList();
        if (hasManifestDie && diceCount > 0)
            mask[0] = true;
        return mask;
    }
}

public sealed record RollToolResult(
    List<int> Dice,
    int RollsRemaining,
    List<RollAdvice> Suggestions,
    bool HasManifestDie,
    int ManifestDieIndex);

public sealed record ProbabilityToolResult(
    double Probability,
    string Method,
    string Notation,
    int DiceCount);

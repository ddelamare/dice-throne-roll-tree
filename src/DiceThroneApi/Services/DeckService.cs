namespace DiceThroneApi.Services;

public sealed class DeckService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DeckState> _decks = new(StringComparer.Ordinal);

    public DeckSnapshot Create(IEnumerable<Card> cards)
    {
        var cardList = ValidateCards(cards);
        var state = new DeckState(cardList);
        var id = Guid.NewGuid().ToString("N");
        lock (_gate) _decks.Add(id, state);
        return Snapshot(id, state);
    }

    public DeckSnapshot Shuffle(string deckId)
    {
        lock (_gate)
        {
            var state = Get(deckId);
            ShuffleInternal(state);
            state.RevealedTop.Clear();
            return Snapshot(deckId, state);
        }
    }

    public IReadOnlyList<Card> Draw(string deckId, int count)
    {
        if (count < 1) throw new ArgumentException("count must be at least 1.");
        lock (_gate)
        {
            var state = Get(deckId);
            if (count > state.Deck.Count) throw new InvalidOperationException("The deck does not contain enough cards.");
            var result = state.Deck.Take(count).ToList();
            state.Deck.RemoveRange(0, count);
            state.PendingDraw.AddRange(result);
            state.RevealedTop.RemoveRange(0, Math.Min(count, state.RevealedTop.Count));
            return result;
        }
    }

    public DeckSnapshot Put(string deckId, IEnumerable<string> cardNames, string destination)
    {
        lock (_gate) return MoveLocked(deckId, Get(deckId), ValidateNames(cardNames), "pending", destination, null);
    }

    public DeckSnapshot MoveDiscardIntoDeck(string deckId, bool shuffle = false)
    {
        lock (_gate)
        {
            var state = Get(deckId);
            state.Deck.AddRange(state.Discard);
            state.Discard.Clear();
            state.RevealedTop.Clear();
            if (shuffle) ShuffleInternal(state);
            return Snapshot(deckId, state);
        }
    }

    public DeckSnapshot Place(string deckId, IEnumerable<string> cardNames, string source, string position)
    {
        lock (_gate) return MoveLocked(deckId, Get(deckId), ValidateNames(cardNames), source, "deck", position);
    }

    public DeckSnapshot Move(string deckId, string source, string destination, IEnumerable<string>? cardNames = null, int? count = null, string position = "top")
    {
        lock (_gate)
        {
            var state = Get(deckId);
            if (source.Equals("deck", StringComparison.OrdinalIgnoreCase))
            {
                if (count is not > 0) throw new ArgumentException("count must be provided and at least 1 when moving from deck.");
                if (count > state.Deck.Count) throw new InvalidOperationException("The deck does not contain enough cards.");
                return MoveLocked(deckId, state, Enumerable.Repeat(string.Empty, count.Value).ToList(), source, destination, null, count.Value);
            }
            if (cardNames is null) throw new ArgumentException("cards must be provided unless moving from deck.");
            return MoveLocked(deckId, state, ValidateNames(cardNames), source, destination, position);
        }
    }

    public IReadOnlyList<Card> Reveal(string deckId, int count)
    {
        if (count < 1) throw new ArgumentException("count must be at least 1.");
        lock (_gate)
        {
            var state = Get(deckId);
            if (count > state.Deck.Count) throw new InvalidOperationException("The deck does not contain enough cards.");
            state.RevealedTop.Clear();
            state.RevealedTop.AddRange(state.Deck.Take(count));
            return state.RevealedTop.ToArray();
        }
    }

    public DeckSnapshot GetSnapshot(string deckId)
    {
        lock (_gate) return Snapshot(deckId, Get(deckId));
    }

    private DeckState Get(string id) => _decks.TryGetValue(id, out var state) ? state : throw new KeyNotFoundException($"Deck not found: {id}");

    private static List<Card> ValidateCards(IEnumerable<Card> cards)
    {
        var result = cards?.ToList() ?? throw new ArgumentNullException(nameof(cards));
        if (result.Count == 0) throw new ArgumentException("cards must contain at least one card.");
        foreach (var card in result)
        {
            if (string.IsNullOrWhiteSpace(card.Name)) throw new ArgumentException("Every card must have a non-empty name.");
            if (card.Cost < 0) throw new ArgumentException("Card cost cannot be negative.");
            if (string.IsNullOrWhiteSpace(card.PlayPhase)) throw new ArgumentException("Every card must have a playPhase.");
            if (string.IsNullOrWhiteSpace(card.Effect)) throw new ArgumentException("Every card must have an effect.");
        }
        return result;
    }

    private static List<string> ValidateNames(IEnumerable<string> names)
    {
        var result = names?.ToList() ?? throw new ArgumentNullException(nameof(names));
        if (result.Count == 0 || result.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("cards must contain at least one non-empty card name.");
        return result;
    }

    private static List<Card> Zone(string value, DeckState state)
        => value.ToLowerInvariant() switch
        {
            "hand" => state.Hand,
            "discard" => state.Discard,
            "inplay" or "in-play" or "in play" or "in_play" => state.InPlay,
            "pending" or "pendingdraw" or "pending-draw" => state.PendingDraw,
            _ => throw new ArgumentException("source/destination must be deck, pending, hand, discard, or inPlay.")
        };

    private static DeckSnapshot MoveLocked(string deckId, DeckState state, List<string> cardNames, string source, string destination, string? position, int? deckCount = null)
    {
        var origin = source.Equals("deck", StringComparison.OrdinalIgnoreCase) ? state.Deck : Zone(source, state);
        var targetIsDeck = destination.Equals("deck", StringComparison.OrdinalIgnoreCase);
        var target = targetIsDeck ? null : Zone(destination, state);
        if (targetIsDeck && position is not null && !position.Equals("top", StringComparison.OrdinalIgnoreCase) && !position.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("position must be top or bottom.");
        if (ReferenceEquals(origin, state.Deck) && targetIsDeck) throw new ArgumentException("source and destination cannot both be deck.");

        var cards = ReferenceEquals(origin, state.Deck) ? origin.Take(deckCount ?? cardNames.Count).ToList() : TakeCards(origin, cardNames);
        if (ReferenceEquals(origin, state.Deck)) origin.RemoveRange(0, cards.Count);
        else foreach (var card in cards) RemoveCard(origin, card);

        if (targetIsDeck)
        {
            if (position!.Equals("top", StringComparison.OrdinalIgnoreCase)) state.Deck.InsertRange(0, cards);
            else state.Deck.AddRange(cards);
            state.RevealedTop.Clear();
        }
        else target!.AddRange(cards);
        if (ReferenceEquals(origin, state.Deck)) state.RevealedTop.RemoveRange(0, Math.Min(cards.Count, state.RevealedTop.Count));
        return Snapshot(deckId, state);
    }

    private static List<Card> TakeCards(List<Card> source, List<string> names)
    {
        var remaining = source.ToList();
        var result = new List<Card>(names.Count);
        foreach (var name in names)
        {
            var index = remaining.FindIndex(card => card.Name.Equals(name, StringComparison.Ordinal));
            if (index < 0) throw new InvalidOperationException($"Card is not available in the requested zone: {name}");
            result.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return result;
    }

    private static void RemoveCard(List<Card> cards, Card card) => cards.RemoveAt(cards.FindIndex(candidate => ReferenceEquals(candidate, card)));
    private static void ShuffleInternal(DeckState state) { for (var i = state.Deck.Count - 1; i > 0; i--) { var j = Random.Shared.Next(i + 1); (state.Deck[i], state.Deck[j]) = (state.Deck[j], state.Deck[i]); } }
    private static DeckSnapshot Snapshot(string id, DeckState state) => new(id, state.Deck.Count, state.Hand.Count, state.Discard.Count, state.InPlay.Count, state.PendingDraw.Count, state.Hand.ToArray(), state.Discard.ToArray(), state.InPlay.ToArray(), state.RevealedTop.ToArray());
    private sealed class DeckState(List<Card> deck) { public List<Card> Deck { get; } = deck; public List<Card> Hand { get; } = []; public List<Card> Discard { get; } = []; public List<Card> InPlay { get; } = []; public List<Card> PendingDraw { get; } = []; public List<Card> RevealedTop { get; } = []; }
}

public sealed record Card(string Name, int Cost, string PlayPhase, string Effect);
public sealed record DeckSnapshot(string DeckId, int DeckCount, int HandCount, int DiscardCount, int InPlayCount, int PendingDrawCount, IReadOnlyList<Card> Hand, IReadOnlyList<Card> Discard, IReadOnlyList<Card> InPlay, IReadOnlyList<Card> RevealedTop);

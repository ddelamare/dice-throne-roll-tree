using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class DeckServiceTests
{
    [Fact]
    public void DrawAndPutTracksZonesWithoutExposingUnrevealedCards()
    {
        var service = new DeckService();
        var created = service.Create([Card("one"), Card("two"), Card("three")]);

        Assert.NotEmpty(created.DeckId);
        Assert.Empty(created.RevealedTop);
        Assert.Equal(3, created.DeckCount);

        var drawn = service.Draw(created.DeckId, 1);
        Assert.Equal(["one"], drawn.Select(card => card.Name));
        var afterPut = service.Put(created.DeckId, drawn.Select(card => card.Name), "hand");
        Assert.Equal(2, afterPut.DeckCount);
        Assert.Equal(1, afterPut.HandCount);
        Assert.Empty(afterPut.RevealedTop);
    }

    [Fact]
    public void RevealPersistsUntilShuffleAndDoesNotRemoveCards()
    {
        var service = new DeckService();
        var created = service.Create([Card("one"), Card("two"), Card("three")]);

        Assert.Equal(["one", "two"], service.Reveal(created.DeckId, 2).Select(card => card.Name));
        var revealed = service.GetSnapshot(created.DeckId);
        Assert.Equal(3, revealed.DeckCount);
        Assert.Equal(["one", "two"], revealed.RevealedTop.Select(card => card.Name));

        var shuffled = service.Shuffle(created.DeckId);
        Assert.Empty(shuffled.RevealedTop);
    }

    [Fact]
    public void PlaceAndRecycleDiscardMaintainCardCounts()
    {
        var service = new DeckService();
        var created = service.Create([Card("one"), Card("two")]);
        var drawn = service.Draw(created.DeckId, 1);
        service.Put(created.DeckId, drawn.Select(card => card.Name), "discard");
        service.Place(created.DeckId, ["one"], "discard", "top");

        var status = service.GetSnapshot(created.DeckId);
        Assert.Equal(2, status.DeckCount);
        Assert.Equal(0, status.DiscardCount);
        Assert.Equal(["one"], service.Draw(created.DeckId, 1).Select(card => card.Name));
    }

    [Fact]
    public void InvalidDeckIdAndUnavailableCardsAreRejected()
    {
        var service = new DeckService();
        Assert.Throws<KeyNotFoundException>(() => service.GetSnapshot("missing"));
        var deck = service.Create([Card("one")]);
        Assert.Throws<InvalidOperationException>(() => service.Put(deck.DeckId, ["not-drawn"], "hand"));
    }

    [Fact]
    public void CardsCanMoveBetweenEveryTrackedZone()
    {
        var service = new DeckService();
        var deck = service.Create([Card("one"), Card("two"), Card("three")]);

        service.Move(deck.DeckId, "deck", "hand", count: 1);
        service.Move(deck.DeckId, "hand", "inPlay", ["one"]);
        service.Move(deck.DeckId, "inPlay", "discard", ["one"]);
        service.Move(deck.DeckId, "discard", "deck", ["one"], position: "bottom");
        service.Move(deck.DeckId, "deck", "pending", count: 1);
        service.Move(deck.DeckId, "pending", "inPlay", ["two"]);

        var status = service.GetSnapshot(deck.DeckId);
        Assert.Equal(["two"], status.InPlay.Select(card => card.Name));
        Assert.Equal(2, status.DeckCount);
        Assert.Empty(status.Hand);
        Assert.Empty(status.Discard);
    }

    private static Card Card(string name) => new(name, 0, "Any", "Test effect");
}

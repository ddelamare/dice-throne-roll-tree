using DiceThroneApi.Models;
using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class ObjectiveMatcherTests
{
    private readonly ObjectiveMatcher _matcher;
    private readonly DiceNotationParser _parser;

    public ObjectiveMatcherTests()
    {
        _matcher = new ObjectiveMatcher();
        _parser = new DiceNotationParser();
    }

    [Fact]
    public void IsMatch_FourSixes_WithFourSixes_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 6, 6, 6, 6, 1 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_FourSixes_WithThreeSixes_ReturnsFalse()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 6, 6, 6, 1, 2 };
        
        Assert.False(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_FiveSixes_WithFiveSixes_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "[66666]");
        var dice = new List<int> { 6, 6, 6, 6, 6 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_ThreeGroupsOf123_WithValidDice_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "[(123)(123)(123)]");
        var dice = new List<int> { 1, 2, 3, 4, 5 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_ThreeGroupsOf123_WithInvalidDice_ReturnsFalse()
    {
        var objective = _parser.Parse("Test", "[(123)(123)(123)]");
        var dice = new List<int> { 1, 2, 4, 5, 6 };
        
        Assert.False(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_MixedGroups_WithValidDice_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "[(123)(456)(456)]");
        var dice = new List<int> { 1, 4, 5, 2, 3 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_SmallStraight_With1234_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "SmallStraight");
        var dice = new List<int> { 1, 2, 3, 4, 6 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_SmallStraight_With2345_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "SmallStraight");
        var dice = new List<int> { 2, 3, 4, 5, 6 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_SmallStraight_With3456_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "SmallStraight");
        var dice = new List<int> { 3, 4, 5, 6, 1 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_SmallStraight_WithoutFourConsecutive_ReturnsFalse()
    {
        var objective = _parser.Parse("Test", "SmallStraight");
        var dice = new List<int> { 1, 2, 4, 5, 6 };
        
        Assert.False(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_LargeStraight_With12345_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "LargeStraight");
        var dice = new List<int> { 1, 2, 3, 4, 5 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_LargeStraight_With23456_ReturnsTrue()
    {
        var objective = _parser.Parse("Test", "LargeStraight");
        var dice = new List<int> { 2, 3, 4, 5, 6 };
        
        Assert.True(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_LargeStraight_WithoutFiveConsecutive_ReturnsFalse()
    {
        var objective = _parser.Parse("Test", "LargeStraight");
        var dice = new List<int> { 1, 2, 3, 4, 6 };
        
        Assert.False(_matcher.IsMatch(dice, objective));
    }

    [Fact]
    public void IsMatch_InsufficientDice_ReturnsFalse()
    {
        var objective = _parser.Parse("Test", "[6666]");
        var dice = new List<int> { 6, 6, 6 };
        
        Assert.False(_matcher.IsMatch(dice, objective));
    }
}

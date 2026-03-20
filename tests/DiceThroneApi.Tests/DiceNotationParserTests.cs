using DiceThroneApi.Services;
using Xunit;

namespace DiceThroneApi.Tests;

public class DiceNotationParserTests
{
    private readonly DiceNotationParser _parser;

    public DiceNotationParserTests()
    {
        _parser = new DiceNotationParser();
    }

    [Fact]
    public void Parse_FourSixes_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "[6666]");
        
        Assert.Equal("Test", result.Name);
        Assert.Equal("[6666]", result.Notation);
        Assert.Equal(Models.ObjectiveType.Standard, result.Type);
        Assert.Equal(4, result.DiceRequired);
        Assert.Equal(4, result.Groups.Count);
        Assert.All(result.Groups, g => Assert.Equal(new[] { 6 }, g.AllowedValues));
    }

    [Fact]
    public void Parse_FiveSixes_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "[66666]");
        
        Assert.Equal(Models.ObjectiveType.Standard, result.Type);
        Assert.Equal(5, result.DiceRequired);
        Assert.Equal(5, result.Groups.Count);
    }

    [Fact]
    public void Parse_ThreeGroupsOf123_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "[(123)(123)(123)]");
        
        Assert.Equal(Models.ObjectiveType.Standard, result.Type);
        Assert.Equal(3, result.DiceRequired);
        Assert.Equal(3, result.Groups.Count);
        Assert.All(result.Groups, g => 
            Assert.Equal(new[] { 1, 2, 3 }, g.AllowedValues));
    }

    [Fact]
    public void Parse_MixedGroups_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "[(123)(45)(6)]");
        
        Assert.Equal(Models.ObjectiveType.Standard, result.Type);
        Assert.Equal(3, result.DiceRequired);
        Assert.Equal(3, result.Groups.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Groups[0].AllowedValues);
        Assert.Equal(new[] { 4, 5 }, result.Groups[1].AllowedValues);
        Assert.Equal(new[] { 6 }, result.Groups[2].AllowedValues);
    }

    [Fact]
    public void Parse_SmallStraight_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "SmallStraight");
        
        Assert.Equal(Models.ObjectiveType.SmallStraight, result.Type);
        Assert.Equal(4, result.DiceRequired);
    }

    [Fact]
    public void Parse_LargeStraight_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "LargeStraight");
        
        Assert.Equal(Models.ObjectiveType.LargeStraight, result.Type);
        Assert.Equal(5, result.DiceRequired);
    }

    [Fact]
    public void Parse_ComplexPattern_ReturnsCorrectObjective()
    {
        var result = _parser.Parse("Test", "[(123)(123)(456)(456)]");
        
        Assert.Equal(4, result.DiceRequired);
        Assert.Equal(4, result.Groups.Count);
    }
}

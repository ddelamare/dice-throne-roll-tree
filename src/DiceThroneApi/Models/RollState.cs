namespace DiceThroneApi.Models;

public class RollState
{
    public List<int> Dice { get; set; } = new();
    public int RollsRemaining { get; set; }
    public int TotalDice { get; set; }
}

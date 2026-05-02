namespace DiceThroneApi.Models;

public class RollObjective
{
    public string Name { get; set; } = string.Empty;
    public string Notation { get; set; } = string.Empty;
    public ObjectiveType Type { get; set; }
    public List<RollObjectiveGroup> Groups { get; set; } = new();
    public int DiceRequired { get; set; }
    public int Damage { get; set; }
    public int Heal { get; set; }
    public int Cards { get; set; }
    public int Cp { get; set; }
    public List<string> Tokens { get; set; } = new();
}

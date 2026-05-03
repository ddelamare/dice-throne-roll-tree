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
    public bool BypassDefense { get; set; } = false; // Whether to ignore enemy defense when calculating expected value
    public bool IsDamageObjective => Damage > 0;
    public bool TriggersDefense => IsDamageObjective && !BypassDefense;
    public List<string> Tokens { get; set; } = new();
}

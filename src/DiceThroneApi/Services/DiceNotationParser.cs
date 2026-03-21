using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class DiceNotationParser
{
    public RollObjective Parse(string name, string notation)
    {
        var objective = new RollObjective
        {
            Name = name,
            Notation = notation
        };

        if (notation.Equals("SmallStraight", StringComparison.OrdinalIgnoreCase))
        {
            objective.Type = ObjectiveType.SmallStraight;
            objective.DiceRequired = 4;
            return objective;
        }

        if (notation.Equals("LargeStraight", StringComparison.OrdinalIgnoreCase))
        {
            objective.Type = ObjectiveType.LargeStraight;
            objective.DiceRequired = 5;
            return objective;
        }

        objective.Type = ObjectiveType.Standard;
        objective.Groups = new List<RollObjectiveGroup>();

        if (!notation.StartsWith('[') || !notation.EndsWith(']'))
        {
            throw new ArgumentException($"Invalid notation format: {notation}");
        }

        var content = notation[1..^1];
        var groups = new List<RollObjectiveGroup>();
        var i = 0;

        while (i < content.Length)
        {
            if (content[i] == '(')
            {
                var closeIdx = content.IndexOf(')', i);
                if (closeIdx == -1)
                {
                    throw new ArgumentException($"Unclosed group in notation: {notation}");
                }

                var groupContent = content.Substring(i + 1, closeIdx - i - 1);
                var allowedValues = groupContent.Select(c => int.Parse(c.ToString())).ToList();
                groups.Add(new RollObjectiveGroup { AllowedValues = allowedValues });
                i = closeIdx + 1;
            }
            else if (char.IsDigit(content[i]))
            {
                var value = int.Parse(content[i].ToString());
                groups.Add(new RollObjectiveGroup { AllowedValues = new List<int> { value } });
                i++;
            }
            else
            {
                throw new ArgumentException($"Invalid character in notation: {content[i]}");
            }
        }

        objective.Groups = groups;
        objective.DiceRequired = groups.Count;
        return objective;
    }
}

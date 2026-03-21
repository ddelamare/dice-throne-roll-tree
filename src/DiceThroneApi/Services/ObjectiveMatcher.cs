using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class ObjectiveMatcher
{
    public bool IsMatch(List<int> dice, RollObjective objective)
    {
        return objective.Type switch
        {
            ObjectiveType.Standard => IsStandardMatch(dice, objective),
            ObjectiveType.SmallStraight => IsSmallStraight(dice),
            ObjectiveType.LargeStraight => IsLargeStraight(dice),
            _ => false
        };
    }

    private bool IsStandardMatch(List<int> dice, RollObjective objective)
    {
        if (dice.Count < objective.DiceRequired)
        {
            return false;
        }

        var used = new bool[dice.Count];
        return CanMatch(dice, objective.Groups, 0, used);
    }

    private bool CanMatch(List<int> dice, List<RollObjectiveGroup> groups, int groupIndex, bool[] used)
    {
        if (groupIndex == groups.Count)
        {
            return true;
        }

        var group = groups[groupIndex];
        for (int i = 0; i < dice.Count; i++)
        {
            if (!used[i] && group.AllowedValues.Contains(dice[i]))
            {
                used[i] = true;
                if (CanMatch(dice, groups, groupIndex + 1, used))
                {
                    return true;
                }
                used[i] = false;
            }
        }

        return false;
    }

    private bool IsSmallStraight(List<int> dice)
    {
        var distinct = dice.Distinct().OrderBy(x => x).ToList();
        
        for (int start = 1; start <= 3; start++)
        {
            bool hasSequence = true;
            for (int val = start; val < start + 4; val++)
            {
                if (!distinct.Contains(val))
                {
                    hasSequence = false;
                    break;
                }
            }
            if (hasSequence)
            {
                return true;
            }
        }
        
        return false;
    }

    private bool IsLargeStraight(List<int> dice)
    {
        var distinct = dice.Distinct().OrderBy(x => x).ToList();
        
        if (distinct.Count < 5)
        {
            return false;
        }

        for (int start = 1; start <= 2; start++)
        {
            bool hasSequence = true;
            for (int val = start; val < start + 5; val++)
            {
                if (!distinct.Contains(val))
                {
                    hasSequence = false;
                    break;
                }
            }
            if (hasSequence)
            {
                return true;
            }
        }
        
        return false;
    }
}

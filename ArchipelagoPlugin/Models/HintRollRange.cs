namespace ArchipelagoPlugin.Models;

public sealed record HintRollRange(int MinimumRoll, int MaximumRoll, HintRewardType RewardType)
{
    public bool Contains(int roll)
    {
        return roll >= MinimumRoll && roll <= MaximumRoll;
    }
}

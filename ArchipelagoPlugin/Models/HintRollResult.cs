using Archipelago.MultiClient.Net.Enums;

namespace ArchipelagoPlugin.Models;

public sealed record HintRollResult(int Roll, HintRewardType RewardType)
{
    public HintStatus HintStatus => RewardType switch
    {
        HintRewardType.Trap => HintStatus.Avoid,
        HintRewardType.Filler => HintStatus.NoPriority,
        HintRewardType.Useful => HintStatus.Priority,
        HintRewardType.Progression => HintStatus.Priority,
        _ => HintStatus.Unspecified
    };
}

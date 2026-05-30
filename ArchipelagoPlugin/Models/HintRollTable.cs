using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchipelagoPlugin.Models;

public sealed class HintRollTable
{
    public static HintRollTable Default { get; } = new(
        "Default",
        new[]
        {
            new HintRollRange(1, 1, HintRewardType.Trap),
            new HintRollRange(2, 100, HintRewardType.Filler)
        });

    public static HintRollTable GuaranteedProgression { get; } = new(
        "Guaranteed progression",
        new[]
        {
            new HintRollRange(1, 100, HintRewardType.Progression)
        });

    public static HintRollTable HighEndSpecial { get; } = new(
        "High-end special",
        new[]
        {
            new HintRollRange(1, 1, HintRewardType.Trap),
            new HintRollRange(2, 25, HintRewardType.Filler),
            new HintRollRange(26, 80, HintRewardType.Useful),
            new HintRollRange(81, 100, HintRewardType.Progression)
        });

    public static HintRollTable DawntrailSavageRaid { get; } = new(
        "Dawntrail Savage raid",
        new[]
        {
            new HintRollRange(1, 1, HintRewardType.Trap),
            new HintRollRange(2, 60, HintRewardType.Useful),
            new HintRollRange(61, 100, HintRewardType.Progression)
        });

    public static HintRollTable DawntrailDungeon { get; } = new(
        "Dawntrail dungeon",
        new[]
        {
            new HintRollRange(1, 1, HintRewardType.Trap),
            new HintRollRange(2, 60, HintRewardType.Filler),
            new HintRollRange(61, 90, HintRewardType.Useful),
            new HintRollRange(91, 100, HintRewardType.Progression)
        });

    public static IReadOnlyList<HintRollTable> All { get; } = new[]
    {
        Default,
        GuaranteedProgression,
        HighEndSpecial,
        DawntrailSavageRaid,
        DawntrailDungeon
    };

    private readonly IReadOnlyList<HintRollRange> ranges;

    public HintRollTable(string name, IReadOnlyList<HintRollRange> ranges)
    {
        Name = name;
        this.ranges = ranges;
    }

    public string Name { get; }
    public IReadOnlyList<HintRollRange> Ranges => ranges;

    public HintRollResult Roll()
    {
        var roll = Random.Shared.Next(1, 101);
        return new HintRollResult(roll, GetRewardType(roll));
    }

    public HintRewardType GetRewardType(int roll)
    {
        return ranges.FirstOrDefault(range => range.Contains(roll))?.RewardType ?? HintRewardType.Filler;
    }

    public int GetProbabilityPercent(HintRewardType rewardType)
    {
        return ranges
            .Where(range => range.RewardType == rewardType)
            .Sum(range => range.MaximumRoll - range.MinimumRoll + 1);
    }
}

using System;
using ArchipelagoPlugin.Models;

namespace ArchipelagoPlugin.Services;

public static class HintRoller
{
    public static HintRollResult Roll(HintRollTable? rollTable = null)
    {
        return (rollTable ?? HintRollTable.Default).Roll();
    }

    public static HintRewardType GetRewardType(int roll, HintRollTable? rollTable = null)
    {
        return (rollTable ?? HintRollTable.Default).GetRewardType(roll);
    }
}

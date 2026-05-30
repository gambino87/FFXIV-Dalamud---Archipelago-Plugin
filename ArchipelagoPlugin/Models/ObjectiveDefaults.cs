using System.Collections.Generic;

namespace ArchipelagoPlugin.Models;

public static class ObjectiveDefaults
{
    public static List<ObjectiveDefinition> CreateObjectives()
    {
        return new List<ObjectiveDefinition>
        {
            new()
            {
                Id = "complete-any-dungeon-trial-or-raid",
                Name = "Complete any dungeon, trial, or raid",
                Kind = ObjectiveKind.DutyCompletion,
                DutyScope = DutyObjectiveScope.DungeonTrialOrRaid
            }
        };
    }
}

using System;

namespace ArchipelagoPlugin.Models;

[Serializable]
public class ObjectiveDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New objective";
    public ObjectiveKind Kind { get; set; } = ObjectiveKind.DutyCompletion;
    public bool Enabled { get; set; } = true;
    public DutyObjectiveScope DutyScope { get; set; } = DutyObjectiveScope.DungeonTrialOrRaid;
}

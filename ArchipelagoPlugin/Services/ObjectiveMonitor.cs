using System;
using System.Collections.Generic;
using System.Linq;
using ArchipelagoPlugin.Models;
using Dalamud.Game.DutyState;
using Lumina.Excel.Sheets;

namespace ArchipelagoPlugin.Services;

public sealed class ObjectiveMonitor : IDisposable
{
    private static readonly string[] DawntrailDungeonNames =
    {
        "Ihuykatumu",
        "Worqor Zormor",
        "The Skydeep Cenote",
        "Vanguard",
        "Origenics",
        "Alexandria",
        "The Strayborough Deadwalk",
        "Tender Valley",
        "Yuweyawata Field Station",
        "The Underkeep",
        "The Meso Terminal",
        "The Vault Oneiron",
        "The Clyteum"
    };

    private readonly Plugin plugin;
    private readonly List<string> recentEvents = new();

    public ObjectiveMonitor(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public IReadOnlyList<string> RecentEvents
    {
        get
        {
            lock (recentEvents)
            {
                return recentEvents.ToArray();
            }
        }
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
    }

    public bool TryCompleteObjective(string objectiveId, string details)
    {
        var objective = ObjectiveDefaults.CreateObjectives()
            .FirstOrDefault(entry => entry.Id.Equals(objectiveId, StringComparison.OrdinalIgnoreCase));

        if (objective == null)
        {
            return false;
        }

        CompleteObjective(objective, details);
        return true;
    }

    public CurrentDutyRollTable? GetCurrentDutyRollTable()
    {
        var completion = CreateDutyCompletion(Plugin.DutyState.ContentFinderCondition);
        if (completion == null)
        {
            return null;
        }

        return ObjectiveDefaults.CreateObjectives().Any(objective => MatchesObjective(objective, completion))
            ? new CurrentDutyRollTable(
                completion.DutyName,
                GetRollTable(completion),
                IsDisabledDueToUnrestrictedParty(completion),
                "Disabled due to Unrestricted Party")
            : null;
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        var completion = CreateDutyCompletion(args);
        if (completion == null)
        {
            AddRecentEvent("Ignored completed duty with no Content Finder metadata.");
            return;
        }

        var matchedAnyObjective = false;
        foreach (var objective in ObjectiveDefaults.CreateObjectives())
        {
            if (!MatchesObjective(objective, completion))
            {
                continue;
            }

            matchedAnyObjective = true;
            if (IsDisabledDueToUnrestrictedParty(completion))
            {
                var disabledMessage = $"Ignored completed duty due to Unrestricted Party: {FormatDutyDetails(completion)}";
                AddRecentEvent(disabledMessage);
                Plugin.Log.Information(disabledMessage);
                continue;
            }

            CompleteObjective(objective, FormatDutyDetails(completion), GetRollTable(completion));
        }

        if (!matchedAnyObjective)
        {
            AddRecentEvent($"Ignored completed duty: {FormatDutyDetails(completion)}");
        }
    }

    private static DutyCompletion? CreateDutyCompletion(IDutyStateEventArgs args)
    {
        return CreateDutyCompletion(args.ContentFinderCondition);
    }

    private static DutyCompletion? CreateDutyCompletion(Lumina.Excel.RowRef<ContentFinderCondition> contentFinderCondition)
    {
        if (!contentFinderCondition.IsValid)
        {
            return null;
        }

        var condition = contentFinderCondition.Value;
        return new DutyCompletion(
            condition.RowId,
            condition.Name.ToString(),
            GetRowName(condition.ContentType),
            GetRowName(condition.ContentUICategory),
            condition.ClassJobLevelSync);
    }

    private static bool MatchesObjective(ObjectiveDefinition objective, DutyCompletion completion)
    {
        return objective.Kind == ObjectiveKind.DutyCompletion &&
               IsInDutyScope(objective.DutyScope, completion);
    }

    private static bool IsInDutyScope(DutyObjectiveScope scope, DutyCompletion completion)
    {
        return scope switch
        {
            DutyObjectiveScope.DungeonTrialOrRaid => IsDungeon(completion) || IsTrial(completion) || IsRaid(completion),
            DutyObjectiveScope.Dungeon => IsDungeon(completion),
            DutyObjectiveScope.Trial => IsTrial(completion),
            DutyObjectiveScope.Raid => IsRaid(completion),
            _ => false
        };
    }

    private static bool IsDungeon(DutyCompletion completion)
    {
        return ContainsDutyCategory(completion, "Dungeon");
    }

    private static bool IsTrial(DutyCompletion completion)
    {
        return ContainsDutyCategory(completion, "Trial");
    }

    private static bool IsRaid(DutyCompletion completion)
    {
        return ContainsDutyCategory(completion, "Raid");
    }

    private static bool ContainsDutyCategory(DutyCompletion completion, string category)
    {
        return completion.ContentTypeName.Contains(category, StringComparison.OrdinalIgnoreCase) ||
               completion.ContentUiCategoryName.Contains(category, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRowName(Lumina.Excel.RowRef<ContentType> rowRef)
    {
        return rowRef.IsValid ? rowRef.Value.Name.ToString() : string.Empty;
    }

    private static string GetRowName(Lumina.Excel.RowRef<ContentUICategory> rowRef)
    {
        return rowRef.IsValid ? rowRef.Value.Name.ToString() : string.Empty;
    }

    private static HintRollTable GetRollTable(DutyCompletion completion)
    {
        if (IsUltimateRaid(completion))
        {
            return HintRollTable.GuaranteedProgression;
        }

        if (UsesHighEndSpecialTable(completion))
        {
            return HintRollTable.HighEndSpecial;
        }

        if (IsDawntrailSavageRaid(completion))
        {
            return HintRollTable.DawntrailSavageRaid;
        }

        if (IsDawntrailDungeon(completion))
        {
            return HintRollTable.DawntrailDungeon;
        }

        return HintRollTable.Default;
    }

    private static bool IsUltimateRaid(DutyCompletion completion)
    {
        return IsRaid(completion) &&
               ContainsDutyText(completion, "Ultimate");
    }

    private static bool UsesHighEndSpecialTable(DutyCompletion completion)
    {
        return IsShinryuUnreal(completion) ||
               IsCloudOfDarknessChaotic(completion) ||
               IsDawntrailHighEndTrial(completion);
    }

    private static bool IsShinryuUnreal(DutyCompletion completion)
    {
        return ContainsDutyText(completion, "Shinryu's Domain") &&
               ContainsDutyText(completion, "Unreal");
    }

    private static bool IsCloudOfDarknessChaotic(DutyCompletion completion)
    {
        return ContainsDutyText(completion, "The Cloud of Darkness") &&
               ContainsDutyText(completion, "Chaotic");
    }

    private static bool IsDawntrailHighEndTrial(DutyCompletion completion)
    {
        return IsTrial(completion) &&
               ContainsDutyText(completion, "High-end") &&
               ContainsDutyText(completion, "Dawntrail");
    }

    private static bool IsDawntrailSavageRaid(DutyCompletion completion)
    {
        if (!IsRaid(completion) || !ContainsDutyText(completion, "Savage"))
        {
            return false;
        }

        return ContainsDutyText(completion, "Dawntrail") ||
               ContainsDutyText(completion, "Arcadion") ||
               completion.DutyName.StartsWith("AAC ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDawntrailDungeon(DutyCompletion completion)
    {
        return IsDungeon(completion) &&
               (ContainsDutyText(completion, "Dawntrail") ||
                DawntrailDungeonNames.Any(name => completion.DutyName.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsDutyText(DutyCompletion completion, string text)
    {
        return completion.DutyName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               completion.ContentTypeName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               completion.ContentUiCategoryName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisabledDueToUnrestrictedParty(DutyCompletion completion)
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded || completion.ClassJobLevelSync == 0)
        {
            return false;
        }

        return playerState.Level > completion.ClassJobLevelSync &&
               !playerState.IsLevelSynced;
    }

    private void CompleteObjective(
        ObjectiveDefinition objective,
        string details,
        HintRollTable? rollTable = null)
    {
        var message = $"Completed: {objective.Name} ({details})";
        AddRecentEvent(message);
        Plugin.Log.Information(message);

        AddRecentEvent("Starting d100 hint roll.");
        _ = plugin.HintRollDisplay.RollAndDispatchAsync(objective, details, rollTable);
    }

    private static string FormatDutyDetails(DutyCompletion completion)
    {
        var category = string.IsNullOrWhiteSpace(completion.ContentTypeName)
            ? completion.ContentUiCategoryName
            : completion.ContentTypeName;

        return string.IsNullOrWhiteSpace(category)
            ? completion.DutyName
            : $"{completion.DutyName} [{category}]";
    }

    private void AddRecentEvent(string message)
    {
        lock (recentEvents)
        {
            recentEvents.Insert(0, $"{DateTime.Now:T} {message}");
            if (recentEvents.Count > 12)
            {
                recentEvents.RemoveRange(12, recentEvents.Count - 12);
            }
        }
    }
}

public sealed record CurrentDutyRollTable(
    string DutyName,
    HintRollTable RollTable,
    bool IsDisabledDueToUnrestrictedParty,
    string DisabledReason);

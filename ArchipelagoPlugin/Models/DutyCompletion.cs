namespace ArchipelagoPlugin.Models;

public sealed record DutyCompletion(
    uint ContentFinderConditionId,
    string DutyName,
    string ContentTypeName,
    string ContentUiCategoryName);

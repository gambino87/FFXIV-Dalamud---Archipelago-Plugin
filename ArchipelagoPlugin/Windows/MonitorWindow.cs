using System;
using System.Numerics;
using ArchipelagoPlugin.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ArchipelagoPlugin.Windows;

public sealed class MonitorWindow : Window, IDisposable
{
    private static readonly Vector4 DisconnectedTextColor = new(1f, 0.35f, 0.25f, 1f);
    private static readonly Vector4 DisabledTextColor = new(1f, 0.25f, 0.2f, 1f);

    private readonly Plugin plugin;

    public MonitorWindow(Plugin plugin)
        : base("Archipelago Monitor###ArchipelagoPluginMonitor")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 130),
            MaximumSize = new Vector2(420, 260)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var roll = plugin.HintRollDisplay.Snapshot;
        if (roll.IsRolling)
        {
            DrawRolling(roll);
            return;
        }

        if (roll.HasResult)
        {
            DrawResult(roll);
            return;
        }

        if (!plugin.Archipelago.IsConnected)
        {
            ImGui.TextColored(DisconnectedTextColor, "Not Connected");
            return;
        }

        if (!Plugin.DutyState.IsDutyStarted)
        {
            ImGui.TextUnformatted("Not in a duty.");
            return;
        }

        var currentRollTable = plugin.ObjectiveMonitor.GetCurrentDutyRollTable();
        if (currentRollTable != null)
        {
            if (currentRollTable.IsDisabledDueToUnrestrictedParty)
            {
                ImGui.TextColored(DisabledTextColor, currentRollTable.DisabledReason);
                return;
            }

            ImGui.TextUnformatted("Waiting....");
            DrawProbabilities(currentRollTable.RollTable);
            return;
        }

        ImGui.TextUnformatted("Waiting....");
    }

    private static void DrawRolling(Services.HintRollDisplaySnapshot roll)
    {
        ImGui.TextUnformatted("Rolling...");
        ImGui.SetWindowFontScale(2.4f);
        ImGui.TextUnformatted(roll.DisplayRoll.ToString("000"));
        ImGui.SetWindowFontScale(1f);

        if (roll.RewardType != null)
        {
            ImGui.TextUnformatted(roll.RewardType.ToString());
        }

        ImGui.ProgressBar(roll.Progress, new Vector2(-1, 0), string.Empty);
    }

    private static void DrawResult(Services.HintRollDisplaySnapshot roll)
    {
        ImGui.TextUnformatted("Roll complete");
        ImGui.SetWindowFontScale(2.0f);
        ImGui.TextUnformatted(roll.DisplayRoll.ToString("000"));
        ImGui.SetWindowFontScale(1f);

        if (roll.RewardType != null)
        {
            ImGui.TextUnformatted($"Bucket: {roll.RewardType}");
        }

        if (!string.IsNullOrWhiteSpace(roll.RevealedHint))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(roll.RevealedHint);
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(roll.Status);
        }
    }

    private static void DrawProbabilities(HintRollTable table)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Probabilities");
        DrawProbabilityLine(table, HintRewardType.Trap);
        DrawProbabilityLine(table, HintRewardType.Filler);
        DrawProbabilityLine(table, HintRewardType.Useful);
        DrawProbabilityLine(table, HintRewardType.Progression);
    }

    private static void DrawProbabilityLine(HintRollTable table, HintRewardType rewardType)
    {
        var percent = table.GetProbabilityPercent(rewardType);
        if (percent <= 0)
        {
            return;
        }

        ImGui.TextUnformatted($"{rewardType} {percent}%");
    }
}

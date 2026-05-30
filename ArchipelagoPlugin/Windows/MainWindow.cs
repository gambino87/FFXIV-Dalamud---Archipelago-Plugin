using System;
using System.Linq;
using System.Numerics;
using ArchipelagoPlugin.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace ArchipelagoPlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly Vector4 DisconnectedTextColor = new(1f, 0.35f, 0.25f, 1f);
    private static readonly Vector4 ActiveRollTableTextColor = new(0.3f, 1f, 0.45f, 1f);
    private static readonly Vector4 DisabledRollTextColor = new(1f, 0.25f, 0.2f, 1f);
    private static readonly RollTableDisplay[] RollTableDisplays =
    {
        new("Default dungeon, trial, or raid", HintRollTable.Default),
        new("Any Ultimate raid", HintRollTable.GuaranteedProgression),
        new("Shinryu's Domain (Unreal), The Cloud of Darkness (Chaotic), High-end (Dawntrail) trials", HintRollTable.HighEndSpecial),
        new("All Dawntrail Savage raids", HintRollTable.DawntrailSavageRaid),
        new("All Dawntrail dungeons", HintRollTable.DawntrailDungeon)
    };

    private readonly Plugin plugin;
    private readonly SettingsPane settingsPane;
    private bool selectSettingsTab;
    private bool startedInitialHintsRefresh;

    public MainWindow(Plugin plugin)
        : base("Archipelago###ArchipelagoPluginMain")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(430, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        settingsPane = new SettingsPane(plugin);
    }

    public void Dispose() { }

    public void OpenSettingsTab()
    {
        IsOpen = true;
        selectSettingsTab = true;
    }

    public override void Draw()
    {
        plugin.Archipelago.RefreshConnectionState();

        if (ImGui.BeginTabBar("ArchipelagoMainTabs"))
        {
            if (ImGui.BeginTabItem("Main"))
            {
                DrawMainTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Hints"))
            {
                DrawHintsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Roll Tables"))
            {
                DrawRollTablesTab();
                ImGui.EndTabItem();
            }

            var settingsFlags = selectSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Settings", settingsFlags))
            {
                settingsPane.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
            selectSettingsTab = false;
        }
    }

    private void DrawRollTablesTab()
    {
        var currentRollTable = plugin.ObjectiveMonitor.GetCurrentDutyRollTable();

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("ArchipelagoRollTablesTable", 5, flags, new Vector2(0, 0)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Duty rule");
        ImGui.TableSetupColumn("Trap");
        ImGui.TableSetupColumn("Filler");
        ImGui.TableSetupColumn("Useful");
        ImGui.TableSetupColumn("Progression");
        ImGui.TableHeadersRow();

        foreach (var display in RollTableDisplays)
        {
            var isActive = currentRollTable != null &&
                           ReferenceEquals(display.Table, currentRollTable.RollTable);
            var isDisabled = isActive && currentRollTable!.IsDisabledDueToUnrestrictedParty;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawRollTableText(display.Rule, isActive, isDisabled, true);
            DrawRollRangeCell(display.Table, HintRewardType.Trap, isActive, isDisabled);
            DrawRollRangeCell(display.Table, HintRewardType.Filler, isActive, isDisabled);
            DrawRollRangeCell(display.Table, HintRewardType.Useful, isActive, isDisabled);
            DrawRollRangeCell(display.Table, HintRewardType.Progression, isActive, isDisabled);
        }

        ImGui.EndTable();
    }

    private static void DrawRollRangeCell(
        HintRollTable table,
        HintRewardType rewardType,
        bool isActive,
        bool isDisabled)
    {
        ImGui.TableNextColumn();
        DrawRollTableText(FormatRollRanges(table, rewardType), isActive, isDisabled, false);
    }

    private static void DrawRollTableText(string text, bool isActive, bool isDisabled, bool wrapped)
    {
        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, isDisabled ? DisabledRollTextColor : ActiveRollTableTextColor);
        }

        if (wrapped)
        {
            ImGui.TextWrapped(text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }

        if (isActive)
        {
            ImGui.PopStyleColor();
        }
    }

    private static string FormatRollRanges(HintRollTable table, HintRewardType rewardType)
    {
        var ranges = table.Ranges
            .Where(range => range.RewardType == rewardType)
            .Select(FormatRollRange)
            .ToArray();

        return ranges.Length == 0 ? "-" : string.Join(", ", ranges);
    }

    private static string FormatRollRange(HintRollRange range)
    {
        return range.MinimumRoll == range.MaximumRoll
            ? range.MinimumRoll.ToString()
            : $"{range.MinimumRoll}-{range.MaximumRoll}";
    }

    private void DrawHintsTab()
    {
        if (!plugin.Archipelago.IsConnected)
        {
            startedInitialHintsRefresh = false;
            ImGui.TextUnformatted("Connect to Archipelago to load hints.");
            return;
        }

        if (!startedInitialHintsRefresh)
        {
            startedInitialHintsRefresh = true;
            _ = plugin.Archipelago.RefreshHintsAsync();
        }

        if (plugin.Archipelago.IsRefreshingHints)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Refresh", new Vector2(110, 0)))
        {
            _ = plugin.Archipelago.RefreshHintsAsync();
        }

        if (plugin.Archipelago.IsRefreshingHints)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(plugin.Archipelago.HintListStatus);

        if (plugin.Archipelago.HintsRefreshedAt != null)
        {
            ImGui.TextUnformatted($"Last refreshed: {plugin.Archipelago.HintsRefreshedAt.Value:T}");
        }

        ImGui.Spacing();
        DrawHintsTable();
    }

    private void DrawHintsTable()
    {
        var hints = plugin.Archipelago.CurrentHints;
        if (hints.Count == 0)
        {
            return;
        }

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("ArchipelagoHintsTable", 6, flags, new Vector2(0, 0)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Found");
        ImGui.TableSetupColumn("Tier");
        ImGui.TableSetupColumn("Location");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Finder");
        ImGui.TableSetupColumn("Receiver");
        ImGui.TableHeadersRow();

        foreach (var hint in hints)
        {
            ImGui.TableNextRow();
            DrawHintCell(hint.Found ? "Yes" : "No");
            DrawHintCell(hint.Tier);
            DrawHintCell(hint.Location);
            DrawHintCell(hint.Item);
            DrawHintCell(hint.Finder);
            DrawHintCell(hint.Receiver);
        }

        ImGui.EndTable();
    }

    private static void DrawHintCell(string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private void DrawMainTab()
    {
        ImGui.TextUnformatted("Archipelago");
        ImGui.Separator();

        DrawMainHeaderRow();
        ImGui.Spacing();

        using var child = ImRaii.Child("ArchipelagoPluginState", Vector2.Zero, true);
        if (!child.Success)
        {
            return;
        }

        DrawFfxivState();
    }

    private void DrawMainHeaderRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rowHeight = 150 * scale;
        var rollPanelWidth = Math.Clamp(availableWidth * 0.38f, 190 * scale, 280 * scale);
        var statusPanelWidth = Math.Max(210 * scale, availableWidth - rollPanelWidth - spacing);

        using (var statusPanel = ImRaii.Child("ArchipelagoMainStatus", new Vector2(statusPanelWidth, rowHeight), false))
        {
            if (statusPanel.Success)
            {
                DrawMainStatusControls();
            }
        }

        ImGui.SameLine();

        using var rollPanel = ImRaii.Child("ArchipelagoMainRollPanel", new Vector2(rollPanelWidth, rowHeight), true);
        if (rollPanel.Success)
        {
            DrawMainRollPanel();
        }
    }

    private void DrawMainStatusControls()
    {
        if (plugin.Archipelago.Status.Equals("Connection lost.", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(DisconnectedTextColor, GetShortConnectionStatus());
        }
        else
        {
            ImGui.TextUnformatted(GetShortConnectionStatus());
        }

        if (!string.IsNullOrWhiteSpace(plugin.Archipelago.LastError))
        {
            ImGui.TextWrapped(plugin.Archipelago.LastError);
        }

        ImGui.TextUnformatted($"Server: {DisplayOrPlaceholder(plugin.Archipelago.ConnectedServer, "not connected")}");
        ImGui.TextUnformatted($"Slot: {DisplayOrPlaceholder(plugin.Archipelago.ConnectedSlot, "not connected")}");
        ImGui.TextUnformatted($"Game: {DisplayOrPlaceholder(plugin.Archipelago.ConnectedGame, "not connected")}");

        var connectDisabled = plugin.Archipelago.IsConnecting || plugin.Archipelago.IsConnected;
        if (connectDisabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Connect", new Vector2(110, 0)))
        {
            _ = plugin.Archipelago.ConnectAsync(plugin.Configuration);
        }

        if (connectDisabled)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (!plugin.Archipelago.IsConnected)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Disconnect", new Vector2(110, 0)))
        {
            plugin.Archipelago.Disconnect();
        }

        if (!plugin.Archipelago.IsConnected)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (ImGui.Button("Monitor", new Vector2(110, 0)))
        {
            plugin.OpenMonitorUi();
        }

    }

    private void DrawMainRollPanel()
    {
        var roll = plugin.HintRollDisplay.Snapshot;

        if (roll.IsRolling)
        {
            ImGui.TextUnformatted("Rolling...");
            ImGui.SetWindowFontScale(2.1f);
            ImGui.TextUnformatted(roll.DisplayRoll.ToString("000"));
            ImGui.SetWindowFontScale(1f);

            if (roll.RewardType != null)
            {
                ImGui.TextUnformatted(roll.RewardType.ToString());
            }

            ImGui.ProgressBar(roll.Progress, new Vector2(-1, 0), string.Empty);
            return;
        }

        if (roll.HasResult)
        {
            ImGui.TextUnformatted("Roll complete");
            ImGui.SetWindowFontScale(1.7f);
            ImGui.TextUnformatted(roll.DisplayRoll.ToString("000"));
            ImGui.SetWindowFontScale(1f);

            if (roll.RewardType != null)
            {
                ImGui.TextUnformatted(roll.RewardType.ToString());
            }

            if (!string.IsNullOrWhiteSpace(roll.RevealedHint))
            {
                ImGui.TextWrapped(roll.RevealedHint);
            }

            return;
        }

        if (!plugin.Archipelago.IsConnected)
        {
            ImGui.TextColored(DisconnectedTextColor, "Not Connected");
            return;
        }

        var currentRollTable = plugin.ObjectiveMonitor.GetCurrentDutyRollTable();
        if (currentRollTable != null)
        {
            if (currentRollTable.IsDisabledDueToUnrestrictedParty)
            {
                ImGui.TextColored(DisabledRollTextColor, currentRollTable.DisabledReason);
                return;
            }

            DrawProbabilities(currentRollTable.RollTable);
            return;
        }

        ImGui.TextUnformatted("Waiting...");
    }

    private static void DrawFfxivState()
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded)
        {
            ImGui.TextUnformatted("Log in to a character to show FFXIV state.");
            return;
        }

        if (!playerState.ClassJob.IsValid)
        {
            ImGui.TextUnformatted("Current job is not available yet.");
            return;
        }

        ImGui.TextUnformatted("FFXIV State");
        ImGui.Separator();

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Current job:");
        ImGui.SameLine(120 * ImGuiHelpers.GlobalScale);

        var jobIconId = 62100 + playerState.ClassJob.RowId;
        var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(jobIconId)).GetWrapOrEmpty();
        ImGui.Image(iconTexture.Handle, new Vector2(28, 28) * ImGuiHelpers.GlobalScale);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{playerState.ClassJob.Value.Abbreviation} [Level {playerState.Level}]");

        var territoryId = Plugin.ClientState.TerritoryType;
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            ImGui.TextUnformatted("Current location:");
            ImGui.SameLine(120 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted(territoryRow.PlaceName.Value.Name.ToString());
        }
        else
        {
            ImGui.TextUnformatted("Current location: unavailable");
        }
    }

    private static string DisplayOrPlaceholder(string value, string placeholder)
    {
        return string.IsNullOrWhiteSpace(value) ? placeholder : value;
    }

    private string GetShortConnectionStatus()
    {
        if (plugin.Archipelago.IsConnected)
        {
            return "Connected";
        }

        return plugin.Archipelago.Status;
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

    private sealed record RollTableDisplay(string Rule, HintRollTable Table);
}

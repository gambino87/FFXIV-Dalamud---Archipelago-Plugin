using System.Numerics;
using ArchipelagoPlugin.Services;
using Dalamud.Bindings.ImGui;

namespace ArchipelagoPlugin.Windows;

public sealed class SettingsPane
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public SettingsPane(Plugin plugin)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Draw()
    {
        configuration.EnsureDefaults();
        DrawConnectionSettings();
    }

    private void DrawConnectionSettings()
    {
        var serverAddress = configuration.ServerAddress;
        if (ImGui.InputText("Server", ref serverAddress, 256))
        {
            configuration.ServerAddress = serverAddress;
            configuration.Save();
        }

        if (plugin.Archipelago.IsLoadingRoomInfo)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Load Room Info", new Vector2(130, 0)))
        {
            _ = plugin.Archipelago.LoadRoomInfoAsync(configuration);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear", new Vector2(80, 0)))
        {
            configuration.ServerAddress = string.Empty;
            configuration.SlotName = string.Empty;
            configuration.Password = string.Empty;
            configuration.GameName = string.Empty;
            configuration.Save();
            plugin.Archipelago.ClearRoomInfo();
        }

        if (plugin.Archipelago.IsLoadingRoomInfo)
        {
            ImGui.EndDisabled();
        }

        ImGui.TextWrapped(plugin.Archipelago.RoomInfoStatus);

        DrawSlotCombo();

        var password = configuration.Password;
        if (ImGui.InputText("Password", ref password, 128, ImGuiInputTextFlags.Password))
        {
            configuration.Password = password;
            configuration.Save();
        }

        DrawGameCombo();

        var connectOnStartup = configuration.ConnectOnStartup;
        if (ImGui.Checkbox("Connect on startup", ref connectOnStartup))
        {
            configuration.ConnectOnStartup = connectOnStartup;
            configuration.Save();
        }
    }

    private void DrawSlotCombo()
    {
        var slots = plugin.Archipelago.DiscoveredSlots;
        var hasSlots = slots.Count > 0;
        if (!hasSlots)
        {
            var slotName = configuration.SlotName;
            if (ImGui.InputText("Slot", ref slotName, 128))
            {
                configuration.SlotName = slotName;
                configuration.Save();
            }

            return;
        }

        var preview = hasSlots
            ? string.IsNullOrWhiteSpace(configuration.SlotName)
            ? "Select slot"
            : configuration.SlotName
            : "Load room info first";

        if (!ImGui.BeginCombo("Slot", preview))
        {
            return;
        }

        foreach (var slot in slots)
        {
            var selected = configuration.SlotName.Equals(slot.Name, System.StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(slot.DisplayName, selected))
            {
                configuration.SlotName = slot.Name;
                configuration.Save();
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawGameCombo()
    {
        var games = plugin.Archipelago.DiscoveredGames;
        var hasGames = games.Count > 0;
        if (!hasGames)
        {
            var gameName = configuration.GameName;
            if (ImGui.InputText("Game", ref gameName, 128))
            {
                configuration.GameName = gameName;
                configuration.Save();
            }

            return;
        }

        var preview = hasGames
            ? string.IsNullOrWhiteSpace(configuration.GameName)
            ? "Select game"
            : configuration.GameName
            : "Load room info first";

        if (!ImGui.BeginCombo("Game", preview))
        {
            return;
        }

        foreach (var game in games)
        {
            var selected = configuration.GameName.Equals(game, System.StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(game, selected))
            {
                configuration.GameName = game;
                configuration.Save();
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }
}

using System;
using Dalamud.Configuration;

namespace ArchipelagoPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string ServerAddress { get; set; } = "localhost:38281";
    public string SlotName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string GameName { get; set; } = "Final Fantasy XIV";
    public bool ConnectOnStartup { get; set; } = false;

    public void EnsureDefaults() { }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

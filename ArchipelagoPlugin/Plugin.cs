using ArchipelagoPlugin.Services;
using ArchipelagoPlugin.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ArchipelagoPlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/archipelago";
    private const string ShortCommandName = "/ap";

    public Configuration Configuration { get; init; }
    public ArchipelagoConnection Archipelago { get; init; }
    public HintRollDisplay HintRollDisplay { get; init; }
    public ObjectiveMonitor ObjectiveMonitor { get; init; }

    public readonly WindowSystem WindowSystem = new("ArchipelagoPlugin");
    private MainWindow MainWindow { get; init; }
    private MonitorWindow MonitorWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureDefaults();
        Archipelago = new ArchipelagoConnection();
        HintRollDisplay = new HintRollDisplay(this);
        ObjectiveMonitor = new ObjectiveMonitor(this);

        MainWindow = new MainWindow(this);
        MonitorWindow = new MonitorWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MonitorWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Archipelago window. Use 'config' or 'complete <objective id>' for extra actions."
        });

        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Archipelago window."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        if (Configuration.ConnectOnStartup)
        {
            _ = Archipelago.ConnectAsync(Configuration);
        }

        Log.Information("{PluginName} loaded.", PluginInterface.Manifest.Name);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();
        MonitorWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        ObjectiveMonitor.Dispose();
        HintRollDisplay.Dispose();
        Archipelago.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args.Trim();
        if (trimmedArgs.Equals("config", System.StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigUi();
            return;
        }

        const string CompletePrefix = "complete ";
        if (trimmedArgs.StartsWith(CompletePrefix, System.StringComparison.OrdinalIgnoreCase))
        {
            var objectiveId = trimmedArgs[CompletePrefix.Length..].Trim();
            if (!ObjectiveMonitor.TryCompleteObjective(objectiveId, "Manually completed from chat command."))
            {
                Log.Warning("Unknown Archipelago objective id: {ObjectiveId}", objectiveId);
            }

            return;
        }

        MainWindow.Toggle();
    }

    public void ToggleConfigUi() => OpenConfigUi();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleMonitorUi() => MonitorWindow.Toggle();
    public void OpenMainUi() => MainWindow.IsOpen = true;
    public void OpenConfigUi() => MainWindow.OpenSettingsTab();
    public void OpenMonitorUi() => MonitorWindow.IsOpen = true;
}

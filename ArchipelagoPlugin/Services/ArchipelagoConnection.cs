using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using ArchipelagoPlugin.Models;

namespace ArchipelagoPlugin.Services;

public sealed class ArchipelagoConnection : IDisposable
{
    private static readonly TimeSpan ScoutSearchTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ScoutRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HintAnnouncementTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HintRefreshTimeout = TimeSpan.FromSeconds(20);
    private static readonly Version ProtocolVersion = new(0, 6, 7);
    private const int ScoutBatchSize = 50;

    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly SemaphoreSlim hintsRefreshLock = new(1, 1);
    private readonly SemaphoreSlim roomInfoLock = new(1, 1);
    private readonly object recentHintLock = new();
    private readonly object currentHintsLock = new();
    private readonly object roomInfoStateLock = new();
    private readonly List<string> recentHintEvents = new();
    private List<ArchipelagoHintDisplay> currentHints = new();
    private List<ArchipelagoRoomSlot> discoveredSlots = new();
    private List<string> discoveredGames = new();
    private ArchipelagoSession? session;
    private bool printedConnectionLostMessage;

    public bool IsConnecting { get; private set; }
    public bool IsRefreshingHints { get; private set; }
    public bool IsLoadingRoomInfo { get; private set; }
    public bool IsConnected => session?.Socket.Connected ?? false;
    public string Status { get; private set; } = "Not connected.";
    public string LastError { get; private set; } = string.Empty;
    public string LastHintStatus { get; private set; } = "No hints sent yet.";
    public string HintListStatus { get; private set; } = "Connect to Archipelago to load hints.";
    public string RoomInfoStatus { get; private set; } = "Load room info to pick slots and games.";
    public string ConnectedServer { get; private set; } = string.Empty;
    public string ConnectedSlot { get; private set; } = string.Empty;
    public string ConnectedGame { get; private set; } = string.Empty;
    public DateTime? HintsRefreshedAt { get; private set; }

    public IReadOnlyList<string> RecentHintEvents
    {
        get
        {
            lock (recentHintLock)
            {
                return recentHintEvents.ToArray();
            }
        }
    }

    public IReadOnlyList<ArchipelagoHintDisplay> CurrentHints
    {
        get
        {
            lock (currentHintsLock)
            {
                return currentHints.ToArray();
            }
        }
    }

    public IReadOnlyList<ArchipelagoRoomSlot> DiscoveredSlots
    {
        get
        {
            lock (roomInfoStateLock)
            {
                return discoveredSlots.ToArray();
            }
        }
    }

    public IReadOnlyList<string> DiscoveredGames
    {
        get
        {
            lock (roomInfoStateLock)
            {
                return discoveredGames.ToArray();
            }
        }
    }

    public async Task LoadRoomInfoAsync(Configuration configuration)
    {
        if (!await roomInfoLock.WaitAsync(0))
        {
            return;
        }

        ArchipelagoSession? roomInfoSession = null;
        try
        {
            IsLoadingRoomInfo = true;
            RoomInfoStatus = "Loading room info...";

            var serverAddress = configuration.ServerAddress.Trim();
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                RoomInfoStatus = "Enter an Archipelago server address first.";
                return;
            }

            lock (roomInfoStateLock)
            {
                discoveredSlots = new List<ArchipelagoRoomSlot>();
                discoveredGames = new List<string>();
            }

            var roomInfoAttempt = await Task.Run(async () =>
            {
                return await LoadRoomInfoFromAnyAddressAsync(serverAddress);
            });
            roomInfoSession = roomInfoAttempt.Session;
            var roomInfo = roomInfoAttempt.RoomInfo;
            var roomInfoPlayers = roomInfo.Players ?? Array.Empty<NetworkPlayer>();
            var roomInfoGames = roomInfo.Games ?? Array.Empty<string>();

            List<ArchipelagoRoomSlot> slots;
            List<string> games;
            lock (roomInfoStateLock)
            {
                slots = roomInfoPlayers
                    .OrderBy(player => player.Team)
                    .ThenBy(player => player.Slot)
                    .Select(player => new ArchipelagoRoomSlot(
                        player.Name,
                        player.Alias,
                        player.Team,
                        player.Slot))
                    .ToList();
                games = roomInfoGames
                    .Where(game => !string.IsNullOrWhiteSpace(game))
                    .OrderBy(game => game)
                    .ToList();
                discoveredSlots = slots;
                discoveredGames = games;
            }

            var changedConfiguration = false;
            if (!slots.Any(slot => slot.Name.Equals(configuration.SlotName, StringComparison.OrdinalIgnoreCase)))
            {
                configuration.SlotName = slots.Count == 1 ? slots[0].Name : string.Empty;
                changedConfiguration = true;
            }

            if (!games.Any(game => game.Equals(configuration.GameName, StringComparison.OrdinalIgnoreCase)))
            {
                configuration.GameName = games.Count == 1 ? games[0] : string.Empty;
                changedConfiguration = true;
            }

            if (changedConfiguration)
            {
                configuration.Save();
            }

            var missingParts = new List<string>();
            if (roomInfo.Players == null)
            {
                missingParts.Add("players");
            }

            if (roomInfo.Games == null)
            {
                missingParts.Add("games");
            }

            RoomInfoStatus = missingParts.Count == 0
                ? $"Loaded {slots.Count} slot(s) and {games.Count} game(s) from {roomInfoAttempt.ServerAddress}."
                : $"Loaded room info from {roomInfoAttempt.ServerAddress}, but the server did not provide {string.Join(" or ", missingParts)}. Enter missing values manually.";
        }
        catch (Exception ex)
        {
            RoomInfoStatus = $"Failed to load room info: {ex.GetBaseException().Message}";
            Plugin.Log.Warning(ex, "Failed to load Archipelago room info.");
        }
        finally
        {
            try
            {
                await (roomInfoSession?.Socket.DisconnectAsync() ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Error while disconnecting room info session.");
            }

            IsLoadingRoomInfo = false;
            roomInfoLock.Release();
        }
    }

    public void ClearRoomInfo()
    {
        lock (roomInfoStateLock)
        {
            discoveredSlots = new List<ArchipelagoRoomSlot>();
            discoveredGames = new List<string>();
        }

        RoomInfoStatus = "Load room info to pick slots and games.";
    }

    public async Task ConnectAsync(Configuration configuration)
    {
        if (!await connectionLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            configuration.EnsureDefaults();
            IsConnecting = true;
            LastError = string.Empty;
            Status = "Connecting...";

            var serverAddress = configuration.ServerAddress.Trim();
            var slotName = configuration.SlotName.Trim();
            var gameName = configuration.GameName.Trim();

            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                Fail("Enter an Archipelago server address first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(slotName))
            {
                Fail("Enter your Archipelago slot name first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(gameName))
            {
                gameName = "Final Fantasy XIV";
            }

            Disconnect();

            var password = string.IsNullOrWhiteSpace(configuration.Password)
                ? null
                : configuration.Password;

            var result = await Task.Run(async () =>
            {
                var newSession = ArchipelagoSessionFactory.CreateSession(serverAddress);
                var roomInfo = await newSession.ConnectAsync();
                var login = await newSession.LoginAsync(
                    gameName,
                    slotName,
                    ItemsHandlingFlags.AllItems,
                    ProtocolVersion,
                    password: password);

                return new ConnectionAttempt(newSession, login, roomInfo.Version.ToVersion(), ProtocolVersion);
            });

            if (!result.LoginResult.Successful)
            {
                await result.Session.Socket.DisconnectAsync();
                Fail($"Server AP {result.ServerVersion}; tried protocol {result.ProtocolVersion}. {FormatLoginFailure(result.LoginResult)}");
                return;
            }

            session = result.Session;
            printedConnectionLostMessage = false;
            ConnectedServer = serverAddress;
            ConnectedSlot = slotName;
            ConnectedGame = gameName;
            Status = $"Connected to {serverAddress} as {slotName}. Server AP {result.ServerVersion}.";
            Plugin.Log.Information(Status);
            _ = RefreshHintsAsync();
        }
        catch (Exception ex)
        {
            Fail(ex.GetBaseException().Message);
        }
        finally
        {
            IsConnecting = false;
            connectionLock.Release();
        }
    }

    public void Disconnect()
    {
        try
        {
            session?.Socket.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Error while disconnecting from Archipelago.");
        }
        finally
        {
            session = null;
            printedConnectionLostMessage = false;
            ConnectedServer = string.Empty;
            ConnectedSlot = string.Empty;
            ConnectedGame = string.Empty;
            Status = "Not connected.";
            LastError = string.Empty;
            HintListStatus = "Connect to Archipelago to load hints.";
            HintsRefreshedAt = null;
            lock (currentHintsLock)
            {
                currentHints = new List<ArchipelagoHintDisplay>();
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
        connectionLock.Dispose();
        hintsRefreshLock.Dispose();
        roomInfoLock.Dispose();
    }

    private void Fail(string message)
    {
        LastError = message;
        Status = "Connection failed.";
        Plugin.Log.Warning("Archipelago connection failed: {Message}", message);
    }

    public void RefreshConnectionState()
    {
        var activeSession = session;
        if (activeSession != null && !activeSession.Socket.Connected)
        {
            MarkConnectionLost("Connection to Archipelago was lost. Reconnect to continue sending hints.");
        }
    }

    private void MarkConnectionLost(string message)
    {
        var activeSession = session;
        session = null;
        ConnectedServer = string.Empty;
        ConnectedSlot = string.Empty;
        ConnectedGame = string.Empty;
        LastError = message;
        Status = "Connection lost.";
        HintListStatus = "Reconnect to Archipelago to load hints.";
        HintsRefreshedAt = null;
        lock (currentHintsLock)
        {
            currentHints = new List<ArchipelagoHintDisplay>();
        }

        if (!printedConnectionLostMessage)
        {
            printedConnectionLostMessage = true;
            Plugin.ChatGui.PrintError($"[Archipelago] {message}");
        }

        try
        {
            activeSession?.Socket.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Error while cleaning up lost Archipelago connection.");
        }
    }

    private static string FormatLoginFailure(LoginResult loginResult)
    {
        if (loginResult is not LoginFailure failure)
        {
            return "The Archipelago server refused the connection.";
        }

        var messages = failure.Errors
            .Concat(failure.ErrorCodes.Select(error => error.ToString()))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return messages.Length == 0
            ? "The Archipelago server refused the connection."
            : string.Join(Environment.NewLine, messages);
    }

    private sealed record ConnectionAttempt(
        ArchipelagoSession Session,
        LoginResult LoginResult,
        Version ServerVersion,
        Version ProtocolVersion);

    private sealed record HintAnnouncementResult(
        ScoutedItemInfo Hint,
        string StatusMessage);

    private static async Task<RoomInfoAttempt> LoadRoomInfoFromAnyAddressAsync(string serverAddress)
    {
        var addresses = GetRoomInfoAddressAttempts(serverAddress);
        var errors = new List<string>();

        foreach (var address in addresses)
        {
            ArchipelagoSession? attemptSession = null;
            try
            {
                attemptSession = ArchipelagoSessionFactory.CreateSession(address);
                var roomInfo = await WithTimeout(
                    attemptSession.ConnectAsync(),
                    TimeSpan.FromSeconds(8),
                    $"Archipelago room info ({address})");

                return new RoomInfoAttempt(attemptSession, roomInfo, address);
            }
            catch (Exception ex)
            {
                errors.Add($"{address}: {ex.GetBaseException().Message}");

                try
                {
                    await (attemptSession?.Socket.DisconnectAsync() ?? Task.CompletedTask);
                }
                catch (Exception disconnectEx)
                {
                    Plugin.Log.Warning(disconnectEx, "Error while cleaning up failed room info attempt.");
                }
            }
        }

        throw new InvalidOperationException($"Could not load room info. {string.Join(" | ", errors)}");
    }

    private static string[] GetRoomInfoAddressAttempts(string serverAddress)
    {
        if (HasWebSocketProtocol(serverAddress))
        {
            return new[] { serverAddress };
        }

        return new[]
        {
            serverAddress,
            $"ws://{serverAddress}"
        };
    }

    private static bool HasWebSocketProtocol(string serverAddress)
    {
        return serverAddress.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
               serverAddress.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RoomInfoAttempt(
        ArchipelagoSession Session,
        RoomInfoPacket RoomInfo,
        string ServerAddress);

    public async Task<string> DispatchHintsForObjectiveAsync(
        ObjectiveDefinition objective,
        string details,
        HintRollResult roll)
    {
        var activeSession = session;
        if (activeSession == null)
        {
            return AddHintEvent($"Could not send hint for '{objective.Name}': not connected.");
        }

        if (!activeSession.Socket.Connected)
        {
            MarkConnectionLost("Connection to Archipelago was lost before sending a hint.");
            return AddHintEvent($"Could not send hint for '{objective.Name}': not connected.");
        }

        try
        {
            var knownHintedLocationIds = await GetKnownHintedLocationIdsAsync(activeSession);
            var selectedHint = await SelectRandomScoutedHintAsync(
                activeSession,
                roll.RewardType,
                knownHintedLocationIds);
            if (selectedHint == null)
            {
                return AddHintEvent($"Rolled {roll.Roll} ({roll.RewardType}), but no {roll.RewardType} hint candidates are available for '{objective.Name}'.");
            }

            var announcement = await CreateAndAnnounceHintForScoutedLocationAsync(activeSession, selectedHint, roll);
            selectedHint = announcement.Hint;
            var display = CreateHintDisplay(activeSession, selectedHint);
            PrintHintToChat(roll, display);

            _ = RefreshHintsAsync();
            var note = string.IsNullOrWhiteSpace(announcement.StatusMessage)
                ? string.Empty
                : $"{Environment.NewLine}{announcement.StatusMessage}";
            return AddHintEvent($"Rolled {roll.Roll} ({roll.RewardType}){Environment.NewLine}{display.Location} -> {display.Item} for {display.Receiver}{note}");
        }
        catch (Exception ex)
        {
            if (!activeSession.Socket.Connected)
            {
                MarkConnectionLost("Connection to Archipelago was lost while sending a hint.");
            }

            Plugin.Log.Warning(ex, "Failed to dispatch Archipelago hint.");
            return AddHintEvent($"Hint failed for '{objective.Name}': {GetDisplayExceptionMessage(ex)}");
        }
    }

    private static async Task<ScoutedItemInfo?> SelectRandomScoutedHintAsync(
        ArchipelagoSession activeSession,
        HintRewardType rewardType,
        IReadOnlySet<long> knownHintedLocationIds)
    {
        var missingLocations = activeSession.Locations.AllMissingLocations
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        if (missingLocations.Length == 0)
        {
            return null;
        }

        var timeoutAt = DateTime.UtcNow.Add(ScoutSearchTimeout);
        var searchedLocationCount = 0;
        ScoutedItemInfo? alreadyHintedCandidate = null;
        foreach (var batch in missingLocations.Chunk(ScoutBatchSize))
        {
            var remaining = timeoutAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    $"Timed out while searching for a {rewardType} hint after checking {searchedLocationCount} missing location(s).");
            }

            var requestTimeout = remaining < ScoutRequestTimeout
                ? remaining
                : ScoutRequestTimeout;
            var scouted = await WithTimeout(
                activeSession.Locations.ScoutLocationsAsync(HintCreationPolicy.None, batch),
                requestTimeout,
                "Archipelago location scout");
            searchedLocationCount += batch.Length;

            var candidates = scouted.Values
                .Where(item => GetRewardTypeForFlags(item.Flags) == rewardType)
                .ToArray();
            var unhintedCandidates = candidates
                .Where(item => !knownHintedLocationIds.Contains(item.LocationId))
                .ToArray();

            if (unhintedCandidates.Length > 0)
            {
                return unhintedCandidates[Random.Shared.Next(unhintedCandidates.Length)];
            }

            if (alreadyHintedCandidate == null && candidates.Length > 0)
            {
                alreadyHintedCandidate = candidates[Random.Shared.Next(candidates.Length)];
            }
        }

        return alreadyHintedCandidate;
    }

    private static async Task<HashSet<long>> GetKnownHintedLocationIdsAsync(ArchipelagoSession activeSession)
    {
        try
        {
            var activePlayer = activeSession.Players.ActivePlayer;
            var hintTasks = activeSession.Players.AllPlayers
                .Where(player => player.Team == activePlayer.Team && player.Slot > 0)
                .Select(player => activeSession.Hints.GetHintsAsync(player.Slot, player.Team))
                .ToArray();
            var hintSets = await WithTimeout(
                Task.WhenAll(hintTasks),
                HintRefreshTimeout,
                "Archipelago known hint lookup");

            return hintSets
                .Where(entries => entries != null)
                .SelectMany(entries => entries)
                .Select(hint => hint.LocationId)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to load known Archipelago hints before choosing a hint.");
            return new HashSet<long>();
        }
    }

    private static async Task<HintAnnouncementResult> CreateAndAnnounceHintForScoutedLocationAsync(
        ArchipelagoSession activeSession,
        ScoutedItemInfo selectedHint,
        HintRollResult roll)
    {
        string statusMessage;
        try
        {
            var announced = await WithTimeout(
                activeSession.Locations.ScoutLocationsAsync(
                    HintCreationPolicy.CreateAndAnnounce,
                    selectedHint.LocationId),
                HintAnnouncementTimeout,
                "Archipelago hint announcement");

            if (announced.TryGetValue(selectedHint.LocationId, out var announcedHint))
            {
                selectedHint = announcedHint;
            }

            statusMessage = string.Empty;
        }
        catch (TimeoutException ex)
        {
            Plugin.Log.Warning(ex, "Timed out waiting for Archipelago to confirm hint announcement.");
            TryCreateHintWithoutAnnouncement(activeSession, selectedHint, roll);
            return new HintAnnouncementResult(
                selectedHint,
                "Archipelago announcement timed out; fallback hint request was sent.");
        }

        TryUpdateHintStatus(activeSession, selectedHint, roll);
        return new HintAnnouncementResult(selectedHint, statusMessage);
    }

    private static void TryCreateHintWithoutAnnouncement(
        ArchipelagoSession activeSession,
        ScoutedItemInfo selectedHint,
        HintRollResult roll)
    {
        try
        {
            activeSession.Hints.CreateHints(roll.HintStatus, new[] { selectedHint.LocationId });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to send Archipelago fallback hint request.");
        }

        TryUpdateHintStatus(activeSession, selectedHint, roll);
    }

    private static void TryUpdateHintStatus(
        ArchipelagoSession activeSession,
        ScoutedItemInfo selectedHint,
        HintRollResult roll)
    {
        try
        {
            activeSession.Hints.UpdateHintStatus(
                activeSession.Players.ActivePlayer.Slot,
                selectedHint.LocationId,
                roll.HintStatus);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to update Archipelago hint status after announcing a hint.");
        }
    }

    public async Task RefreshHintsAsync()
    {
        if (!await hintsRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsRefreshingHints = true;
            var activeSession = session;
            if (activeSession == null)
            {
                HintListStatus = "Connect to Archipelago to load hints.";
                HintsRefreshedAt = null;
                lock (currentHintsLock)
                {
                    currentHints = new List<ArchipelagoHintDisplay>();
                }

                return;
            }

            if (!activeSession.Socket.Connected)
            {
                MarkConnectionLost("Connection to Archipelago was lost while refreshing hints.");
                return;
            }

            HintListStatus = "Refreshing hints...";
            var activePlayer = activeSession.Players.ActivePlayer;
            var hintTasks = activeSession.Players.AllPlayers
                .Where(player => player.Team == activePlayer.Team && player.Slot > 0)
                .Select(player => activeSession.Hints.GetHintsAsync(player.Slot, player.Team))
                .ToArray();
            var hintSets = await WithTimeout(
                Task.WhenAll(hintTasks),
                HintRefreshTimeout,
                "Archipelago hint refresh");
            var hints = hintSets
                .Where(entries => entries != null)
                .SelectMany(entries => entries)
                .GroupBy(hint => new
                {
                    hint.ReceivingPlayer,
                    hint.FindingPlayer,
                    hint.LocationId,
                    hint.ItemId
                })
                .Select(group => group.First())
                .ToArray();
            var displays = hints
                .OrderBy(hint => hint.Found)
                .ThenBy(hint => GetPlayerDisplay(activeSession, hint.FindingPlayer))
                .ThenBy(hint => GetLocationDisplay(activeSession, hint.LocationId, GetPlayerGame(activeSession, hint.FindingPlayer)))
                .Select(hint => CreateHintDisplay(activeSession, hint))
                .ToList();

            lock (currentHintsLock)
            {
                currentHints = displays;
            }

            HintsRefreshedAt = DateTime.Now;
            HintListStatus = displays.Count == 0
                ? "No unlocked hints found."
                : $"Loaded {displays.Count} hint(s).";
        }
        catch (Exception ex)
        {
            HintListStatus = $"Failed to refresh hints: {ex.GetBaseException().Message}";
            Plugin.Log.Warning(ex, "Failed to refresh Archipelago hints.");
        }
        finally
        {
            IsRefreshingHints = false;
            hintsRefreshLock.Release();
        }
    }

    private static void PrintHintToChat(HintRollResult roll, ArchipelagoHintDisplay display)
    {
        var hintClass = display.Tier.ToLowerInvariant();
        Plugin.ChatGui.Print(
            $"[Archipelago] Rolled {roll.Roll}. Received {hintClass} hint. | Location: {display.Location} | Item: {display.Item} | Finder: {display.Finder} | Receiver: {display.Receiver}");
    }

    private static ArchipelagoHintDisplay CreateHintDisplay(ArchipelagoSession activeSession, Hint hint)
    {
        var finderGame = GetPlayerGame(activeSession, hint.FindingPlayer);
        var receiverGame = GetPlayerGame(activeSession, hint.ReceivingPlayer);
        return new ArchipelagoHintDisplay(
            GetLocationDisplay(activeSession, hint.LocationId, finderGame),
            GetItemDisplay(activeSession, hint.ItemId, receiverGame),
            GetPlayerDisplay(activeSession, hint.FindingPlayer),
            GetPlayerDisplay(activeSession, hint.ReceivingPlayer),
            GetItemTier(hint.ItemFlags),
            hint.Found,
            hint.LocationId,
            hint.ItemId);
    }

    private static ArchipelagoHintDisplay CreateHintDisplay(ArchipelagoSession activeSession, ScoutedItemInfo hint)
    {
        return new ArchipelagoHintDisplay(
            DisplayOrFallback(hint.LocationDisplayName, $"location id {hint.LocationId}"),
            DisplayOrFallback(hint.ItemDisplayName, $"item id {hint.ItemId}"),
            GetPlayerDisplay(activeSession.Players.ActivePlayer),
            GetPlayerDisplay(hint.Player),
            GetItemTier(hint.Flags),
            false,
            hint.LocationId,
            hint.ItemId);
    }

    private static string GetItemTier(ItemFlags flags)
    {
        if (flags.HasFlag(ItemFlags.Trap))
        {
            return "Trap";
        }

        if (flags.HasFlag(ItemFlags.Advancement))
        {
            return "Progression";
        }

        if (flags.HasFlag(ItemFlags.NeverExclude))
        {
            return "Useful";
        }

        return "Filler";
    }

    private static HintRewardType GetRewardTypeForFlags(ItemFlags flags)
    {
        if (flags.HasFlag(ItemFlags.Trap))
        {
            return HintRewardType.Trap;
        }

        if (flags.HasFlag(ItemFlags.Advancement))
        {
            return HintRewardType.Progression;
        }

        if (flags.HasFlag(ItemFlags.NeverExclude))
        {
            return HintRewardType.Useful;
        }

        return HintRewardType.Filler;
    }

    private static string GetPlayerDisplay(ArchipelagoSession activeSession, int slot)
    {
        return GetPlayerDisplay(activeSession.Players.GetPlayerInfo(slot)) ?? $"slot {slot}";
    }

    private static string GetPlayerDisplay(PlayerInfo? player)
    {
        if (player == null)
        {
            return "unknown";
        }

        if (string.IsNullOrWhiteSpace(player.Alias) ||
            player.Alias.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
        {
            return DisplayOrFallback(player.Name, $"slot {player.Slot}");
        }

        return $"{player.Alias} ({player.Name})";
    }

    private static string GetPlayerGame(ArchipelagoSession activeSession, int slot)
    {
        return activeSession.Players.GetPlayerInfo(slot)?.Game ?? string.Empty;
    }

    private static string GetLocationDisplay(ArchipelagoSession activeSession, long locationId, string game)
    {
        try
        {
            return string.IsNullOrWhiteSpace(game)
                ? $"location id {locationId}"
                : activeSession.Locations.GetLocationNameFromId(locationId, game);
        }
        catch (Exception)
        {
            return $"location id {locationId}";
        }
    }

    private static string GetItemDisplay(ArchipelagoSession activeSession, long itemId, string game)
    {
        try
        {
            return string.IsNullOrWhiteSpace(game)
                ? $"item id {itemId}"
                : activeSession.Items.GetItemName(itemId, game);
        }
        catch (Exception)
        {
            return $"item id {itemId}";
        }
    }

    private static string DisplayOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private string AddHintEvent(string message)
    {
        LastHintStatus = message;
        lock (recentHintLock)
        {
            recentHintEvents.Insert(0, $"{DateTime.Now:T} {message}");
            if (recentHintEvents.Count > 12)
            {
                recentHintEvents.RemoveRange(12, recentHintEvents.Count - 12);
            }
        }

        Plugin.Log.Information(message);
        return message;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string operationName)
    {
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"{operationName} timed out after {timeout.TotalSeconds:0.#} second(s).",
                ex);
        }
    }

    private static string GetDisplayExceptionMessage(Exception ex)
    {
        return ex is TimeoutException
            ? ex.Message
            : ex.GetBaseException().Message;
    }
}

public sealed record ArchipelagoHintDisplay(
    string Location,
    string Item,
    string Finder,
    string Receiver,
    string Tier,
    bool Found,
    long LocationId,
    long ItemId);

public sealed record ArchipelagoRoomSlot(
    string Name,
    string Alias,
    int Team,
    int Slot)
{
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Alias) ||
                Alias.Equals(Name, StringComparison.OrdinalIgnoreCase))
            {
                return $"{Name} [Team {Team}, Slot {Slot}]";
            }

            return $"{Alias} ({Name}) [Team {Team}, Slot {Slot}]";
        }
    }
}

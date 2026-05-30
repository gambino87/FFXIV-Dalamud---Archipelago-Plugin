using System;
using System.Threading;
using System.Threading.Tasks;
using ArchipelagoPlugin.Models;

namespace ArchipelagoPlugin.Services;

public sealed class HintRollDisplay : IDisposable
{
    private readonly Plugin plugin;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim rollLock = new(1, 1);
    private readonly CancellationTokenSource disposeCts = new();

    private HintRollDisplaySnapshot snapshot = HintRollDisplaySnapshot.Idle;

    public HintRollDisplay(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public HintRollDisplaySnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return snapshot;
            }
        }
    }

    public bool IsBusy => rollLock.CurrentCount == 0;

    public async Task RollAndDispatchAsync(
        ObjectiveDefinition objective,
        string details,
        HintRollTable? rollTable = null)
    {
        var lockTaken = false;
        rollTable ??= HintRollTable.Default;

        try
        {
            await rollLock.WaitAsync(disposeCts.Token);
            lockTaken = true;

            var duration = TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 6001));
            var startedUtc = DateTime.UtcNow;
            var endsUtc = startedUtc.Add(duration);

            SetSnapshot(new HintRollDisplaySnapshot(
                true,
                false,
                Random.Shared.Next(1, 101),
                null,
                    objective.Name,
                    details,
                    startedUtc,
                    endsUtc,
                    "Rolling...",
                    string.Empty));

            while (DateTime.UtcNow < endsUtc)
            {
                disposeCts.Token.ThrowIfCancellationRequested();
                var displayRoll = Random.Shared.Next(1, 101);
                SetSnapshot(new HintRollDisplaySnapshot(
                    true,
                    false,
                    displayRoll,
                    HintRoller.GetRewardType(displayRoll, rollTable),
                    objective.Name,
                    details,
                    startedUtc,
                    endsUtc,
                    "Rolling...",
                    string.Empty));

                await Task.Delay(75, disposeCts.Token);
            }

            var finalRoll = HintRoller.Roll(rollTable);
            SetSnapshot(new HintRollDisplaySnapshot(
                false,
                true,
                finalRoll.Roll,
                finalRoll.RewardType,
                objective.Name,
                details,
                startedUtc,
                DateTime.UtcNow,
                "Revealing hint...",
                string.Empty));

            var revealedHint = await plugin.Archipelago.DispatchHintsForObjectiveAsync(
                objective,
                details,
                finalRoll);

            SetSnapshot(new HintRollDisplaySnapshot(
                false,
                true,
                finalRoll.Roll,
                finalRoll.RewardType,
                objective.Name,
                details,
                startedUtc,
                DateTime.UtcNow,
                $"Locked: {finalRoll.Roll} ({finalRoll.RewardType})",
                revealedHint));
        }
        catch (OperationCanceledException)
        {
            SetSnapshot(HintRollDisplaySnapshot.Idle);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Hint roll animation failed.");
            SetSnapshot(new HintRollDisplaySnapshot(
                false,
                false,
                0,
                null,
                objective.Name,
                details,
                DateTime.UtcNow,
                DateTime.UtcNow,
                $"Roll failed: {ex.GetBaseException().Message}",
                string.Empty));
        }
        finally
        {
            if (lockTaken)
            {
                rollLock.Release();
            }
        }
    }

    public void Dispose()
    {
        disposeCts.Cancel();
    }

    private void SetSnapshot(HintRollDisplaySnapshot nextSnapshot)
    {
        lock (stateLock)
        {
            snapshot = nextSnapshot;
        }
    }
}

public sealed record HintRollDisplaySnapshot(
    bool IsRolling,
    bool HasResult,
    int DisplayRoll,
    HintRewardType? RewardType,
    string ObjectiveName,
    string Details,
    DateTime StartedUtc,
    DateTime EndsUtc,
    string Status,
    string RevealedHint)
{
    public static HintRollDisplaySnapshot Idle { get; } = new(
        false,
        false,
        0,
        null,
        string.Empty,
        string.Empty,
        DateTime.MinValue,
        DateTime.MinValue,
        "No active roll.",
        string.Empty);

    public float Progress
    {
        get
        {
            if (!IsRolling)
            {
                return HasResult ? 1f : 0f;
            }

            var total = EndsUtc - StartedUtc;
            if (total <= TimeSpan.Zero)
            {
                return 1f;
            }

            var elapsed = DateTime.UtcNow - StartedUtc;
            return Math.Clamp((float)(elapsed.TotalMilliseconds / total.TotalMilliseconds), 0f, 1f);
        }
    }
}

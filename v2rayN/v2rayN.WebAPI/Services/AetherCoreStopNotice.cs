namespace v2rayN.WebAPI.Services;

/// <summary>
/// PattN (critique 32): what CoreManager's stop of the cores, once the Aether core exited on its own, does to the WebAPI's
/// runtime state. A running core is faulted with the core's own words (<see cref="V2rayRuntime.AetherStoppedRuntime" />).
/// A start under way owns the state, which says Starting or Restarting until it is over; the words are kept for it, and
/// it takes them as its failure (<see cref="Take" />). The most common end of an Aether core, at once on a missing DLL, a
/// refused key or a port taken, comes while the start still waits for its listener.
/// </summary>
internal sealed class AetherCoreStopNotice
{
    private string? _pending;

    /// <summary>
    /// The state once the cores were stopped with <paramref name="message" />, as <paramref name="current" /> gives the
    /// state now; null when a start owns it and keeps the message, or when there is nothing to fault. A start that set
    /// its state between the two readings, and found nothing to take, gets the message here instead.
    /// </summary>
    public CoreRuntimeSnapshot? Apply(Func<CoreRuntimeSnapshot> current, string message)
    {
        var snapshot = current();
        if (!StartOwns(snapshot))
        {
            return V2rayRuntime.AetherStoppedRuntime(snapshot, message);
        }
        Interlocked.Exchange(ref _pending, message);
        var now = current();
        if (StartOwns(now) || Take() is not { } taken)
        {
            return null;
        }
        return V2rayRuntime.AetherStoppedRuntime(now, taken);
    }

    /// <summary>A start begins: what came before it is no failure of it.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _pending, null);
    }

    /// <summary>The message a start under way was left, once; null when there is none.</summary>
    public string? Take()
    {
        return Interlocked.Exchange(ref _pending, null);
    }

    private static bool StartOwns(CoreRuntimeSnapshot snapshot) =>
        snapshot.State is CoreRuntimeState.Starting or CoreRuntimeState.Restarting;
}

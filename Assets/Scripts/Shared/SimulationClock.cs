/// <summary>
/// Simulation time shared by client, server and network emulation.
/// Everything that affects the experiment (input, cheats, packet delay, timestamps)
/// uses this tick-based clock instead of Unity's frame time, so a session produces
/// the same result whether it runs in real time or fast-forwarded.
/// </summary>
public static class SimulationClock
{
    public static uint Tick { get; private set; }

    public static float Time => Tick * GameConstants.FixedDeltaTime;

    public static void Reset()
    {
        Tick = 0;
    }

    public static void Advance()
    {
        Tick++;
    }

    /// <summary>
    /// Number of whole ticks a packet waits before the receiver can process it.
    /// The server and client only read packets at tick boundaries, so a delay is
    /// rounded up to the next tick (50 ms -> 2 ticks = 66.7 ms at 30 Hz).
    /// </summary>
    public static uint DelayToTicks(float delaySeconds)
    {
        if (delaySeconds <= 0f) return 0;
        return (uint)System.Math.Ceiling(delaySeconds / GameConstants.FixedDeltaTime - 1e-4);
    }
}

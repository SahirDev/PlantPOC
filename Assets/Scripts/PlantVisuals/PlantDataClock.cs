using UnityEngine;

/// <summary>
/// One clock for all live values: boiler, turbines and electrical panels take a new reading every
/// <see cref="Interval"/> seconds, all at the same moment. The monitors in the rooms and the Plant UI panels only
/// show those readings, so they always show the same numbers and change together. Moving a slider takes a new
/// reading at once (the source bumps its version).
/// </summary>
public static class PlantDataClock
{
    public const float Interval = 5f;

    /// <summary>Number of the current 5-second period (real time, also while the game is paused).</summary>
    public static int Tick => Mathf.FloorToInt(Time.unscaledTime / Interval);
}

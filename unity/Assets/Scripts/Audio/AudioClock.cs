using System.Diagnostics;

namespace FlightRecord.Audio
{
    /// <summary>
    /// The clock every audio-integration timestamp uses: <see cref="Stopwatch"/> in seconds.
    /// Monotonic and high resolution (QueryPerformanceCounter on Windows, the same clock as
    /// Python's <c>time.perf_counter()</c>), unlike <c>Time.time</c>, which only advances per
    /// frame and is unreadable off the main thread.
    /// </summary>
    public static class AudioClock
    {
        static readonly double SecondsPerTick = 1.0 / Stopwatch.Frequency;

        /// <summary>Seconds since an arbitrary start. Safe to read from any thread.</summary>
        public static double Now => Stopwatch.GetTimestamp() * SecondsPerTick;
    }
}

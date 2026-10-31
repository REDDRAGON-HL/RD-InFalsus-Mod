using System;
using System.Diagnostics;

namespace CustomSong
{
    internal static class Timing
    {
        internal static long Now => Stopwatch.GetTimestamp();

        internal static double Ms(long since) =>
            (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        internal static void Report(string phase, long since) =>
            ModLog.Load($"timing: {phase} {Ms(since):F0} ms");
    }
}

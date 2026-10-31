using System;

namespace CustomSong
{
    internal static class ModLog
    {
        internal static void Info(string message) => Console.WriteLine("info:  " + message);
        internal static void Load(string message) => Console.WriteLine("load:  " + message);
        internal static void Warn(string message) => Console.WriteLine("warn:  " + message);
        internal static void WarnRelease(string message) => Console.WriteLine("warn:  " + message);
        internal static void Error(string message) => Console.WriteLine("error: " + message);
        internal static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;
    }

    internal static class AudioCodec
    {
        internal static int Measure(string path, out string reason)
        {
            reason = null;
            return 120;
        }
    }
}

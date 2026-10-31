using System.Diagnostics;

namespace CustomSong
{
    internal static class ModLog
    {
        private static class Sink
        {
#if MELONLOADER
            internal static void Msg(string message) => MelonLoader.MelonLogger.Msg(message);
            internal static void Error(string message) => MelonLoader.MelonLogger.Error(message);
            internal static void Warning(string message) => MelonLoader.MelonLogger.Warning(message);
#elif BEPINEX
            private static readonly BepInEx.Logging.ManualLogSource Source =
                BepInEx.Logging.Logger.CreateLogSource("CustomSong");
            internal static void Msg(string message) => Source.LogInfo(message);
            internal static void Error(string message) => Source.LogError(message);
            internal static void Warning(string message) => Source.LogWarning(message);
#else
#error Define MELONLOADER or BEPINEX.
#endif
        }

        [Conditional("DEBUG")]
        internal static void Info(string message)
        {
        }

        internal static void Load(string message) => Sink.Msg(message);
        internal static void Error(string message) => Sink.Error(message);
        internal static void WarnRelease(string message) => Sink.Warning(message);

        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
        }

        internal static string Describe(System.Exception e)
        {
            if (e == null) return "<null>";

            return e.GetType().Name;
        }
    }
}

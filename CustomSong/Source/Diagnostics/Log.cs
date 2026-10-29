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
#if DEBUG
            Sink.Msg(message);
#endif
        }

        internal static void Load(string message) => Sink.Msg(message);
        internal static void Error(string message) => Sink.Error(message);
        internal static void WarnRelease(string message) => Sink.Warning(message);

        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
#if DEBUG
            Sink.Warning(message);
#endif
        }

        internal static string Describe(System.Exception e)
        {
            if (e == null) return "<null>";

#if DEBUG
            var sb = new System.Text.StringBuilder();
            for (System.Exception x = e; x != null; x = x.InnerException)
            {
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(x.GetType().Name).Append(": ").Append(x.Message);
            }
            return sb.ToString();
#else
            return e.GetType().Name;
#endif
        }
    }
}

using System;

namespace CustomSong.Auto
{
    internal sealed class AutoSong
    {
        private const double RewindMs = 500.0;
        private const double RestartDropMs = 1500.0;
        internal static AutoSong Current { get; private set; } = new AutoSong(IntPtr.Zero);
        private readonly IntPtr _owner;
        private double _lastMs = -1.0;
        internal Chart Chart { get; } = new Chart();
        internal Floor Floor { get; }
        internal Sky Sky { get; } = new Sky();
        internal SkyBar Bar { get; } = new SkyBar();

        internal AutoSong(IntPtr owner, Floor previous = null)
        {
            _owner = owner;
            JudgementPoint.Reset();
            Floor = new Floor(Chart, previous);
        }

        internal static void Observe(IntPtr lnp, double chartSeconds)
        {
            if (lnp != Current._owner)
            {
                AutoMark.Ran = false;
                ModLog.Load($"autoplay: chart start ({(AutoSwitch.On ? "on" : "off")})");
                AutoResults.BlockParticles(AutoSwitch.On);
                Current = new AutoSong(lnp, Current.Floor);
            }

            Current.Chart.TryLoad(lnp, chartSeconds * 1000.0);
        }

        internal static void Frame(IntPtr engine, double seconds)
        {
            if (!Memory.LooksLikeObject(engine)) return;
            double nowMs = Memory.F64(engine + AutoOffsets.Engine.ChartTime) * 1000.0;
            if (nowMs <= 0.0) return;
            double dropMs = Current._lastMs - nowMs;

            if (Current._lastMs >= 0.0 && dropMs > RewindMs && IsRestart(Current.Chart, dropMs, nowMs))
                Current = new AutoSong(Current._owner, Current.Floor);
            AutoSong song = Current;
            song._lastMs = nowMs;
            if (AutoHooks.Writing) song.Play(engine, seconds, nowMs);
            else song.LetGoLanes(engine, seconds);
        }

        private static bool IsRestart(Chart chart, double dropMs, double nowMs)
        {
            if (dropMs > RestartDropMs) return true;
            if (!chart.Loaded) return true;
            double opening = Math.Max(chart.Notes[0].StartMs, RewindMs);
            return nowMs <= opening;
        }

        internal static void LetGo(IntPtr engine, double seconds) => Current.LetGoLanes(engine, seconds);

        private void LetGoLanes(IntPtr engine, double seconds)
        {
            if (!Floor.HoldsAny) return;
            IntPtr lanes = AutoMemory.InputLanes(engine);
            if (lanes != IntPtr.Zero) Floor.LetGo(lanes, seconds);
        }

        private void Play(IntPtr engine, double seconds, double nowMs)
        {
            Sky.AimAtBar(Chart, nowMs);
            Sky.Hold(engine);
            IntPtr lanes = AutoMemory.InputLanes(engine);
            if (lanes != IntPtr.Zero) Floor.Tick(lanes, seconds, nowMs);
        }
    }
}

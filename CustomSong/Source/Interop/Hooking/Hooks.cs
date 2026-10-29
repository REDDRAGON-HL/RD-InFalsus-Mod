using System;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        internal static bool Faulted { get; private set; }

        internal static int Faults { get; private set; }

        internal static long VaCalls, VaServed;
        internal static long DjaCalls, DjaHits;
        internal static long ZgaCalls, ZgaServed;
        internal static long ApACalls, ApAArmed;
        internal static long ApaAsked;
        internal static long MiaCalls, MiaClaimed;
        internal static long ZoaCalls, ZoaArmed, LiaCalls, LiaClaimed;
        internal static long UmaCalls, UmaArmed;
        internal static long UmaUnset;
        internal static long ChartStarts;
        internal static long MusicPauses, MusicResumes;

#if DEBUG
        internal static long TicksApa, TicksMia, TicksVc, TicksRead;
        internal static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
        internal static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif

        internal static int Installed { get; private set; }
        internal static int Attempted { get; private set; }

        internal static void Install()
        {
            int installed = 0;
            int attempted = 0;

            void Count(bool ok) { attempted++; if (ok) installed++; }

            Count(InstallChartLoad());
            Count(InstallChartStart());
            Count(InstallChartSetup());
            Count(InstallPause());
            Count(InstallAudioLoad());
            Count(InstallJacket());
            Count(InstallSongSelect());
            Count(InstallDifficultyState());
            Count(InstallPackVisual());
            Count(InstallScoreStore());
            Count(InstallResultsUnlock());
            Count(InstallPlayGate());
            Count(InstallSceneSwitch());
            Count(InstallSelectAwake());
            Installed = installed;
            Attempted = attempted;
            ModLog.Load($"{installed}/{attempted} hooks installed");
            if (installed < attempted)
                ModLog.Warn("a hook is missing; whatever it drives will silently do nothing");
        }

        internal static void Uninstall()
        {
            DetachSelectAwake();
            DetachSceneSwitch();
            DetachPlayGate();
            DetachResultsUnlock();
            DetachScoreStore();
            DetachPackVisual();
            DetachDifficultyState();
            DetachSongSelect();
            DetachJacket();
            DetachAudioLoad();
            DetachPause();
            DetachChartStart();
            DetachChartSetup();
            DetachChartLoad();
        }

        internal static unsafe byte[] Prologue(IntPtr target, int count = 8)
        {
            if (target == IntPtr.Zero) return null;
            var bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = *(byte*)(target + i);
            return bytes;
        }

        private static unsafe bool Patched(IntPtr target, byte[] prologue)
        {
            if (target == IntPtr.Zero || prologue == null) return false;

            for (int i = 0; i < prologue.Length; i++)
                if (*(byte*)(target + i) != prologue[i]) return true;
            return false;
        }

        internal enum Hook
        {
            ChartLoader,
            AudioRead,
            AudioName,
            JacketReference,
            JacketMaterial,
            ChartReference,
            ChartMaterial,
            Background,
            ChartStart,
            ChartSetup,
            MusicPause,
            MusicResume,
            SongSelectApply,
            PreviewRecord,
            DifficultyState,
            PackVisual,
            ResultRead,
            ResultReadFull,
            ResultUpdate,
            EncounterResult,
            ResultsUnlock,
            PlayGate,
            SceneSwitch,
        }

        private static bool Landed(Hook hook, IntPtr target, byte[] prologue)
        {
            if (Patched(target, prologue)) return true;
            NotLanded(hook);
            return false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void NotLanded(Hook hook)
        {
#if DEBUG
            ModLog.Warn($"{Name(hook)} was attached but its first bytes are unchanged; the detour " +
                             "is not in place, and whatever it answers for will silently do nothing");
#endif
        }

#if DEBUG

        private static string Name(Hook hook) => hook switch
        {
            Hook.ChartLoader => "_s._VA",
            Hook.AudioRead => "_J._hF._ZgA",
            Hook.AudioName => "_BG._DJA",
            Hook.JacketReference => "_apA",
            Hook.JacketMaterial => "_MIA",
            Hook.ChartReference => "SongData._ZOA",
            Hook.ChartMaterial => "AddressableHandleAutoReleaser._LIA",
            Hook.Background => "GameplayBackgrounds._UmA",
            Hook.ChartStart => "GameScene._rk",
            Hook.ChartSetup => "LogicalNotePlayer._Ib",
            Hook.MusicPause => "_Dg._hiA",
            Hook.MusicResume => "_Dg._KiA",
            Hook.SongSelectApply => "SongSelectScene._MN",
            Hook.PreviewRecord => "SongSelectScene._qN",
            Hook.DifficultyState => "_NH._UoA",
            Hook.PackVisual => "PackVisualMemberLarge._vc",
            Hook.ResultRead => "GameResultsV4.TryGetResult(song)",
            Hook.ResultReadFull => "GameResultsV4.TryGetResult(song, full)",
            Hook.ResultUpdate => "GameResultsV4.TryUpdate",
            Hook.EncounterResult => "EncounterResults.UpdateEncounterResult",
            Hook.ResultsUnlock => "DifficultyUnlockContainer._GB",
            Hook.PlayGate => "SongSelectScene._sN",
            Hook.SceneSwitch => "CoreScene._CB.MoveNext",
            _ => "(unknown hook)",
        };
#endif

        private static void Fault(Hook hook, Exception e)
        {
            Faulted = true;
            Faults++;
            if (Faults > 1) return;
            ModLog.Error($"{Which(hook)}detour faulted, custom charts disabled: {ModLog.Describe(e)}");
        }

        private static string Which(Hook hook)
        {
#if DEBUG
            return Name(hook) + " ";
#else
            return "a ";
#endif
        }
    }
}

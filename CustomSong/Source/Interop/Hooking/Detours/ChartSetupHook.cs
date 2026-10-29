using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        private const string NotePlayerImage = "Game.dll";
        private const string NotePlayerNamespace = "ifapp.Game";
        private const int TupleNotes = 0x10;
        private const int TupleEvents = 0x18;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ChartSetupFn(IntPtr self, IntPtr name, long id, IntPtr chart,
                                           IntPtr sourceText, byte flag);
        private static NativeHook<ChartSetupFn> _chartSetup;
        private static ChartSetupFn _chartSetupTramp;

        private static bool InstallChartSetup()
        {
            IntPtr target = MethodResolver.ByRuntime("LogicalNotePlayer", "_Ib",
                                                     NotePlayerImage, NotePlayerNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("LogicalNotePlayer._Ib could not be found; a chart the game refuses " +
                                 "will not say where it stopped");
                return false;
            }

            byte[] prologue = Prologue(target);
            _chartSetup = new NativeHook<ChartSetupFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, long, IntPtr, IntPtr, byte, void>)
                    &ChartSetupDetour,
            };
            _chartSetup.Attach();
            _chartSetupTramp = _chartSetup.Trampoline;
            return Landed(Hook.ChartSetup, target, prologue);
        }

        private static void DetachChartSetup()
        {
            _chartSetup?.Detach();
            _chartSetup = null;
            _chartSetupTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ChartSetupDetour(IntPtr self, IntPtr name, long id, IntPtr chart,
                                             IntPtr sourceText, byte flag)
        {
            try
            {
                int notes = Count(chart, TupleNotes);
                int events = Count(chart, TupleEvents);
                _chartSetupTramp(self, name, id, chart, sourceText, flag);
            }
            catch (Exception e)
            {
                ModLog.Error($"the game could not set this chart up: {e.GetType().Name}");
                ModLog.Error($"  pools: {Pools(self)}");
                throw;
            }
        }

        private static int Count(IntPtr chart, int slot)
        {
            IntPtr list = Memory.Ptr(chart + slot);
            return Memory.LooksLikeObject(list) ? Memory.I32(list + Offsets.Runtime.ListSize) : -1;
        }

        private static string Pools(IntPtr self)
        {
            IntPtr notes = Memory.Ptr(self + ChartSource);
            if (!Memory.LooksLikeObject(notes)) return "<no chart>";
            var text = new System.Text.StringBuilder();
            text.Append($"points {Memory.I32(notes + PointsUsed)}/{ArrayLength(Memory.Ptr(notes + PointsArray))}");

            (string Name, int Slot)[] memories =
            {
                ("events", 0x50), ("perNote", 0x60), ("perTick", 0x70),
                ("doublesA", 0x80), ("doublesB", 0x90), ("doublesC", 0xA0),
                ("points", 0xB0), ("pointsB", 0xC8),
            };

            foreach ((string which, int slot) in memories)
                text.Append($" {which} {Memory.I32(notes + slot + MemorySize)}");
            text.Append($" fields {Memory.I32(notes + 0xD8)}/{Memory.I32(notes + 0xDC)}/{Memory.I32(notes + 0xE0)}");
            text.Append($" lastPoint {LastPoint(notes):F0}ms");
            return text.ToString();
        }

        private static double LastPoint(IntPtr chart)
        {
            int used = Memory.I32(chart + PointsUsed);
            IntPtr array = Memory.Ptr(chart + PointsArray);
            if (used <= 0 || !Memory.LooksLikeObject(array)) return -1;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (used > length) return -2;
            return Memory.F64(array + Offsets.Runtime.ArrayDataOffset + (used - 1) * PointSize + PointMs);
        }

        private const int PointSize = 40;
        private const int PointMs = 0x28;

        private static int ArrayLength(IntPtr array) =>
            Memory.LooksLikeObject(array) ? Memory.I32(array + Offsets.Runtime.ArrayLength) : -1;
        private const int ChartSource = 0x40;
        private const int PointsArray = 0xC0;
        private const int PointsUsed = 0xD8;
        private const int MemorySize = 0x0C;
    }
}

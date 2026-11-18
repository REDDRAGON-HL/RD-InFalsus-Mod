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
            Alive(Hook.ChartSetup);

            try
            {
                int notes = Count(chart, TupleNotes);
                int events = Count(chart, TupleEvents);
                _chartSetupTramp(self, name, id, chart, sourceText, flag);
                HideSkyNoteSquares(self);
            }
            catch (Exception e)
            {
                ModLog.Error($"the game could not set this chart up: {e.GetType().Name}");
                ModLog.Error($"  detail: {e}");
                if (e.InnerException != null) ModLog.Error($"  inner: {e.InnerException}");
                ModLog.Error($"  pools: {Pools(self)}");
                Slices(self, chart);
                throw;
            }
        }

        private static void HideSkyNoteSquares(IntPtr self)
        {
            try
            {
                IntPtr chart = Memory.Ptr(self + ChartSource);
                if (!Memory.LooksLikeObject(chart)) return;

                IntPtr dict = Memory.Ptr(chart + Auto.AutoOffsets.ChartObject.PlaneArrays);
                if (!Memory.LooksLikeObject(dict)) return;

                IntPtr entries = Memory.Ptr(dict + Auto.AutoOffsets.Runtime.DictEntries);
                if (!Memory.LooksLikeObject(entries)) return;

                int capacity = Memory.I32(entries + Auto.AutoOffsets.Runtime.ListSize);
                if (capacity <= 0 || capacity > 256) return;

                int stride = Auto.AutoOffsets.Note.Size > 0 ? Auto.AutoOffsets.Note.Size : 0x80;
                IntPtr data = entries + Auto.AutoOffsets.Runtime.ArrayDataOffset;
                int hidden = 0;

                for (int i = 0; i < capacity; i++)
                {
                    int slot = i * Auto.AutoOffsets.Runtime.DictEntrySize;
                    if (Memory.I32(data + slot) < 0) continue;

                    IntPtr holder = Memory.Ptr(data + slot + Auto.AutoOffsets.Runtime.DictEntryValue);
                    if (!Memory.LooksLikeObject(holder)) continue;

                    IntPtr array = Memory.Ptr(holder + Auto.AutoOffsets.PlaneHolder.NoteArray);
                    if (!Memory.LooksLikeObject(array)) continue;

                    int slots = Memory.I32(array + Auto.AutoOffsets.Runtime.ListSize);
                    if (slots <= 0 || slots > 200000) continue;

                    int size = Memory.I32(holder + Auto.AutoOffsets.PlaneHolder.Count);
                    if (size <= 0 || size > slots) continue;

                    IntPtr notes = array + Auto.AutoOffsets.Runtime.ArrayDataOffset;
                    for (int n = 0; n < size; n++)
                    {
                        IntPtr note = notes + n * stride;
                        if ((Memory.I32(note + Auto.AutoOffsets.Note.Flags) & SkyNoteFlag) == 0) continue;

                        ZeroBand(note + WidthBand1);
                        ZeroBand(note + WidthBand2);
                        hidden++;
                    }
                }

                if (hidden > 0) ModLog.Load($"skynote: {hidden} ground square(s) hidden");
            }
            catch (Exception e)
            {
                ModLog.Warn("the skynote squares could not be hidden: " + ModLog.Describe(e));
            }
        }

        private static void ZeroBand(IntPtr band)
        {
            Memory.WriteI32(band, 0);
            Memory.WriteF32(band + 8, 0f);
        }

        private const int WidthBand1 = 0x38;

        private const int WidthBand2 = 0x44;

        private const int SkyNoteFlag = 0x10000;

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

        private static void Slices(IntPtr self, IntPtr chart)
        {
            try
            {
                int notes = Count(chart, TupleNotes);
                IntPtr loaded = Memory.Ptr(self + ChartObject);
                IntPtr dict = Memory.LooksLikeObject(loaded) ? Memory.Ptr(loaded + ChartJudgements) : IntPtr.Zero;
                if (!Memory.LooksLikeObject(dict))
                {
                    ModLog.Error("  slices: the judgement dictionary is not readable");
                    return;
                }

                IntPtr entries = Memory.Ptr(dict + DictEntries);
                if (!Memory.LooksLikeObject(entries))
                {
                    ModLog.Error("  slices: the judgement dictionary has no entries array");
                    return;
                }

                int capacity = ArrayLength(entries);
                if (capacity <= 0 || capacity > 200000)
                {
                    ModLog.Error($"  slices: the judgement dictionary holds {capacity} slot(s)");
                    return;
                }

                IntPtr data = entries + Offsets.Runtime.ArrayDataOffset;
                var counts = new System.Collections.Generic.Dictionary<long, int>();
                var present = new System.Collections.Generic.HashSet<long>();
                for (int i = 0; i < capacity; i++)
                {
                    IntPtr entry = data + i * EntryStride;
                    if (Memory.I32(entry) < 0) continue;
                    long id = Memory.I64(entry + EntryKey);
                    present.Add(id);
                    counts[id] = Memory.I32(entry + EntryValue + MemorySize);
                }

                int empty = 0;
                long firstEmpty = -1;
                long total = 0;
                var empties = new System.Text.StringBuilder();
                foreach (System.Collections.Generic.KeyValuePair<long, int> item in counts)
                {
                    total += item.Value;
                    if (item.Value > 0) continue;
                    empty++;
                    if (firstEmpty < 0) firstEmpty = item.Key;
                    if (empty <= 40) empties.Append(item.Key).Append(' ');
                }

                var window = new System.Text.StringBuilder();
                for (long id = 1060; id <= 1100; id++)
                    window.Append(id).Append(':')
                          .Append(counts.TryGetValue(id, out int c) ? c : -1).Append(' ');

                ModLog.Error($"  slices: {empty} of {present.Count} empty, {total} point(s), " +
                             $"first empty id={firstEmpty}");
                ModLog.Error($"  empty ids: {empties}");
                ModLog.Error($"  counts 1060..1100: {window}");
                int missing = 0;
                long first = -1;
                for (long id = 0; id < notes; id++)
                {
                    if (present.Contains(id)) continue;
                    missing++;
                    if (first < 0) first = id;
                }

                if (missing > 0) ModLog.Error($"  slices: {missing}/{notes} note(s) have no slice, first id={first}");
            }
            catch (Exception e)
            {
                ModLog.Error("  slices: could not be read: " + ModLog.Describe(e));
            }
        }

        private const int ChartSource = 0x40;
        private const int PointsArray = 0xC0;
        private const int PointsUsed = 0xD8;
        private const int ChartObject = 0x40;
        private const int ChartJudgements = 0x38;
        private const int DictEntries = 0x18;
        private const int EntryStride = 32;
        private const int EntryKey = 8;
        private const int EntryValue = 16;
        private const int MemorySize = 0x0C;
    }
}

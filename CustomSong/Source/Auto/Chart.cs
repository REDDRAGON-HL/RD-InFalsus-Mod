using System;
using System.Collections.Generic;

namespace CustomSong.Auto
{
    internal sealed class Chart
    {
        internal ChartNote[] Notes = Array.Empty<ChartNote>();

        internal int SkyCount { get; private set; }

        internal int Revision { get; private set; }

        internal bool Loaded { get; private set; }

        private const int MaxAttempts = 600;
        private const int MaxReReads = 6;
        private const double ReloadGapMs = 3000.0;
        private int _reads;
        private int _attempts;
        private double _lastReadMs = double.NegativeInfinity;

        internal bool TryLoad(IntPtr lnp, double nowMs)
        {
            if (!Memory.LooksLikeObject(lnp)) return false;
            if (Loaded && !ShouldReRead(nowMs)) return true;

            if (!Loaded && _attempts >= MaxAttempts)
            {
                if (_attempts == MaxAttempts)
                    ModLog.Warn($"autoplay: chart could not be read after {MaxAttempts} frames; " +
                                "it will do nothing this song");
                _attempts++;
                return false;
            }
            _attempts++;

            try
            {
                bool ok = TryReadViaPlanes(lnp, out ChartNote[] notes);
                _reads++;
                _lastReadMs = nowMs;

                if (!ok)
                {
                    ModLog.Warn("autoplay: the chart read found nothing");
                    return false;
                }

                Publish(notes);
                return true;
            }
            catch (Exception e)
            {
                ModLog.Warn($"autoplay: chart read failed: {ModLog.Describe(e)}");
                return false;
            }
        }

        private bool ShouldReRead(double nowMs)
        {
            if (_reads >= MaxReReads) return false;
            if (nowMs - _lastReadMs < ReloadGapMs) return false;
            return SkyCount == 0;
        }

        private static bool TryReadViaPlanes(IntPtr lnp, out ChartNote[] notes)
        {
            notes = null;
            IntPtr chart = Memory.Ptr(lnp + AutoOffsets.NotePlayer.ChartObject);
            if (!Memory.LooksLikeObject(chart)) return false;
            IntPtr dict = Memory.Ptr(chart + AutoOffsets.ChartObject.PlaneArrays);
            if (!Memory.LooksLikeObject(dict)) return false;
            IntPtr entries = Memory.Ptr(dict + AutoOffsets.Runtime.DictEntries);
            if (!Memory.LooksLikeObject(entries)) return false;
            int capacity = Memory.I32(entries + AutoOffsets.Runtime.ListSize);
            if (capacity <= 0 || capacity > 256) return false;
            var all = new List<ChartNote>();
            IntPtr data = entries + AutoOffsets.Runtime.ArrayDataOffset;

            for (int i = 0; i < capacity; i++)
            {
                int slot = i * AutoOffsets.Runtime.DictEntrySize;
                if (Memory.I32(data + slot) < 0) continue;
                IntPtr holder = Memory.Ptr(data + slot + AutoOffsets.Runtime.DictEntryValue);
                if (!Memory.LooksLikeObject(holder)) continue;
                IntPtr arr = Memory.Ptr(holder + AutoOffsets.PlaneHolder.NoteArray);
                if (!Memory.LooksLikeObject(arr)) continue;
                int slots = Memory.I32(arr + AutoOffsets.Runtime.ListSize);
                if (slots <= 0 || slots > 200000) continue;
                int size = Memory.I32(holder + AutoOffsets.PlaneHolder.Count);
                if (size <= 0 || size > slots) continue;
                int stride = FieldResolver.ElementSize(arr, "Note.Size");
                if (stride > 0) AutoOffsets.Note.Size = stride;
                if (Collect(arr, size, out ChartNote[] part)) all.AddRange(part);
            }

            if (all.Count == 0) return false;
            notes = all.ToArray();
            SortByTime(notes);
            return true;
        }

        private static bool Collect(IntPtr array, int size, out ChartNote[] notes)
        {
            var list = new List<ChartNote>(size);
            int badSide = 0, badTime = 0;

            for (int i = 0; i < size; i++)
            {
                IntPtr p = array + AutoOffsets.Runtime.ArrayDataOffset + i * AutoOffsets.Note.Size;
                int side = Memory.I32(p + AutoOffsets.Note.Side);
                if (side < AutoOffsets.Plane.None || side > AutoOffsets.Plane.Sky) { badSide++; continue; }

                int start = Memory.I32(p + AutoOffsets.Note.StartMs);
                int end = Memory.I32(p + AutoOffsets.Note.EndMs);

                if (start < 0 || end < start || start > 6 * 60 * 60 * 1000) { badTime++; continue; }

                list.Add(new ChartNote(
                    start, end,
                    Memory.I32(p + AutoOffsets.Note.LaneFirst),
                    Memory.I32(p + AutoOffsets.Note.LaneLast),
                    Memory.I32(p + AutoOffsets.Note.Type),
                    side,
                    Memory.F32(p + AutoOffsets.Note.StartX),
                    Memory.F32(p + AutoOffsets.Note.EndX),
                    Memory.F32(p + AutoOffsets.Note.StartWidth),
                    Memory.F32(p + AutoOffsets.Note.EndWidth),
                    Memory.I32(p + AutoOffsets.Note.Flags),
                    Memory.I64(p + AutoOffsets.Note.Id)));
            }

            if (badSide > 0 || badTime > 0)
                ModLog.Warn($"autoplay: chart read dropped {badSide} bad side, {badTime} bad time, " +
                            $"of {size}");

            if (list.Count == 0) { notes = null; return false; }

            notes = list.ToArray();
            return true;
        }

        private static void SortByTime(ChartNote[] notes) =>
            Array.Sort(notes, (a, b) =>
            {
                int c = a.StartMs.CompareTo(b.StartMs);
                return c != 0 ? c : a.NoteId.CompareTo(b.NoteId);
            });

        private void Publish(ChartNote[] notes)
        {
            int sky = 0;
            foreach (ChartNote n in notes) if (n.IsSky) sky++;
            Notes = notes;
            SkyCount = sky;
            Loaded = true;
            Revision++;
        }

    }
}

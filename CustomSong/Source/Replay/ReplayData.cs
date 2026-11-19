using System;
using System.Collections.Generic;
using CustomSong.Auto;

namespace CustomSong.Replay
{
    internal sealed class ReplayData
    {
        internal struct Tick
        {
            internal double Ms;
            internal byte Held;
            internal byte Edges;
            internal float Raw;
            internal float Clamped;
        }

        internal struct Point
        {
            internal byte Grade;
            internal byte Kind;
            internal double Delta;
            internal bool Recorded;
            internal bool Checked;
        }

        internal readonly List<Tick> Ticks = new List<Tick>(4096);
        internal readonly Dictionary<long, Point[]> Points = new Dictionary<long, Point[]>(1024);
        internal int NoteCount;
        internal int FirstMs;
        internal int LastMs;
        internal long Checksum;
        internal int Recorded;

        internal bool Matches(Chart chart)
        {
            if (!chart.Loaded || chart.Notes.Length != NoteCount) return false;
            if (NoteCount == 0) return true;
            if (chart.Notes[0].StartMs != FirstMs) return false;
            if (chart.Notes[NoteCount - 1].StartMs != LastMs) return false;
            long checksum = 0;
            for (int i = 0; i < chart.Notes.Length; i++) checksum ^= chart.Notes[i].NoteId;
            return checksum == Checksum;
        }

        internal int TickAt(double ms)
        {
            int low = 0, high = Ticks.Count - 1, found = -1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (Ticks[mid].Ms <= ms) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }
            return found;
        }
    }
}

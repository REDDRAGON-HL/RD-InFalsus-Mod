using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal sealed class Icp1Reader
    {
        internal const int HeaderSize = 28;
        internal const int NoteSize = 80;
        internal const int EventSize = 32;

        internal sealed class Note
        {
            internal long Id;
            internal long Line;
            internal int Start;
            internal int End;
            internal int Side;
            internal int Type;
            internal int Flags;
            internal readonly int[] Num = new int[4];
            internal readonly int[] Den = new int[4];
        }

        internal sealed class Event
        {
            internal long Id;
            internal int Time;
            internal int Type;
            internal ulong Value1;
            internal ulong Value2;
            internal double Number => BitConverter.Int64BitsToDouble((long)Value1);
        }

        internal readonly List<Note> Notes = new List<Note>();
        internal readonly List<Event> Events = new List<Event>();
        internal float Bpm;
        internal float Meter;

        internal static bool TryRead(byte[] file, string key, out Icp1Reader chart, out string reason)
        {
            chart = null;
            reason = null;

            if (file == null || file.Length < HeaderSize) { reason = "the file is too short"; return false; }
            if (file[0] != 'I' || file[1] != 'C' || file[2] != 'P' || file[3] != '1')
            {
                reason = "it does not start with ICP1";
                return false;
            }

            int noteSize = U16(file, 8);
            int eventSize = U16(file, 10);
            int notes = (int)U32(file, 12);
            int events = (int)U32(file, 16);

            if (noteSize != NoteSize || eventSize != EventSize)
            {
                reason = $"record sizes are {noteSize}/{eventSize}";
                return false;
            }
            if (notes < 0 || events < 0 ||
                HeaderSize + (long)notes * NoteSize + (long)events * EventSize != file.Length)
            {
                reason = "the counts do not match the file length";
                return false;
            }

            var result = new Icp1Reader
            {
                Bpm = F32(file, 20),
                Meter = F32(file, 24),
            };
            Icp1Writer.State state = Icp1Writer.State.Seed(key, notes);
            var scaled = new int[8];
            var raw = new int[8];
            var cipher = new int[4];

            for (int i = 0; i < notes; i++)
            {
                int at = HeaderSize + i * NoteSize;
                long id = (long)(U64(file, at) - state.Field(i, 0));
                long line = (long)(U64(file, at + 0x08) - state.Field(i, 1));
                int start = (int)(U32(file, at + 0x10) - state.Field(i, 2));
                int end = (int)(U32(file, at + 0x14) - state.Field(i, 3));
                uint sideType = (uint)(U32(file, at + 0x18) - state.Field(i, 4));
                uint flags = (uint)(U32(file, at + 0x1C) - state.Field(i, 5));

                for (int p = 0; p < 4; p++)
                    cipher[p] = (int)(U32(file, at + 0x20 + 4 * p) - state.Field(i, 6 + p));
                for (int p = 0; p < 8; p++) raw[p] = (int)U32(file, at + 0x30 + 4 * p);

                var note = new Note
                {
                    Id = id,
                    Line = line,
                    Start = start,
                    End = end,
                    Side = (int)(sideType & 0xFF),
                    Type = (int)(sideType >> 8),
                    Flags = (int)flags,
                };

                for (int p = 0; p < 4; p++)
                {
                    ScalePair(cipher[p], raw[2 * p], raw[2 * p + 1], out note.Num[p], out note.Den[p]);
                    scaled[2 * p] = note.Num[p];
                    scaled[2 * p + 1] = note.Den[p];
                }

                result.Notes.Add(note);
                state.AdvanceNote((ulong)id, (ulong)line, start, end, sideType, flags, scaled, raw, i);
            }

            int baseAt = HeaderSize + notes * NoteSize;
            for (int i = 0; i < events; i++)
            {
                int at = baseAt + i * EventSize;
                result.Events.Add(new Event
                {
                    Id = (long)U64(file, at),
                    Time = (int)U32(file, at + 0x08),
                    Type = (int)U32(file, at + 0x0C),
                    Value1 = U64(file, at + 0x10),
                    Value2 = U64(file, at + 0x18),
                });
            }

            chart = result;
            return true;
        }

        internal static byte[] FlattenDefaultSpeeds(byte[] file, string key)
        {
            if (!TryRead(file, key, out Icp1Reader chart, out _)) return file;
            var claimed = new HashSet<int>();
            foreach (Note note in chart.Notes)
            {
                int group = (int)((uint)((ulong)note.Line >> 32) & 0x1FFFFFFF);
                if (group != 0) claimed.Add(group);
            }

            var flatten = new List<int>();
            for (int i = 0; i < chart.Events.Count; i++)
            {
                Event item = chart.Events[i];
                if (item.Type != 5) continue;
                int group = (int)BitConverter.Int32BitsToSingle((int)(uint)item.Value2);
                if (claimed.Contains(group)) continue;
                flatten.Add(i);
            }

            if (flatten.Count == 0) return file;
            byte[] copy = (byte[])file.Clone();
            int baseAt = HeaderSize + chart.Notes.Count * NoteSize;
            foreach (int index in flatten)
            {
                int at = baseAt + index * EventSize + 0x0C;
                copy[at] = 0;
                copy[at + 1] = 0;
                copy[at + 2] = 0;
                copy[at + 3] = 0;
            }

            return copy;
        }

        private static void ScalePair(int scaleRaw, int numRaw, int denRaw, out int num, out int den)
        {
            long scale = S32(scaleRaw);
            long n = S32(numRaw);
            long d = S32(denRaw);
            long g = Gcd(n, d);
            num = (int)(scale * (n / g));
            den = (int)(scale * (d / g));
        }

        private static long S32(int v) => v;

        private static long Gcd(long a, long b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            if (a == 0 && b == 0) return 1;
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        private static int U16(byte[] file, int at) =>
            file[at] | (file[at + 1] << 8);

        private static uint U32(byte[] file, int at) =>
            (uint)(file[at] | (file[at + 1] << 8) | (file[at + 2] << 16) | (file[at + 3] << 24));

        private static ulong U64(byte[] file, int at) =>
            U32(file, at) | ((ulong)U32(file, at + 4) << 32);

        private static float F32(byte[] file, int at) =>
            BitConverter.Int32BitsToSingle((int)U32(file, at));
    }
}

using System;
using System.Text;

namespace CustomSong
{
    internal static class Icp1Writer
    {
        private const int HeaderSize = 28;
        private const int NoteSize = 80;
        private const int EventSize = 32;
        private const ulong M64 = ulong.MaxValue;
        private const uint M32 = uint.MaxValue;

        internal static byte[] Build(TextChart chart, string key)
        {
            int notes = chart.Notes.Count;
            int events = chart.Events.Count;
            byte[] file = new byte[HeaderSize + notes * NoteSize + events * EventSize];
            file[0] = (byte)'I'; file[1] = (byte)'C'; file[2] = (byte)'P'; file[3] = (byte)'1';
            Put16(file, 4, 1);
            Put16(file, 6, HeaderSize);
            Put16(file, 8, NoteSize);
            Put16(file, 10, EventSize);
            Put32(file, 12, (uint)notes);
            Put32(file, 16, (uint)events);
            PutF32(file, 20, chart.Bpm);
            PutF32(file, 24, chart.Meter);
            State state = State.Seed(key, notes);

            for (int i = 0; i < notes; i++)
            {
                int at = HeaderSize + i * NoteSize;
                TextChart.Note note = chart.Notes[i];
                int[] plains = note.Plains;
                int[] fields = new int[10];
                fields[0] = i;
                fields[1] = note.Line;
                fields[2] = note.Start;
                fields[3] = note.End;
                fields[4] = (note.Type << 8) | note.Track;
                fields[5] = Field5(note);
                fields[6] = Gcd(plains[0], plains[1]);
                fields[7] = Gcd(plains[2], plains[3]);
                fields[8] = Gcd(plains[4], plains[5]);
                fields[9] = Gcd(plains[6], plains[7]);
                Put64(file, at + 0x00, Masked(state, i, 0, (ulong)fields[0], 8));
                Put64(file, at + 0x08, Masked(state, i, 1, (ulong)fields[1], 8));
                Put32(file, at + 0x10, (uint)Masked(state, i, 2, (ulong)fields[2], 4));
                Put32(file, at + 0x14, (uint)Masked(state, i, 3, (ulong)fields[3], 4));
                Put32(file, at + 0x18, (uint)Masked(state, i, 4, (ulong)fields[4], 4));
                Put32(file, at + 0x1C, (uint)Masked(state, i, 5, (ulong)fields[5], 4));
                Put32(file, at + 0x20, (uint)Masked(state, i, 6, (ulong)fields[6], 4));
                Put32(file, at + 0x24, (uint)Masked(state, i, 7, (ulong)fields[7], 4));
                Put32(file, at + 0x28, (uint)Masked(state, i, 8, (ulong)fields[8], 4));
                Put32(file, at + 0x2C, (uint)Masked(state, i, 9, (ulong)fields[9], 4));
                for (int p = 0; p < 8; p++) Put32(file, at + 0x30 + p * 4, (uint)plains[p]);
                state.Advance(fields, plains, i);
            }

            int baseAt = HeaderSize + notes * NoteSize;
            for (int i = 0; i < events; i++)
            {
                int at = baseAt + i * EventSize;
                TextChart.Event e = chart.Events[i];

                ulong value1 = e.Type == 3
                    ? (ulong)(uint)e.Flags
                    : (ulong)BitConverter.DoubleToInt64Bits(e.Number);
                ulong value2 = e.HasSpeed
                    ? (ulong)BitConverter.SingleToInt32Bits(e.Speed)
                    : 0UL;
                Put64(file, at + 0x00, (ulong)i);
                Put32(file, at + 0x08, (uint)e.TimeMs);
                Put32(file, at + 0x0C, (uint)e.Type);
                Put64(file, at + 0x10, value1);
                Put64(file, at + 0x18, value2);
            }

            return file;
        }

        private static int Field5(in TextChart.Note note)
        {
            if (note.Flags != 0) return note.Flags;
            if (note.Type == 4) return note.Flick << 8;
            return note.LeftEase | note.RightEase;
        }

        private static ulong Masked(in State state, int index, int field, ulong value, int size)
        {
            ulong mask = state.Field(index, field);
            if (size == 4) mask &= M32;
            return value + mask;
        }

        private static int Gcd(int a, int b)
        {
            long x = Math.Abs((long)a);
            long y = Math.Abs((long)b);
            while (y != 0)
            {
                long t = x % y;
                x = y;
                y = t;
            }
            return (int)(x == 0 ? 1 : x);
        }

        private static void Put16(byte[] file, int at, int value)
        {
            file[at] = (byte)value;
            file[at + 1] = (byte)(value >> 8);
        }

        private static void Put32(byte[] file, int at, uint value)
        {
            for (int i = 0; i < 4; i++) file[at + i] = (byte)(value >> (8 * i));
        }

        private static void PutF32(byte[] file, int at, float value) =>
            Put32(file, at, (uint)BitConverter.SingleToInt32Bits(value));

        private static void Put64(byte[] file, int at, ulong value)
        {
            for (int i = 0; i < 8; i++) file[at + i] = (byte)(value >> (8 * i));
        }

        private struct State
        {
            private ulong _a, _b, _c, _d;
            private uint _count;

            internal static State Seed(string key, int noteCount)
            {
                var state = new State
                {
                    _a = unchecked(0xC4CEB9FE1A85EC53UL + (ulong)noteCount),
                    _b = ((ulong)(uint)noteCount << 32) ^ 0x9E3779B185EBCA87UL,
                    _c = 0xD1B54A32D192ED03UL + 1UL,
                    _d = 1UL ^ 0x94D049BB133111EBUL,
                    _count = 0,
                };

                for (int i = 0; i < key.Length; i++)
                {
                    ulong character = ((ulong)(uint)i << 32) | key[i];
                    state.Finalize(character, character + (ulong)noteCount, 1UL ^ character);
                }

                state.Finalize((ulong)(uint)noteCount, 1UL, (ulong)key.Length);
                return state;
            }

            internal ulong Field(int index, int field)
            {
                ulong packed = ((ulong)(uint)index << 32) | (uint)field;

                switch (field & 3)
                {
                    case 0:
                        return _d ^ (unchecked(_a + Rol(_c ^ packed, 11)));
                    case 1:
                        return _a ^ (unchecked(_b - Rol(packed + _d, 17)));
                    case 2:
                        return _b ^ (unchecked(_c + Rol(_a ^ packed, 29)));
                    default:
                        return _d ^ Rol(unchecked(packed + _b + _c), 37);
                }
            }

            internal void Advance(int[] fields, int[] plains, int index)
            {
                Finalize((ulong)fields[0], (ulong)fields[1],
                         ((ulong)(uint)fields[3] << 32) | (uint)fields[2]);
                Finalize((ulong)fields[4], (ulong)fields[5], Pack(plains[1], plains[0]));
                Finalize(Pack(plains[3], plains[2]), Pack(plains[5], plains[4]), Pack(plains[7], plains[6]));
                Record(index, fields[0], Gcd(plains[0], plains[1]), Gcd(plains[2], plains[3]),
                       Gcd(plains[4], plains[5]), Gcd(plains[6], plains[7]));
            }

            private static ulong Pack(int high, int low) =>
                (uint)low | ((ulong)(uint)high << 32);

            private void Finalize(ulong first, ulong second, ulong third)
            {
                switch (_count & 3)
                {
                    case 0:
                        _a = unchecked(_a + Rol(_d, 7) + first);
                        ulong rax = Rol(unchecked(_a + second), 13);
                        _b ^= rax;
                        third ^= _b;
                        _c = unchecked(_c - third);
                        _d = Rol(unchecked(_c + _d), 27) ^ _a;
                        break;

                    case 1:
                        _b = unchecked(_b + Rol(_a, 9) + third);
                        ulong rcx = Rol(unchecked(_b + first), 19);
                        _c ^= rcx;
                        second ^= _c;
                        _d = unchecked(_d - second);
                        _a = Rol(unchecked(_d + _a), 31) ^ _b;
                        break;

                    case 2:
                        _c = unchecked(_c + Rol(_b, 15) + second);
                        ulong rdx = Rol(unchecked(_c + third), 23);
                        _d ^= rdx;
                        first ^= _d;
                        _a = unchecked(_a - first);
                        _b = Rol(unchecked(_b + _a), 39) ^ _c;
                        break;

                    default:
                        ulong keep = _c;
                        _d = unchecked(_d + Rol(_c, 21) + first);
                        ulong r8 = Rol(unchecked(_d + second), 29);
                        _a ^= r8;
                        r8 = _a ^ third;
                        _b = unchecked(_b - r8);
                        _c = Rol(unchecked(keep + _b), 43) ^ _d;
                        break;
                }

                _count++;
            }

            private void Record(int index, int field0, int g0, int g1, int g2, int g3)
            {
                ulong repeated = (uint)index | ((ulong)(uint)index << 32);
                ulong folded = unchecked(repeated + ((_a + Rol(_b, 9)) ^ (_d + Rol(_c, 23))));
                ulong fold = (folded ^ (folded >> 32)) & M32;
                ulong fourth = unchecked((ulong)(g3 - 1) ^ (fold >> 24));
                ulong third = Rol(unchecked((ulong)(g2 - 1) ^ ((fold >> 16) & 0xFF)), 29) ^ Rol(fourth, 47);
                ulong combined = unchecked((ulong)(g0 - 1) ^ (fold & 0xFF))
                                 ^ Rol(unchecked((ulong)(g1 - 1) ^ ((fold >> 8) & 0xFF)), 13)
                                 ^ third;

                ulong mix = ((uint)field0 ^ (uint)index ^ (uint)combined ^ (uint)(combined >> 32) ^
                             (uint)((ulong)field0 >> 32)) & M32;
                ulong repeatedMix = mix | (mix << 32);
                _a ^= repeatedMix;
                _b = unchecked(_b + Rol(repeatedMix, 17));
                _c = unchecked(_c - Rol(repeatedMix, 31));
                _d ^= Rol(repeatedMix, 47);
            }

            private static ulong Rol(ulong value, int bits) =>
                (value << bits) | (value >> (64 - bits));
        }
    }
}

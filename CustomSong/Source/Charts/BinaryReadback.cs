using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class BinaryReadback
    {
        private const int TrackMoveMagic = 0x54524B;
        private const int ShowTextMagic = 0x545854;
        private const int AudioOffsetMagic = 0x414F46;
        private const int SceneControlMagic = 0x53434E;
        private const int SetColorMagic = 0x434F4C;
        private const int TrackDisplayMode = 1;
        private const int HideGroupMode = 2;
        private const uint TimingGroupNoInput = 0x80000000;
        private const uint TimingGroupNoClip = 0x40000000;
        private const uint TimingGroupMask = 0x1FFFFFFF;
        private const int NoteStride = 0x80;
        private const int NoteLine = 0x08;
        private const int NoteType = 0x14;

        internal static void Publish(IntPtr tuple, string seed, byte[] file)
        {
            try
            {
                NormalizeLines(tuple, seed);

                if (!Icp1Reader.TryRead(file, seed, out Icp1Reader chart, out string reason))
                {
                    ModLog.Warn($"encrypted chart '{seed}''s statements could not be read: {reason}");
                    return;
                }

                var directives = new List<SceneDirectives.Entry>();
                var groups = new Dictionary<long, int>();
                var skyGroups = new Dictionary<long, long>();
                int noInputNotes = 0;

                foreach (Icp1Reader.Note note in chart.Notes)
                {
                    int magic = note.Type == 3 ? (note.Flags >> 8) & 0xFFFFFF : 0;
                    if (magic != 0)
                    {
                        SceneDirectives.Entry? row = Container(magic, note);
                        if (row.HasValue) directives.Add(row.Value);
                        continue;
                    }

                    if (note.Type == 3) continue;
                    uint packed = (uint)((ulong)note.Line >> 32);
                    if (packed != 0)
                        groups[note.Id] = (int)(packed & TimingGroupMask) |
                                          ((packed & TimingGroupNoInput) != 0 ? unchecked((int)0x80000000) : 0) |
                                          ((packed & TimingGroupNoClip) != 0 ? 0x40000000 : 0);

                    if (note.Type == 5)
                    {
                        skyGroups[note.Id] = note.Line & 0xFFFFFFFF;
                        if ((note.Flags & 0x8000) != 0) noInputNotes++;
                    }
                }

                foreach (Icp1Reader.Event item in chart.Events)
                {
                    if (item.Type != 0 && item.Type != 5) continue;
                    double multiplier = item.Number;
                    if (double.IsNaN(multiplier) || double.IsInfinity(multiplier)) continue;

                    int group = item.Type == 5
                        ? (int)BitConverter.Int32BitsToSingle((int)(uint)item.Value2)
                        : 0;

                    directives.Add(new SceneDirectives.Entry
                    {
                        Kind = SceneDirectives.Kind.GroupSpeed,
                        Time = item.Time,
                        Group = group,
                        X = (float)multiplier,
                    });
                }

                if (directives.Count == 0 && groups.Count == 0) return;

                SceneDirectives.Publish(seed, directives, groups, skyGroups,
                                        new List<SceneDirectives.SkyVisual>(), null);
            }
            catch (Exception e)
            {
                ModLog.Warn("the chart's extensions could not be read back: " + ModLog.Describe(e));
            }
        }

        private static SceneDirectives.Entry? Container(int magic, Icp1Reader.Note note)
        {
            switch (magic)
            {
                case AudioOffsetMagic:
                    return new SceneDirectives.Entry
                    {
                        Kind = SceneDirectives.Kind.AudioOffset,
                        Time = 0,
                        Value = (int)note.Line,
                    };

                case SceneControlMagic:
                {
                    int mode = note.Flags & 0xFF;
                    if (mode == TrackDisplayMode)
                        return new SceneDirectives.Entry
                        {
                            Kind = SceneDirectives.Kind.TrackDisplay,
                            Time = note.Start,
                            Group = (int)(uint)note.Line,

                            Value = note.End,
                            Mode = note.Num[0],
                        };

                    if (mode == HideGroupMode)
                        return new SceneDirectives.Entry
                        {
                            Kind = SceneDirectives.Kind.HideGroup,
                            Time = note.Start,
                            Group = (int)(uint)note.Line,
                            Value = note.End,
                        };
                    return null;
                }

                case SetColorMagic:
                    return new SceneDirectives.Entry
                    {
                        Kind = SceneDirectives.Kind.SetColor,
                        Time = note.Start,
                        Group = (long)((ulong)note.Line & 0xFFFFFFFF),
                        Rgba = (uint)note.End,
                        EdgeGlow = note.Num[0] != 0,
                    };

                case TrackMoveMagic:
                    return new SceneDirectives.Entry
                    {
                        Kind = SceneDirectives.Kind.TrackMove,
                        Time = note.Start,
                        X = note.Num[0] / 1000000f,
                        Y = note.Num[1] / 1000000f,
                        Z = note.Num[2] / 1000000f,
                        Xoz = note.Num[3] / 1000000f,
                        Yoz = (int)(uint)note.Line / 1000f,
                        Xoy = (int)((ulong)note.Line >> 32) / 1000f,
                        Mode = note.Flags & 0xFF,
                        Duration = note.End - note.Start,
                    };

                case ShowTextMagic:
                    return new SceneDirectives.Entry
                    {
                        Kind = SceneDirectives.Kind.ShowText,
                        Time = note.Start,
                        Value = (int)note.Line,
                        Size = note.Den[0] != 0 ? note.Num[0] / note.Den[0] : 0,
                    };

                default:
                    return null;
            }
        }

        private static void NormalizeLines(IntPtr tuple, string seed)
        {
            IntPtr notes = Memory.Ptr(tuple + Offsets.Runtime.ChartTupleNotes);
            if (!Memory.TryList(notes, out IntPtr data, out int count)) return;
            if (count <= 0 || count > 200000) return;
            int stride = ChartCodec.NoteSize;
            if (stride <= 0)
            {
                long bytes = Memory.ArrayBytes(data - Offsets.Runtime.ArrayDataOffset);
                stride = bytes > 0 && bytes % count == 0 ? (int)(bytes / count) : NoteStride;
            }
            if (stride < NoteLine + 8) return;
            var seen = new HashSet<long>();
            bool repeated = false;

            for (int i = 0; i < count && !repeated; i++)
            {
                IntPtr note = data + i * stride;
                int type = Memory.I32(note + NoteType);
                if (type != 1 && type != 2) continue;
                long line = (long)((ulong)Memory.I64(note + NoteLine) & 0xFFFFFFFF);
                if (!seen.Add(line)) repeated = true;
            }

            if (!repeated) return;
            long next = 1000;
            int renumbered = 0;

            for (int i = 0; i < count; i++)
            {
                IntPtr note = data + i * stride;
                int type = Memory.I32(note + NoteType);
                if (type != 1 && type != 2) continue;
                ulong packed = (ulong)Memory.I64(note + NoteLine);
                Memory.WriteI64(note + NoteLine,
                    (long)((packed & 0xFFFFFFFF00000000UL) | ((ulong)next++ & 0xFFFFFFFF)));
                renumbered++;
            }
        }
    }
}

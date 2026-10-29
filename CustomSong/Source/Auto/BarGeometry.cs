using System;

namespace CustomSong.Auto
{
    internal static class BarGeometry
    {
        internal static float CurrentCentreX(IntPtr note)
        {
            double duration = Memory.I32(note + AutoOffsets.Note.EndMs)
                            - Memory.I32(note + AutoOffsets.Note.StartMs);
            if (duration <= 0.5) return Memory.F32(note + AutoOffsets.Note.StartX);
            double delta = Memory.F64(note + AutoOffsets.Note.DeltaMs);
            double elapsed = delta > 0.0 ? 0.0 : -delta;
            double progress = Math.Min(1.0, elapsed / duration);

            return Centre(Memory.F32(note + AutoOffsets.Note.StartX),
                          Memory.F32(note + AutoOffsets.Note.EndX),
                          Memory.F32(note + AutoOffsets.Note.StartWidth),
                          Memory.F32(note + AutoOffsets.Note.EndWidth),
                          Memory.I32(note + AutoOffsets.Note.Flags),
                          progress);
        }

        internal static float CurrentCentreX(ChartNote note, double nowMs)
        {
            double duration = note.EndMs - note.StartMs;
            if (duration <= 0.5) return note.StartX;
            double progress = (nowMs - note.StartMs) / duration;
            if (progress < 0.0) progress = 0.0;
            else if (progress > 1.0) progress = 1.0;
            return Centre(note.StartX, note.EndX, note.StartWidth, note.EndWidth, note.Flags, progress);
        }

        private static float Centre(float startX, float endX, float startW, float endW,
                                    int flags, double progress)
        {
            float startHalf = startW * 0.5f;
            float endHalf = endW * 0.5f;
            float dLeft = (endX - endHalf) - (startX - startHalf);
            float dRight = (endX + endHalf) - (startX + startHalf);
            float x0 = -startHalf + startX + (float)(dLeft * Ease(LeftEase(flags), progress));
            float x1 = startHalf + startX + (float)(dRight * Ease(RightEase(flags), progress));
            return (x0 + x1) * 0.5f;
        }

        private static int LeftEase(int flags) =>
            (flags & 0x08) != 0 ? 1 : ((flags & 0x10) != 0 ? 2 : 0);

        private static int RightEase(int flags) =>
            (flags & 0x40) != 0 ? 1 : ((flags & 0x80) != 0 ? 2 : 0);

        private static double Ease(int kind, double p)
        {
            const double HalfPi = Math.PI / 2.0;
            switch (kind)
            {
                case 1: return Math.Sin(p * HalfPi);
                case 2: return 1.0 - Math.Cos(p * HalfPi);
                default: return p;
            }
        }
    }
}

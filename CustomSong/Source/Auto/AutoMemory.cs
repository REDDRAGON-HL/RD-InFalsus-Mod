using System;

namespace CustomSong.Auto
{
    internal static unsafe class AutoMemory
    {
        internal static bool LooksLikeNote(IntPtr p)
        {
            long v = p.ToInt64();
            if (v < 0x10000 || v > 0x00007FFFFFFFFFFF || (v & 7) != 0) return false;
            int side = Memory.I32(p + AutoOffsets.Note.Side);
            if (side < AutoOffsets.Plane.None || side > AutoOffsets.Plane.Sky) return false;
            int type = Memory.I32(p + AutoOffsets.Note.Type);
            return type >= 0 && type <= 8;
        }

        internal static IntPtr InputLanes(IntPtr engine)
        {
            if (!Memory.LooksLikeObject(engine)) return IntPtr.Zero;
            IntPtr array = Memory.Ptr(engine + AutoOffsets.Engine.Input);
            if (!Memory.LooksLikeObject(array)) return IntPtr.Zero;
            int len = Memory.I32(array + AutoOffsets.Runtime.ListSize);
            if (len < AutoOffsets.Plane.LaneCount || len > 64) return IntPtr.Zero;
            int stride = FieldResolver.ElementSize(array, "InputLane.Stride");
            if (stride > 0) AutoOffsets.InputLane.Stride = stride;
            return array + AutoOffsets.Runtime.ArrayDataOffset;
        }

        internal static IntPtr Lane(IntPtr lanes, int lane) =>
            lanes + lane * AutoOffsets.InputLane.Stride;
    }
}

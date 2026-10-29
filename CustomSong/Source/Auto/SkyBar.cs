using System;

namespace CustomSong.Auto
{
    internal sealed class SkyBar
    {
        internal void Pin(IntPtr engine)
        {
            if (!Memory.LooksLikeObject(engine)) return;
            Memory.WriteF64(engine + AutoOffsets.SafeArea.SafeDuration, AutoOffsets.SafeArea.SafeDurationFull);
            Memory.WriteF64(engine + AutoOffsets.SafeArea.ForcedMiss, 0.0);
            Memory.WriteU8(engine + AutoOffsets.SafeArea.StartOk, 1);
            AutoMark.Ran = true;
        }
    }
}

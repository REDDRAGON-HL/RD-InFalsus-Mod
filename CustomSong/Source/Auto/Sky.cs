using System;
using System.Collections.Generic;

namespace CustomSong.Auto
{
    internal sealed class Sky
    {
        private float _x;
        private bool _held;
        private ChartNote[] _bars = Array.Empty<ChartNote>();
        private int _revision = -1;

        internal void AimAtBar(Chart chart, double nowMs)
        {
            if (chart.Revision != _revision)
            {
                _revision = chart.Revision;
                _bars = Bars(chart.Notes);
            }

            for (int i = 0; i < _bars.Length; i++)
            {
                ChartNote bar = _bars[i];
                if (bar.StartMs > nowMs) return;
                if (bar.EndMs < nowMs) continue;
                float x = BarGeometry.CurrentCentreX(bar, nowMs);
                if (float.IsNaN(x)) return;
                _x = Clamp(x);
                _held = true;
                return;
            }
        }

        private static ChartNote[] Bars(ChartNote[] notes)
        {
            var bars = new List<ChartNote>();
            foreach (ChartNote n in notes)
            {
                if (n.IsSky && n.Type == AutoOffsets.NoteType.SkyBar) bars.Add(n);
            }
            return bars.ToArray();
        }

        internal void Hold(IntPtr engine)
        {
            if (!Memory.LooksLikeObject(engine)) return;

            if (!_held)
            {
                _x = Clamp(Memory.F32(engine + AutoOffsets.Engine.CursorRaw));
                _held = true;
            }

            Write(engine);
        }

        internal void AimAt(IntPtr engine, IntPtr note)
        {
            if (!Memory.LooksLikeObject(engine) || !AutoMemory.LooksLikeNote(note)) return;
            if (Memory.F64(note + AutoOffsets.Note.DeltaMs) > 0.0) return;
            float x = BarGeometry.CurrentCentreX(note);
            if (float.IsNaN(x)) return;
            _x = Clamp(x);
            _held = true;
            Write(engine);
        }

        private static float Clamp(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        private void Write(IntPtr engine)
        {
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorRaw, _x);
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorClamped, _x);
        }
    }
}

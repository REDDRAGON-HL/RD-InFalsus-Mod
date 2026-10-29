using System;
using System.Collections.Generic;

namespace CustomSong.Auto
{
    internal sealed unsafe class Floor
    {
        private const double CatchUpMs = 120.0;
        private const double NotHeld = -1.0;
        private const double PressLeadMs = 8.0;
        private const double TapHoldMs = 50.0;
        private const double HoldSlackMs = 40.0;
        private readonly Chart _chart;
        private readonly double[] _releaseAtMs = new double[AutoOffsets.Plane.LaneCount];
        private bool _inherited;
        private bool _clearAll;
        private readonly HashSet<long> _pressed = new HashSet<long>();
        private int _next;
        private int _revision = -1;
        internal long Presses;

        internal Floor(Chart chart, Floor previous)
        {
            _chart = chart;
            _inherited = previous != null && previous.HoldsAny;
            ClearLanes();
        }

        internal void ClearLanes()
        {
            for (int i = 0; i < _releaseAtMs.Length; i++) _releaseAtMs[i] = NotHeld;
            _clearAll = true;
        }

        internal bool HoldsAny
        {
            get
            {
                if (_inherited) return true;

                for (int i = 0; i < _releaseAtMs.Length; i++)
                    if (_releaseAtMs[i] != NotHeld) return true;
                return false;
            }
        }

        internal void LetGo(IntPtr lanes, double tickSeconds)
        {
            if (!HoldsAny) return;
            ClearLanes();
            Release(lanes, tickSeconds, double.PositiveInfinity);
        }

        internal void Tick(IntPtr lanes, double tickSeconds, double nowMs)
        {
            if (_chart.Revision != _revision) Resync(nowMs);
            Release(lanes, tickSeconds, nowMs);
            if (_chart.Loaded) Press(lanes, tickSeconds, nowMs);
        }

        private void Press(IntPtr lanes, double tickSeconds, double nowMs)
        {
            ChartNote[] notes = _chart.Notes;
            uint pressedThisTick = 0;

            while (_next < notes.Length && notes[_next].StartMs <= nowMs + PressLeadMs)
            {
                ChartNote n = notes[_next];

                if (n.Side < AutoOffsets.Plane.Main || n.Side > AutoOffsets.Plane.Space)
                {
                    _next++;
                    continue;
                }

                if (_pressed.Contains(n.NoteId))
                {
                    _next++;
                    continue;
                }

                if (nowMs - n.StartMs > CatchUpMs)
                {
                    _next++;
                    continue;
                }

                int first = Math.Max(0, n.LaneFirst);
                int last = Math.Min(AutoOffsets.Plane.LaneCount - 1, n.LaneLast);
                if (last < first) { _next++; continue; }

                uint mask = 1u << first;
                if ((pressedThisTick & mask) != 0) return;
                pressedThisTick |= mask;
                _next++;
                _pressed.Add(n.NoteId);
                IntPtr e = AutoMemory.Lane(lanes, first);
                *(byte*)(e + AutoOffsets.InputLane.PressEdge) = 1;
                *(byte*)(e + AutoOffsets.InputLane.Held) = 1;
                *(double*)(e + AutoOffsets.InputLane.LastInput) = tickSeconds;
                AutoMark.Ran = true;

                _releaseAtMs[first] = Math.Max(_releaseAtMs[first],
                    n.IsHold ? n.EndMs + HoldSlackMs : n.StartMs + TapHoldMs);
                Presses++;
            }
        }

        private void Release(IntPtr lanes, double tickSeconds, double nowMs)
        {
            bool clearAll = _clearAll;
            _clearAll = false;
            if (clearAll) _inherited = false;

            for (int lane = 0; lane < AutoOffsets.Plane.LaneCount; lane++)
            {
                if (clearAll || Expired(lane, nowMs)) _releaseAtMs[lane] = NotHeld;
                IntPtr e = AutoMemory.Lane(lanes, lane);
                *(byte*)(e + AutoOffsets.InputLane.PressEdge) = 0;
                *(byte*)(e + AutoOffsets.InputLane.Consumed) = 0;

                if (_releaseAtMs[lane] == NotHeld)
                {
                    *(byte*)(e + AutoOffsets.InputLane.Held) = 0;
                    *(double*)(e + AutoOffsets.InputLane.LastInput) = 0.0;
                }
                else
                {
                    *(byte*)(e + AutoOffsets.InputLane.Held) = 1;
                    *(double*)(e + AutoOffsets.InputLane.LastInput) = tickSeconds;
                }
            }
        }

        private bool Expired(int lane, double nowMs) =>
            _releaseAtMs[lane] != NotHeld && nowMs >= _releaseAtMs[lane];

        private void Resync(double nowMs)
        {
            _revision = _chart.Revision;
            ChartNote[] notes = _chart.Notes;
            int target = 0;
            while (target < notes.Length && notes[target].StartMs + CatchUpMs <= nowMs) target++;
            _next = target;
        }
    }
}

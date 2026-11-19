using System;
using CustomSong.Auto;

namespace CustomSong.Replay
{
    internal sealed class ReplayRecorder
    {
        private const double BackMs = 500.0;
        private const double AheadMs = 250.0;
        private Chart _chart;
        private readonly ReplayData _data = new ReplayData();
        private double[] _frameDelta = Array.Empty<double>();
        private byte _held;
        private byte _edges;
        private float _raw = float.NaN;
        private float _clamped = float.NaN;
        private int _from;
        private int _to;
        private double _windowMs = double.NegativeInfinity;
        private bool _flickLogged;

        internal ReplayRecorder(Chart chart)
        {
            _chart = chart;
            Header();
        }

        private void Header()
        {
            ChartNote[] notes = _chart.Notes;
            _data.NoteCount = notes.Length;
            _data.FirstMs = notes.Length > 0 ? notes[0].StartMs : 0;
            _data.LastMs = notes.Length > 0 ? notes[notes.Length - 1].StartMs : 0;
            long checksum = 0;
            for (int i = 0; i < notes.Length; i++) checksum ^= notes[i].NoteId;
            _data.Checksum = checksum;
            _frameDelta = new double[notes.Length];
            Fill();
        }

        private void Fill()
        {
            for (int i = 0; i < _frameDelta.Length; i++) _frameDelta[i] = double.NaN;
        }

        internal Chart Chart => _chart;

        internal bool CanAdopt(Chart chart, double nowMs)
        {
            if (chart == null || !_data.Matches(chart)) return false;
            double last = _data.Ticks.Count > 0 ? _data.Ticks[_data.Ticks.Count - 1].Ms : double.NegativeInfinity;
            return nowMs >= last - 3000.0;
        }

        internal void Adopt(Chart chart)
        {
            _chart = chart;
            Header();
            _from = 0;
            _to = 0;
            _windowMs = double.NegativeInfinity;
        }

        internal void Frame(IntPtr engine, double chartMs)
        {
            if (!Memory.LooksLikeObject(engine)) return;
            if (_chart.Notes.Length != _frameDelta.Length) Header();

            if (_data.Ticks.Count > 0 && chartMs < _data.Ticks[_data.Ticks.Count - 1].Ms - 100.0)
            {
                while (_data.Ticks.Count > 0 && _data.Ticks[_data.Ticks.Count - 1].Ms > chartMs)
                    _data.Ticks.RemoveAt(_data.Ticks.Count - 1);
                _edges = 0xFF;
                _held = 0xFF;
                _raw = float.NaN;
                _clamped = float.NaN;
            }

            IntPtr lanes = AutoMemory.InputLanes(engine);
            byte edges = 0;
            byte held = 0;

            if (lanes != IntPtr.Zero)
            {
                for (int lane = 0; lane < AutoOffsets.Plane.LaneCount; lane++)
                {
                    IntPtr entry = AutoMemory.Lane(lanes, lane);
                    if (Memory.U8(entry + AutoOffsets.InputLane.PressEdge) != 0) edges |= (byte)(1 << lane);
                    if (Memory.U8(entry + AutoOffsets.InputLane.Held) != 0) held |= (byte)(1 << lane);
                }
            }

            float raw = Memory.F32(engine + AutoOffsets.Engine.CursorRaw);
            float clamped = Memory.F32(engine + AutoOffsets.Engine.CursorClamped);
            if (edges == _edges && held == _held && raw.Equals(_raw) && clamped.Equals(_clamped)) return;
            _edges = edges;
            _held = held;
            _raw = raw;
            _clamped = clamped;

            _data.Ticks.Add(new ReplayData.Tick
            {
                Ms = chartMs,
                Held = held,
                Edges = edges,
                Raw = raw,
                Clamped = clamped,
            });
        }

        internal void Hb(double chartMs)
        {
            if (_chart.Notes.Length != _frameDelta.Length) return;
            Window(chartMs);
            ChartNote[] notes = _chart.Notes;
            for (int i = _from; i < _to; i++)
            {
                ChartNote note = notes[i];
                if (note.Address == IntPtr.Zero || !AutoMemory.LooksLikeNote(note.Address)) continue;
                _frameDelta[i] = Memory.F64(note.Address + AutoOffsets.Note.DeltaMs);
            }
        }

        internal void OzPost(double chartMs)
        {
            if (_chart.Notes.Length != _frameDelta.Length) return;
            Window(chartMs);
            ChartNote[] notes = _chart.Notes;
            for (int i = _from; i < _to; i++)
            {
                ChartNote note = notes[i];
                if (!_chart.Records.TryGetValue(note.NoteId, out Chart.RecordRange records)) continue;
                ReplayData.Point[] points;
                if (!_data.Points.TryGetValue(note.NoteId, out points) || points.Length != records.Count)
                {
                    points = new ReplayData.Point[records.Count];
                    _data.Points[note.NoteId] = points;
                }

                for (int j = 0; j < records.Count; j++)
                {
                    if (points[j].Recorded) continue;
                    IntPtr point = records.First + j * AutoOffsets.JudgementRecord.Size;
                    if (Memory.U8(point + AutoOffsets.JudgementRecord.Judged) == 0) continue;
                    int kind = Memory.I32(point + AutoOffsets.JudgementRecord.Kind);
                    double offset = Memory.F64(point + AutoOffsets.JudgementRecord.RelativeMs);
                    double frame = _frameDelta[i];
                    if (double.IsNaN(frame) || double.IsInfinity(frame))
                    {
                        frame = note.Address != IntPtr.Zero && AutoMemory.LooksLikeNote(note.Address)
                            ? Memory.F64(note.Address + AutoOffsets.Note.DeltaMs)
                            : 0.0;
                    }

                    bool flick = note.Side == AutoOffsets.Plane.Sky &&
                                 note.Type == AutoOffsets.NoteType.SkyFlick;
                    double delta = flick ? frame : offset + frame;

                    if (flick && !_flickLogged)
                    {
                        _flickLogged = true;
                    }

                    points[j] = new ReplayData.Point
                    {
                        Grade = Memory.U8(point + AutoOffsets.JudgementRecord.Grade),
                        Kind = (byte)kind,
                        Delta = delta,
                        Recorded = true,
                    };
                    _data.Recorded++;
                }
            }
        }

        internal ReplayData Finish()
        {
            return _data;
        }

        private void Window(double nowMs)
        {
            ChartNote[] notes = _chart.Notes;

            if (nowMs < _windowMs - 600.0)
            {
                _from = 0;
                _to = 0;
            }

            _windowMs = nowMs;
            while (_from < notes.Length && notes[_from].StartMs < nowMs - BackMs) _from++;
            if (_to < _from) _to = _from;
            while (_to < notes.Length && notes[_to].StartMs <= nowMs + AheadMs) _to++;
        }
    }
}

using System;
using CustomSong.Auto;

namespace CustomSong.Replay
{
    internal sealed class ReplayPlayer
    {
        private const double BackMs = 500.0;
        private const double AheadMs = 250.0;
        private readonly Chart _chart;
        private readonly ReplayData _data;
        private int _tick = -1;
        private double _lastMs = double.NegativeInfinity;
        private double _tickSeconds;

        private int _from;
        private int _to;
        private double _windowMs = double.NegativeInfinity;
        private int _notesLength = -1;
        private bool _tabDown;
        internal long Presses;
        internal long Flicks;
        internal int Drift;

        internal int Open()
        {
            int open = 0;

            try
            {
                foreach (var entry in _data.Points)
                {
                    if (!_chart.Records.TryGetValue(entry.Key, out Chart.RecordRange records)) continue;
                    ReplayData.Point[] points = entry.Value;
                    for (int j = 0; j < points.Length && j < records.Count; j++)
                    {
                        if (!points[j].Recorded || points[j].Grade <= 1) continue;
                        IntPtr point = records.First + j * AutoOffsets.JudgementRecord.Size;
                        if (Memory.U8(point + AutoOffsets.JudgementRecord.Judged) == 0) open++;
                    }
                }
            }
            catch (Exception)
            {
            }

            return open;
        }

        private const int DriftLogLimit = 8;
        private int _driftLogged;
        private bool[] _confirmed = Array.Empty<bool>();
        private static IntPtr _trackClass;
        private static IntPtr _trackGetter;
        private static IntPtr _drawCursor;
        private static IntPtr _drawMarker;
        private static IntPtr _pushSample;
        private static IntPtr _clearSamples;
        private static bool _trackAsked;
        private static bool _trackLogged;
        private float _seenRaw;
        private double _seenTime;
        private bool _seen;
        private static bool _gestureAsked;

        internal ReplayPlayer(Chart chart, ReplayData data)
        {
            _chart = chart;
            _data = data;
        }

        internal void Frame(IntPtr engine, double seconds, double chartMs)
        {
            if (!Memory.LooksLikeObject(engine)) return;
            _tickSeconds = seconds;

            if (chartMs < _lastMs - 500.0)
            {
                _tick = -1;
                _from = 0;
                _to = 0;
                _seen = false;
            }
            _lastMs = chartMs;
            while (_tick + 1 < _data.Ticks.Count && _data.Ticks[_tick + 1].Ms <= chartMs) _tick++;
            ReplayData.Tick state;
            if (_tick >= 0) state = _data.Ticks[_tick];
            else if (_data.Ticks.Count > 0) state = _data.Ticks[0];
            else state = default;
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorRaw, state.Raw);
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorClamped, state.Clamped);
            FeedFlick(engine, seconds, state.Raw);
            DrawCursor(state.Clamped, state.Raw, engine);
            IntPtr lanes = AutoMemory.InputLanes(engine);
            if (lanes != IntPtr.Zero)
            {
                for (int lane = 0; lane < AutoOffsets.Plane.LaneCount; lane++)
                {
                    IntPtr entry = AutoMemory.Lane(lanes, lane);
                    Memory.WriteU8(entry + AutoOffsets.InputLane.PressEdge, 0);
                    Memory.WriteU8(entry + AutoOffsets.InputLane.Consumed, 0);

                    if ((state.Held & (1 << lane)) != 0)
                    {
                        Memory.WriteU8(entry + AutoOffsets.InputLane.Held, 1);
                        Memory.WriteF64(entry + AutoOffsets.InputLane.LastInput, seconds);
                    }
                    else
                    {
                        Memory.WriteU8(entry + AutoOffsets.InputLane.Held, 0);
                        Memory.WriteF64(entry + AutoOffsets.InputLane.LastInput, 0.0);
                    }
                }
            }

            bool tab = ReplayScreen.KeyDown(ReplayScreen.TabKey);
            if (tab && !_tabDown) ReplayScreen.RequestReturn();
            _tabDown = tab;
        }

        internal void Hb(IntPtr engine, double chartMs)
        {
            Window(chartMs);
            IntPtr lanes = AutoMemory.InputLanes(engine);
            if (lanes == IntPtr.Zero) return;
            uint armed = 0;
            ChartNote[] notes = _chart.Notes;

            for (int i = _from; i < _to; i++)
            {
                ChartNote note = notes[i];
                if (note.Side < AutoOffsets.Plane.Main || note.Side > AutoOffsets.Plane.Space) continue;
                if (note.Address == IntPtr.Zero || !AutoMemory.LooksLikeNote(note.Address)) continue;
                if (Memory.U8(note.Address + AutoOffsets.Note.Finished) != 0) continue;
                if (!_chart.Records.TryGetValue(note.NoteId, out Chart.RecordRange records)) continue;
                if (!_data.Points.TryGetValue(note.NoteId, out ReplayData.Point[] points)) continue;
                int index = FirstUnjudged(records);
                if (index < 0 || index >= points.Length) continue;
                ReplayData.Point point = points[index];
                if (!point.Recorded) continue;
                IntPtr record = records.First + index * AutoOffsets.JudgementRecord.Size;
                double offset = Memory.F64(record + AutoOffsets.JudgementRecord.RelativeMs);
                double delta = Memory.F64(note.Address + AutoOffsets.Note.DeltaMs);
                if (double.IsNaN(offset) || double.IsInfinity(offset)) continue;
                if (double.IsNaN(delta) || double.IsInfinity(delta)) continue;
                if (offset + delta > point.Delta) continue;
                int lane = Math.Max(0, Math.Min(AutoOffsets.Plane.LaneCount - 1, note.LaneFirst));
                IntPtr entry = AutoMemory.Lane(lanes, lane);

                if (point.Kind == 3)
                {
                    bool heldPoint = point.Grade > 1;
                    Memory.WriteU8(entry + AutoOffsets.InputLane.Held, heldPoint ? (byte)1 : (byte)0);
                    Memory.WriteF64(entry + AutoOffsets.InputLane.LastInput,
                                    heldPoint ? _tickSeconds : 0.0);
                    continue;
                }

                if (point.Grade <= 1) continue;
                uint bit = 1u << lane;
                if ((armed & bit) != 0) continue;
                armed |= bit;
                Memory.WriteF64(note.Address + AutoOffsets.Note.DeltaMs, point.Delta - offset);
                Memory.WriteU8(entry + AutoOffsets.InputLane.PressEdge, 1);
                Memory.WriteU8(entry + AutoOffsets.InputLane.Consumed, 0);
                Memory.WriteU8(entry + AutoOffsets.InputLane.Held, 1);
                Memory.WriteF64(entry + AutoOffsets.InputLane.LastInput, _tickSeconds);
                Presses++;
            }
        }

        internal int Flick(IntPtr note)
        {
            if (!AutoMemory.LooksLikeNote(note)) return 0;
            if (Memory.I32(note + AutoOffsets.Note.Side) != AutoOffsets.Plane.Sky) return 0;
            if (Memory.I32(note + AutoOffsets.Note.Type) != AutoOffsets.NoteType.SkyFlick) return 0;
            if (Memory.U8(note + AutoOffsets.Note.Finished) != 0) return 0;
            long id = Memory.I64(note + AutoOffsets.Note.Id);
            if (!_chart.Records.TryGetValue(id, out Chart.RecordRange records)) return 0;
            if (!_data.Points.TryGetValue(id, out ReplayData.Point[] points)) return 0;
            int index = FirstUnjudged(records);
            if (index < 0 || index >= points.Length) return 0;
            ReplayData.Point point = points[index];
            if (!point.Recorded || point.Grade != 6) return 0;
            double delta = Memory.F64(note + AutoOffsets.Note.DeltaMs);
            if (double.IsNaN(delta) || double.IsInfinity(delta)) return 0;
            double due = point.Delta;
            if (due > 0.0) due = 0.0;
            if (delta > due) return -1;
            Memory.WriteF64(note + AutoOffsets.Note.DeltaMs, -1.0);
            Flicks++;
            return 1;
        }

        private unsafe void FeedFlick(IntPtr engine, double seconds, float raw)
        {
            if (!_gestureAsked)
            {
                _gestureAsked = true;
                _pushSample = MethodResolver.ByRuntime("_TD", "_dz", "Game.dll", "_E");
                _clearSamples = MethodResolver.ByRuntime("_TD", "_cz", "Game.dll", "_E");
            }

            if (_pushSample == IntPtr.Zero || _clearSamples == IntPtr.Zero)
            {
                _seen = false;
                return;
            }

            if (!_seen)
            {
                _seen = true;
                _seenRaw = raw;
                _seenTime = seconds;
                return;
            }

            float delta = raw - _seenRaw;
            if (delta != 0f)
            {
                IntPtr right = Memory.Ptr(engine + AutoOffsets.Engine.FlickRight);
                IntPtr left = Memory.Ptr(engine + AutoOffsets.Engine.FlickLeft);

                if (delta > 0f)
                {
                    if (Memory.LooksLikeObject(left))
                        ((delegate* unmanaged[Cdecl]<IntPtr, void>)_clearSamples)(left);
                    if (Memory.LooksLikeObject(right))
                        ((delegate* unmanaged[Cdecl]<IntPtr, double, float, void>)_pushSample)(right, _seenTime, delta);
                }
                else
                {
                    if (Memory.LooksLikeObject(right))
                        ((delegate* unmanaged[Cdecl]<IntPtr, void>)_clearSamples)(right);
                    if (Memory.LooksLikeObject(left))
                        ((delegate* unmanaged[Cdecl]<IntPtr, double, float, void>)_pushSample)(left, _seenTime, -delta);
                }
            }

            _seenRaw = raw;
            _seenTime = seconds;
        }

        private static unsafe void DrawCursor(float clamped, float raw, IntPtr engine)
        {
            if (!_trackAsked)
            {
                _trackAsked = true;
                _trackClass = MethodResolver.ClassByFullName("ifapp.Game.Scenes.Game", "Track");
                _trackGetter = MethodResolver.ByRuntime("Track", "_YbA", "Game.dll", "ifapp.Game.Scenes.Game");
                _drawCursor = MethodResolver.ByRuntime("Track", "_bBA", "Game.dll", "ifapp.Game.Scenes.Game");
                _drawMarker = MethodResolver.ByRuntime("Track", "_BBA", "Game.dll", "ifapp.Game.Scenes.Game");
            }

            if (_drawCursor == IntPtr.Zero && _drawMarker == IntPtr.Zero) return;

            IntPtr track = _trackGetter != IntPtr.Zero
                ? ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)_trackGetter)(IntPtr.Zero)
                : IntPtr.Zero;

            if (!Memory.LooksLikeObject(track) && _trackClass != IntPtr.Zero)
            {
                IntPtr statics = FieldResolver.Statics(_trackClass);
                if (statics != IntPtr.Zero) track = Memory.Ptr(statics + 0x58);
            }

            if (!Memory.LooksLikeObject(track)) return;
            IntPtr trackEngine = Memory.Ptr(track + AutoOffsets.Track.Engine);
            if (trackEngine != engine)
            {
                if (!_trackLogged)
                {
                    _trackLogged = true;
                }
                return;
            }

            if (!_trackLogged)
            {
                _trackLogged = true;
            }

            if (_drawCursor != IntPtr.Zero)
                ((delegate* unmanaged[Cdecl]<IntPtr, float, IntPtr, void>)_drawCursor)(track, clamped, IntPtr.Zero);

            if (_drawMarker != IntPtr.Zero)
                ((delegate* unmanaged[Cdecl]<IntPtr, float, IntPtr, void>)_drawMarker)(track, raw, IntPtr.Zero);
        }

        internal void Correct(double chartMs)
        {
            ChartNote[] notes = _chart.Notes;
            if (_confirmed.Length != notes.Length) _confirmed = new bool[notes.Length];

            for (int i = 0; i < notes.Length; i++)
            {
                ChartNote note = notes[i];
                if (note.StartMs > chartMs + 300.0) break;
                if (_confirmed[i]) continue;

                if (!_chart.Records.TryGetValue(note.NoteId, out Chart.RecordRange records) ||
                    !_data.Points.TryGetValue(note.NoteId, out ReplayData.Point[] points) ||
                    points.Length != records.Count)
                {
                    _confirmed[i] = true;
                    continue;
                }

                bool open = false;
                for (int j = 0; j < records.Count; j++)
                {
                    if (!points[j].Recorded) continue;
                    IntPtr point = records.First + j * AutoOffsets.JudgementRecord.Size;
                    if (Memory.U8(point + AutoOffsets.JudgementRecord.Judged) == 0)
                    {
                        open = true;
                        continue;
                    }

                    points[j].Checked = true;
                    byte grade = Memory.U8(point + AutoOffsets.JudgementRecord.Grade);
                    if (grade == points[j].Grade) continue;
                    Drift++;
                    if (++_driftLogged <= DriftLogLimit)
                        {
                        }

                    Memory.WriteU8(point + AutoOffsets.JudgementRecord.Grade, points[j].Grade);
                }

                if (!open) _confirmed[i] = true;
            }
        }

        private static int FirstUnjudged(Chart.RecordRange records)
        {
            for (int i = 0; i < records.Count; i++)
            {
                IntPtr point = records.First + i * AutoOffsets.JudgementRecord.Size;
                if (Memory.U8(point + AutoOffsets.JudgementRecord.Judged) == 0) return i;
            }
            return -1;
        }

        private void Window(double nowMs)
        {
            ChartNote[] notes = _chart.Notes;

            if (notes.Length != _notesLength)
            {
                _notesLength = notes.Length;
                _from = 0;
                _to = 0;
            }

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

using System;
using System.Collections.Generic;

namespace CustomSong.Auto
{
    internal static unsafe class Exact
    {
        private const int MaxReports = 20;
        private const double GroundLateMs = 1.0;
        private static int _reportRevision = -1;
        private static double _reportNextMs = double.NegativeInfinity;
        private static readonly HashSet<long> _reported = new HashSet<long>();

        internal static void FloorTick(IntPtr lnp, IntPtr engine, double chartMs)
        {
            if (!AutoHooks.Writing || engine == IntPtr.Zero) return;
            Chart chart = AutoSong.Current.Chart;
            if (!chart.TryLoad(lnp, chartMs)) return;
            if (!chart.Loaded || chart.Records.Count == 0) return;
            IntPtr lanes = AutoMemory.InputLanes(engine);
            uint armed = 0;
            Report(chart, lanes, chartMs);
            ChartNote[] notes = chart.Notes;
            for (int i = 0; i < notes.Length; i++)
            {
                ChartNote note = notes[i];
                if (note.StartMs > chartMs + 150.0) break;
                if (note.Side < AutoOffsets.Plane.Main || note.Side > AutoOffsets.Plane.Space) continue;
                if (note.Address == IntPtr.Zero) continue;
                if (!AutoMemory.LooksLikeNote(note.Address)) continue;
                if (Memory.U8(note.Address + AutoOffsets.Note.Finished) != 0) continue;
                if (!chart.Records.TryGetValue(note.NoteId, out Chart.RecordRange records)) continue;
                int index = FirstUnjudged(records);
                if (index < 0) continue;
                IntPtr record = records.First + index * AutoOffsets.JudgementRecord.Size;
                int kind = Memory.I32(record + AutoOffsets.JudgementRecord.Kind);
                double offset = Memory.F64(record + AutoOffsets.JudgementRecord.RelativeMs);
                double delta = Memory.F64(note.Address + AutoOffsets.Note.DeltaMs);
                if (double.IsNaN(offset) || double.IsInfinity(offset)) continue;
                if (double.IsNaN(delta) || double.IsInfinity(delta)) continue;
                if (offset < -60000.0 || offset > 86400000.0) continue;
                if (offset + delta > 0.0) continue;
                Memory.WriteF64(note.Address + AutoOffsets.Note.DeltaMs, -offset - GroundLateMs);
                Hooks.Alive("judgement point correction");
                if (lanes == IntPtr.Zero) continue;
                if (note.LaneLast < note.LaneFirst) continue;
                int lane = Math.Max(0, Math.Min(AutoOffsets.Plane.LaneCount - 1, note.LaneFirst));
                IntPtr entry = AutoMemory.Lane(lanes, lane);
                Memory.WriteU8(entry + AutoOffsets.InputLane.Held, 1);
                Memory.WriteF64(entry + AutoOffsets.InputLane.LastInput, AutoSong.Current.TickSeconds);
                if (note.Type == 2 && kind == 3) continue;
                uint bit = 1u << lane;
                if ((armed & bit) != 0) continue;
                armed |= bit;
                Memory.WriteU8(entry + AutoOffsets.InputLane.PressEdge, 1);
                Memory.WriteU8(entry + AutoOffsets.InputLane.Consumed, 0);
                AutoSong.Current.Floor.MarkPressed(note.NoteId);
            }
        }

        internal static void Flick(IntPtr note)
        {
            if (!AutoHooks.Writing) return;
            if (!AutoMemory.LooksLikeNote(note)) return;
            if (Memory.I32(note + AutoOffsets.Note.Side) != AutoOffsets.Plane.Sky) return;
            if (Memory.I32(note + AutoOffsets.Note.Type) != AutoOffsets.NoteType.SkyFlick) return;
            double delta = Memory.F64(note + AutoOffsets.Note.DeltaMs);
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta > 0.0) return;
            Memory.WriteF64(note + AutoOffsets.Note.DeltaMs, -FlickLateMs);
            Hooks.Alive("sky flick correction");
        }

        private const double FlickLateMs = 1.0;

        private static void Report(Chart chart, IntPtr lanes, double chartMs)
        {
            if (chart.Revision != _reportRevision)
            {
                _reportRevision = chart.Revision;
                _reported.Clear();
                _reportNextMs = double.NegativeInfinity;
            }

            if (chartMs < _reportNextMs) return;
            _reportNextMs = chartMs + 500.0;
            if (_reported.Count >= MaxReports) return;
            ChartNote[] notes = chart.Notes;
            for (int i = 0; i < notes.Length; i++)
            {
                ChartNote note = notes[i];
                if (note.StartMs > chartMs + 150.0) break;
                if (note.Address == IntPtr.Zero) continue;
                if (!AutoMemory.LooksLikeNote(note.Address)) continue;
                if (!chart.Records.TryGetValue(note.NoteId, out Chart.RecordRange records)) continue;

                for (int j = 0; j < records.Count; j++)
                {
                    IntPtr point = records.First + j * AutoOffsets.JudgementRecord.Size;
                    int judged = Memory.U8(point + AutoOffsets.JudgementRecord.Judged);
                    int grade = Memory.U8(point + AutoOffsets.JudgementRecord.Grade);
                    if (judged != 0 && grade == 6) continue;
                    int kind = Memory.I32(point + AutoOffsets.JudgementRecord.Kind);
                    double offset = Memory.F64(point + AutoOffsets.JudgementRecord.RelativeMs);
                    double delta = Memory.F64(note.Address + AutoOffsets.Note.DeltaMs);
                    double pointDelta = offset + delta;
                    if (judged == 0 && grade == 0 && pointDelta > -200.0) continue;
                    if (judged == 0 && grade != 0 && pointDelta > -20.0) continue;
                    if (!_reported.Add(note.NoteId)) continue;
                    int held = -1;
                    double last = 0.0;
                    if (lanes != IntPtr.Zero && note.LaneLast >= note.LaneFirst)
                    {
                        int lane = Math.Max(0, Math.Min(AutoOffsets.Plane.LaneCount - 1,
                                                        note.LaneFirst));
                        IntPtr entry = AutoMemory.Lane(lanes, lane);
                        held = Memory.U8(entry + AutoOffsets.InputLane.Held);
                        last = Memory.F64(entry + AutoOffsets.InputLane.LastInput);
                    }

                    ModLog.Warn($"autoplay: non-EX+ grade {grade} judged {judged} " +
                                $"note {note.NoteId} side {note.Side} type {note.Type} kind {kind} " +
                                $"offset {offset:F1} delta {delta:F1} pointDelta {pointDelta:F1} " +
                                $"lane {note.LaneFirst}..{note.LaneLast} held {held} last {last:F3} " +
                                $"finished {Memory.U8(note.Address + AutoOffsets.Note.Finished)} " +
                                $"rec {j}/{records.Count}");
                    if (_reported.Count >= MaxReports) return;
                }
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
    }
}

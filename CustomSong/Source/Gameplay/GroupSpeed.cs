using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class GroupSpeed
    {
        private const int TimingGroupMask = 0x1FFFFFFF;
        private const int TrackRenderTable = 0x4B0;
        private const int DictCapacityLimit = 256;
        private const int NoteCountLimit = 200000;
        private const double SeekWindowMs = 60_000.0;
        private const double VisibleMs = 3_000.0;

        private sealed class Curve
        {
            internal readonly List<double> Times = new List<double>();
            internal readonly List<double> Multipliers = new List<double>();

            internal void Add(double time, double multiplier)
            {
                Times.Add(time);
                Multipliers.Add(multiplier);
                int at = Times.Count - 1;
                while (at > 0 && Times[at - 1] > time)
                {
                    Times[at] = Times[at - 1];
                    Multipliers[at] = Multipliers[at - 1];
                    at--;
                }

                Times[at] = time;
                Multipliers[at] = multiplier;
            }

            internal double MultiplierAt(double time)
            {
                double speed = 1.0;
                for (int i = 0; i < Times.Count && Times[i] <= time; i++) speed = Multipliers[i];
                return speed;
            }

            internal double Evaluate(double time)
            {
                if (time <= 0.0) return 0.0;
                double wind = 0.0;
                double previous = 0.0;
                double speed = 1.0;

                for (int i = 0; i < Times.Count && Times[i] <= time; i++)
                {
                    wind += speed * (Times[i] - previous);
                    previous = Times[i];
                    speed = Multipliers[i];
                }

                return wind + speed * (time - previous);
            }

            internal double Inverse(double target, double near)
            {
                double lo = Math.Max(0.0, near - SeekWindowMs);
                double hi = near + SeekWindowMs;
                double best = double.NaN;
                double bestDistance = double.PositiveInfinity;
                double at = lo;
                double wind = Evaluate(lo);
                double speed = MultiplierAt(lo);

                for (int i = 0; i < Times.Count; i++)
                {
                    double next = Times[i];
                    if (next <= lo) continue;
                    if (next > hi) break;
                    Consider(at, next, wind, speed, target, near, ref best, ref bestDistance);
                    wind += speed * (next - at);
                    at = next;
                    speed = Multipliers[i];
                }

                Consider(at, hi, wind, speed, target, near, ref best, ref bestDistance);
                return best;
            }

            private static void Consider(double from, double to, double wind, double speed,
                                         double target, double near,
                                         ref double best, ref double bestDistance)
            {
                if (to <= from) return;
                double found;
                if (Math.Abs(speed) < 1e-9)
                {
                    if (Math.Abs(target - wind) > 1e-6) return;
                    found = Math.Min(Math.Max(near, from), to);
                }
                else
                {
                    found = from + (target - wind) / speed;
                    if (found < from - 1e-6 || found > to + 1e-6) return;
                    found = Math.Min(Math.Max(found, from), to);
                }

                double distance = Math.Abs(found - near);
                if (distance >= bestDistance) return;
                bestDistance = distance;
                best = found;
            }
        }

        private struct Warped
        {
            internal IntPtr Note;
            internal int Start;
            internal int End;
        }

        private static readonly Curve Identity = new Curve();
        private static readonly Dictionary<int, Curve> _curves = new Dictionary<int, Curve>();
        private static readonly List<Warped> _frame = new List<Warped>();
        private static readonly Dictionary<Curve, double> _now = new Dictionary<Curve, double>();
        private static SceneDirectives.Entry[] _source = Array.Empty<SceneDirectives.Entry>();
        private static Curve _global;
        private static Curve _fallback;
        private static bool _fallbackResolved;
        private static IntPtr _track;
        private static int _generation = -1;
        private static bool _applied;
        private static bool _probed;
        private static int _calls;
        private static int _applies;
        private static int _reports;
        private static int _failures;

        internal static IntPtr Track
        {
            get => _track;
            set => _track = value;
        }

        internal static bool Any
        {
            get
            {
                Sync();
                return _curves.Count > 0;
            }
        }

        internal static void Observe(IntPtr laneLists)
        {
            Sync();
            _calls++;
            if (_calls != 1 && _calls != 2 && _calls != 120 && _calls != 600) return;

            IntPtr render = Memory.LooksLikeObject(_track)
                ? Memory.Ptr(_track + TrackRenderTable)
                : IntPtr.Zero;
        }

        internal static bool IsRenderPass(IntPtr laneLists)
        {
            if (!Memory.LooksLikeObject(_track)) return false;
            IntPtr renderTable = Memory.Ptr(_track + TrackRenderTable);
            bool match = renderTable != IntPtr.Zero && renderTable == laneLists;

            if (!_probed)
            {
                _probed = true;
            }

            return match;
        }

        internal static void Apply(IntPtr lnp, double nowMs)
        {
            Sync();
            if (_curves.Count == 0 || _applied) return;
            if (double.IsNaN(nowMs) || double.IsInfinity(nowMs)) return;

            try
            {
                IntPtr chart = Memory.Ptr(lnp + Auto.AutoOffsets.NotePlayer.ChartObject);
                if (!Memory.LooksLikeObject(chart)) return;
                IntPtr dict = Memory.Ptr(chart + Auto.AutoOffsets.ChartObject.PlaneArrays);
                if (!Memory.LooksLikeObject(dict)) return;
                IntPtr entries = Memory.Ptr(dict + Auto.AutoOffsets.Runtime.DictEntries);
                if (!Memory.LooksLikeObject(entries)) return;
                int capacity = Memory.I32(entries + Auto.AutoOffsets.Runtime.ListSize);
                if (capacity <= 0 || capacity > DictCapacityLimit) return;
                if (!_fallbackResolved && !ResolveFallback(entries, capacity)) return;
                Curve globalCurve = _global ?? Identity;
                double baseWind = globalCurve.Evaluate(nowMs);
                int stride = Auto.AutoOffsets.Note.Size;
                if (stride <= 0) stride = 0x80;
                _now.Clear();
                foreach (Curve curve in _curves.Values) _now[curve] = curve.Evaluate(nowMs);
                if (_fallback != null) _now[_fallback] = _fallback.Evaluate(nowMs);
                int warped = 0;
                IntPtr data = entries + Auto.AutoOffsets.Runtime.ArrayDataOffset;

                for (int i = 0; i < capacity; i++)
                {
                    int slot = i * Auto.AutoOffsets.Runtime.DictEntrySize;
                    if (Memory.I32(data + slot) < 0) continue;
                    IntPtr holder = Memory.Ptr(data + slot + Auto.AutoOffsets.Runtime.DictEntryValue);
                    if (!Memory.LooksLikeObject(holder)) continue;
                    IntPtr array = Memory.Ptr(holder + Auto.AutoOffsets.PlaneHolder.NoteArray);
                    if (!Memory.LooksLikeObject(array)) continue;
                    int slots = Memory.I32(array + Auto.AutoOffsets.Runtime.ListSize);
                    if (slots <= 0 || slots > NoteCountLimit) continue;
                    int size = Memory.I32(holder + Auto.AutoOffsets.PlaneHolder.Count);
                    if (size <= 0 || size > slots) continue;
                    IntPtr notes = array + Auto.AutoOffsets.Runtime.ArrayDataOffset;
                    for (int n = 0; n < size; n++)
                    {
                        IntPtr note = notes + n * stride;
                        long id = Memory.I64(note + Auto.AutoOffsets.Note.Id);
                        int group = SceneDirectives.TimingGroupOf(id) & TimingGroupMask;
                        Curve curve = null;
                        if (group != 0) _curves.TryGetValue(group, out curve);
                        if (curve == null) curve = _fallback;
                        if (curve == null) continue;
                        int start = Memory.I32(note + Auto.AutoOffsets.Note.StartMs);
                        int type = Memory.I32(note + Auto.AutoOffsets.Note.Type);
                        double delta = curve.Evaluate(start) - _now[curve];
                        bool sky = type == 2 || type == 4 || type == 5;
                        double reach = sky ? Math.Abs(delta) : Math.Abs((double)start - nowMs);
                        if (reach > VisibleMs) continue;
                        int end = Memory.I32(note + Auto.AutoOffsets.Note.EndMs);
                        double warpEnd = globalCurve.Inverse(
                            baseWind + curve.Evaluate(end) - _now[curve], nowMs);
                        double warpStart = globalCurve.Inverse(baseWind + delta, nowMs);
                        if (double.IsNaN(warpStart) || double.IsInfinity(warpStart)) continue;
                        if (double.IsNaN(warpEnd) || double.IsInfinity(warpEnd)) warpEnd = warpStart;
                        int newStart = (int)Math.Round(warpStart);
                        int newEnd = (int)Math.Round(warpEnd);
                        if (newEnd < newStart) newEnd = newStart;
                        if (newStart == start && newEnd == end) continue;
                        Memory.WriteI32(note + Auto.AutoOffsets.Note.StartMs, newStart);
                        Memory.WriteI32(note + Auto.AutoOffsets.Note.EndMs, newEnd);
                        _frame.Add(new Warped { Note = note, Start = start, End = end });
                        warped++;
                    }
                }

                _applied = true;
                if (_reports++ < 4)
                    {
                    }
                else if (++_applies % 240 == 0)
                    {
                    }
            }
            catch (Exception e)
            {
                NoteFailure("apply", e);
                Restore();
            }
        }

        internal static void Restore()
        {
            _applied = false;
            if (_frame.Count == 0) return;

            try
            {
                for (int i = 0; i < _frame.Count; i++)
                {
                    Warped item = _frame[i];
                    Memory.WriteI32(item.Note + Auto.AutoOffsets.Note.StartMs, item.Start);
                    Memory.WriteI32(item.Note + Auto.AutoOffsets.Note.EndMs, item.End);
                }
            }
            catch
            {
            }

            _frame.Clear();
        }

        private static bool ResolveFallback(IntPtr entries, int capacity)
        {
            bool claimed = false;
            bool seen = false;
            IntPtr data = entries + Auto.AutoOffsets.Runtime.ArrayDataOffset;
            int stride = Auto.AutoOffsets.Note.Size > 0 ? Auto.AutoOffsets.Note.Size : 0x80;

            for (int i = 0; i < capacity && !claimed; i++)
            {
                int slot = i * Auto.AutoOffsets.Runtime.DictEntrySize;
                if (Memory.I32(data + slot) < 0) continue;
                IntPtr holder = Memory.Ptr(data + slot + Auto.AutoOffsets.Runtime.DictEntryValue);
                if (!Memory.LooksLikeObject(holder)) continue;
                IntPtr array = Memory.Ptr(holder + Auto.AutoOffsets.PlaneHolder.NoteArray);
                if (!Memory.LooksLikeObject(array)) continue;
                int slots = Memory.I32(array + Auto.AutoOffsets.Runtime.ListSize);
                if (slots <= 0 || slots > NoteCountLimit) continue;
                int size = Memory.I32(holder + Auto.AutoOffsets.PlaneHolder.Count);
                if (size <= 0 || size > slots) continue;
                seen = true;
                IntPtr notes = array + Auto.AutoOffsets.Runtime.ArrayDataOffset;
                for (int n = 0; n < size; n++)
                {
                    long id = Memory.I64(notes + n * stride + Auto.AutoOffsets.Note.Id);
                    int group = SceneDirectives.TimingGroupOf(id) & TimingGroupMask;
                    if (group == 0 || !_curves.ContainsKey(group)) continue;
                    claimed = true;
                    break;
                }
            }

            if (!seen) return false;
            _fallbackResolved = true;
            Curve only = !claimed && _curves.Count == 1 ? FirstCurve() : null;
            _fallback = only == null ? null : Merge(_global, only);
            if (_fallback != null)
                {
                }
            return true;
        }

        private static Curve Merge(Curve global, Curve group)
        {
            var merged = new Curve();
            if (global != null)
                for (int i = 0; i < global.Times.Count; i++)
                    merged.Add(global.Times[i], global.Multipliers[i]);
            for (int i = 0; i < group.Times.Count; i++)
                merged.Add(group.Times[i], group.Multipliers[i]);
            return merged;
        }

        private static Curve FirstCurve()
        {
            foreach (Curve curve in _curves.Values) return curve;
            return null;
        }

        private static void Sync()
        {
            if (ReferenceEquals(_source, SceneDirectives.Current) &&
                _generation == SceneDirectives.Generation)
                return;
            _source = SceneDirectives.Current;
            _generation = SceneDirectives.Generation;
            _probed = false;
            _calls = 0;
            _applies = 0;
            Restore();
            _curves.Clear();
            _global = null;
            _fallback = null;
            _fallbackResolved = false;

            foreach (SceneDirectives.Entry row in _source)
            {
                if (row.Kind != SceneDirectives.Kind.GroupSpeed) continue;

                if (row.Group == 0)
                {
                    if (_global == null) _global = new Curve();
                    _global.Add(row.Time, row.X);
                    continue;
                }

                int group = (int)row.Group;
                if (!_curves.TryGetValue(group, out Curve curve))
                    _curves[group] = curve = new Curve();
                curve.Add(row.Time, row.X);

                if (_reports++ < 8)
                    {
                    }
            }
        }

        internal static void Forget()
        {
            Restore();
            _curves.Clear();
            _now.Clear();
            _global = null;
            _fallback = null;
            _fallbackResolved = false;
            _source = Array.Empty<SceneDirectives.Entry>();
            _generation = -1;
            _reports = 0;
        }

        private static void NoteFailure(string where, Exception e)
        {
            if (_failures++ < 3)
                {
                ModLog.Warn($"timing warp {where} failed: {ModLog.Describe(e)}");
                }
        }
    }
}

extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;

namespace CustomSong
{
    using UnityEngineCore::UnityEngine;

    internal static class TrackDisplay
    {
        private const int TrackMasterScene = 0xD8;
        private const int TrackNotesContainer = 0x1F0;
        private const int TrackRuntimeRenderers = 0x248;
        private const int TrackSkyCursor = 0x170;
        private const int TrackSkyGhostCursor = 0x1A0;
        private const int TrackCursorExtras = 0x1A8;
        private static readonly string[] TintProperties = { "_BaseColor", "_Color", "_EmissionColor" };

        private sealed class Tinted
        {
            internal IntPtr Renderer;
            internal readonly Color[] Original = new Color[TintProperties.Length];
            internal readonly bool[] Has = new bool[TintProperties.Length];
        }

        private sealed class Keyframe
        {
            internal double TimeMs;
            internal float ValueAtTime;
            internal float Target;
            internal int DurationMs;
        }

        private static readonly List<Tinted> _renderers = new List<Tinted>();
        private static readonly List<Keyframe> _timeline = new List<Keyframe>();
        private static SceneDirectives.Entry[] _source = Array.Empty<SceneDirectives.Entry>();
        private static IntPtr _track;
        private static int _retiredGeneration = -1;
        private static MaterialPropertyBlock _block;
        private static float _applied = -1f;
        private static int _failures;

        internal static void Tick(IntPtr track, double now)
        {
            if (_retiredGeneration >= 0 && _retiredGeneration == SceneDirectives.Generation) return;
            if (!Memory.LooksLikeObject(track)) return;

            if (!ReferenceEquals(_source, SceneDirectives.Current))
            {
                BuildTimeline();
                _source = SceneDirectives.Current;
                ClearTint();
                _renderers.Clear();
                _track = IntPtr.Zero;
            }

            if (_timeline.Count == 0)
            {
                if (_applied >= 0f) ClearTint();
                return;
            }

            if (_track != track)
            {
                ClearTint();
                _track = track;
                Gather(track);
            }

            float alpha = Evaluate(now);
            if (Math.Abs(alpha - _applied) < 0.002f) return;
            Apply(alpha);
        }

        private static void BuildTimeline()
        {
            _timeline.Clear();
            _applied = -1f;
            float value = 1f;
            float target = 1f;
            int cursor = 0;
            int duration = 0;

            foreach (SceneDirectives.Entry row in SceneDirectives.Current)
            {
                if (row.Kind != SceneDirectives.Kind.TrackDisplay) continue;
                value = Advance(value, cursor, row.Time, duration, target);
                target = Math.Clamp(row.Mode / 255f, 0f, 1f);

                _timeline.Add(new Keyframe
                {
                    TimeMs = row.Time,
                    ValueAtTime = value,
                    Target = target,
                    DurationMs = Math.Max(0, row.Value),
                });
                cursor = row.Time;
                duration = Math.Max(0, row.Value);
            }
        }

        private static float Evaluate(double nowMs)
        {
            int low = 0;
            int high = _timeline.Count;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (_timeline[mid].TimeMs <= nowMs) low = mid + 1;
                else high = mid;
            }

            int index = low - 1;
            if (index < 0) return 1f;
            Keyframe key = _timeline[index];
            return Advance(key.ValueAtTime, key.TimeMs, nowMs, key.DurationMs, key.Target);
        }

        private static float Advance(float start, double fromMs, double toMs, int durationMs, float target)
        {
            if (durationMs <= 0 || toMs <= fromMs) return target;
            float k = (float)Math.Max(0.0, Math.Min(1.0, (toMs - fromMs) / durationMs));
            return start + (target - start) * k;
        }

        private static void Gather(IntPtr track)
        {
            try
            {
                IntPtr master = Memory.Ptr(track + TrackMasterScene);
                IntPtr notes = Memory.Ptr(track + TrackNotesContainer);

                if (Memory.LooksLikeObject(master))
                {
                    var root = new Transform(master);
                    var skip = new List<IntPtr>(4);
                    AddSkip(skip, track + TrackSkyCursor);
                    AddSkip(skip, track + TrackSkyGhostCursor);
                    AddSkip(skip, track + TrackCursorExtras);
                    MeshRenderer[] found = root.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; found != null && i < found.Length; i++)
                    {
                        MeshRenderer renderer = found[i];
                        if (renderer == null || renderer.Pointer == IntPtr.Zero) continue;
                        if (Under(renderer.transform, skip) || Under(renderer.transform, notes)) continue;
                        Add(renderer.Pointer);
                    }
                }

                AddArray(Memory.Ptr(track + TrackRuntimeRenderers));

                if (_renderers.Count == 0)
                {
                    AddArray(Memory.Ptr(track + 0x808));
                }
            }
            catch (Exception e)
            {
                NoteFailure("gather", e);
            }
        }

        private static void AddSkip(List<IntPtr> skip, IntPtr address)
        {
            IntPtr value = Memory.Ptr(address);
            if (Memory.LooksLikeObject(value)) skip.Add(value);
        }

        private static bool Under(Transform transform, List<IntPtr> roots)
        {
            if (transform == null) return false;

            foreach (IntPtr root in roots)
            {
                try
                {
                    if (transform.IsChildOf(new Transform(root))) return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private static bool Under(Transform transform, IntPtr root)
        {
            if (transform == null || !Memory.LooksLikeObject(root)) return false;

            try
            {
                return transform.IsChildOf(new Transform(root));
            }
            catch
            {
                return false;
            }
        }

        private static void AddArray(IntPtr array)
        {
            if (!Memory.LooksLikeObject(array)) return;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (length <= 0 || length > 256) return;
            IntPtr data = array + Offsets.Runtime.ArrayDataOffset;
            for (int i = 0; i < length; i++)
            {
                IntPtr renderer = Memory.Ptr(data + i * IntPtr.Size);
                if (Memory.LooksLikeObject(renderer)) Add(renderer);
            }
        }

        private static void Add(IntPtr renderer)
        {
            foreach (Tinted seen in _renderers)
                if (seen.Renderer == renderer) return;
            var entry = new Tinted { Renderer = renderer };

            try
            {
                Material material = new MeshRenderer(renderer).sharedMaterial;
                if (material == null) return;

                for (int i = 0; i < TintProperties.Length; i++)
                {
                    if (!material.HasProperty(TintProperties[i])) continue;
                    entry.Original[i] = material.GetColor(TintProperties[i]);
                    entry.Has[i] = true;
                }
            }
            catch (Exception e)
            {
                NoteFailure("material", e);
                return;
            }

            _renderers.Add(entry);
        }

        private static void Apply(float alpha)
        {
            try
            {
                if (_block == null) _block = new MaterialPropertyBlock();
                _applied = alpha;

                foreach (Tinted entry in _renderers)
                {
                    if (!Memory.LooksLikeObject(entry.Renderer)) continue;
                    var renderer = new MeshRenderer(entry.Renderer);
                    if (renderer == null) continue;
                    _block.Clear();

                    for (int i = 0; i < TintProperties.Length; i++)
                    {
                        if (!entry.Has[i]) continue;
                        Color original = entry.Original[i];
                        _block.SetColor(TintProperties[i],
                            new Color(original.r, original.g, original.b, original.a * alpha));
                    }

                    renderer.SetPropertyBlock(_block);
                }
            }
            catch (Exception e)
            {
                NoteFailure("apply", e);
            }
        }

        internal static void Retire()
        {
            ClearTint();
            Forget();
            _retiredGeneration = SceneDirectives.Generation;
        }

        internal static void Forget()
        {
            _renderers.Clear();
            _timeline.Clear();
            _source = Array.Empty<SceneDirectives.Entry>();
            _track = IntPtr.Zero;
            _applied = -1f;
            _retiredGeneration = -1;
        }

        private static void ClearTint()
        {
            if (_block == null || _renderers.Count == 0) return;

            try
            {
                _block.Clear();
                foreach (Tinted entry in _renderers)
                {
                    if (!Memory.LooksLikeObject(entry.Renderer)) continue;
                    var renderer = new MeshRenderer(entry.Renderer);
                    if (renderer == null) continue;
                    renderer.SetPropertyBlock(_block);
                }
            }
            catch
            {
            }

            _applied = -1f;
        }

        private static void NoteFailure(string where, Exception e)
        {
            if (_failures++ < 3)
                {
                ModLog.Warn($"scene rows: trackdisplay {where} failed: {ModLog.Describe(e)}");
                }
        }
    }
}

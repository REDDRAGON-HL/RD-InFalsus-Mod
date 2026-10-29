extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using UnityEngineCore::UnityEngine;

namespace CustomSong.Auto
{
    internal static class JudgementPoint
    {
        private static bool ShouldHide => AutoHooks.Writing;

        private static readonly int[] Parts =
        {
            AutoOffsets.Track.SkyCursor,
            AutoOffsets.Track.SkyCursorRenderer,
            AutoOffsets.Track.SkyGhostCursor,
            AutoOffsets.Track.CursorExtras,
        };
        private static int _lastShape = -1;
        private static int _failures;
        private static bool _partsAreHidden;

        internal static void SuppressAt(IntPtr track)
        {
            if (!ShouldHide) return;
            if (!Memory.LooksLikeObject(track)) return;
            int hidden = 0;
            var seen = new List<IntPtr>(Parts.Length);

            foreach (int offset in Parts)
            {
                IntPtr component = Memory.Ptr(track + offset);
                if (!Memory.LooksLikeObject(component)) continue;
                GameObject go;
                try { go = new Component(component).gameObject; }
                catch (Exception e) { NoteFailure(e); continue; }

                if (go == null) continue;
                IntPtr handle = go.Pointer;
                if (seen.Contains(handle)) continue;
                seen.Add(handle);
                if (go.activeSelf) go.SetActive(false);
                hidden++;
            }

            if (hidden > 0) _partsAreHidden = true;

            if (hidden != _lastShape)
            {
                _lastShape = hidden;
            }
        }

        private static void Restore(IntPtr track)
        {
            if (!_partsAreHidden) return;
            _partsAreHidden = false;
            int restored = 0;
            foreach (int offset in Parts)
            {
                if (offset == AutoOffsets.Track.SkyGhostCursor) continue;
                IntPtr component = Memory.Ptr(track + offset);
                if (!Memory.LooksLikeObject(component)) continue;

                try
                {
                    GameObject go = new Component(component).gameObject;
                    if (go == null || go.activeSelf) continue;
                    go.SetActive(true);
                    restored++;
                }
                catch (Exception e)
                {
                    NoteFailure(e);
                }
            }

        }

        internal static void KeepOff(IntPtr track)
        {
            if (!Memory.LooksLikeObject(track)) return;

            if (!ShouldHide)
            {
                Restore(track);
                return;
            }

            IntPtr engine = Memory.Ptr(track + AutoOffsets.Track.Engine);
            if (!Memory.LooksLikeObject(engine)) return;
            float raw = Memory.F32(engine + AutoOffsets.Engine.CursorRaw);
            if (raw >= 0f && raw <= 1f) return;
            float clamped = raw < 0f ? 0f : 1f;
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorRaw, clamped);
            Memory.WriteF32(engine + AutoOffsets.Engine.CursorClamped, clamped);
            IntPtr ghost = Memory.Ptr(track + AutoOffsets.Track.SkyGhostCursor);
            if (!Memory.LooksLikeObject(ghost)) return;

            try
            {
                GameObject go = new Component(ghost).gameObject;
                if (go != null && go.activeSelf) go.SetActive(false);
            }
            catch (Exception e)
            {
                NoteFailure(e);
            }
        }

        internal static void NoteFailure(Exception e)
        {
            _failures++;
            if (_failures <= 3)
                ModLog.Warn($"autoplay: the sky judgement point could not be hidden ({_failures}): " +
                            ModLog.Describe(e));
        }

        internal static void Reset()
        {
            _lastShape = -1;
            _failures = 0;
        }
    }
}

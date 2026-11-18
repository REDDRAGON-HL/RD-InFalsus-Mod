extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomSong
{
    using UnityEngineCore::UnityEngine;

    internal static class SceneControl
    {
        private const int AreaRenderer = 0x30;
        private const int AreaColorDetails = 0x38;
        private const int AreaNoteId = 0x50;
        private const int MainDefault = 0x18;
        private const int ColorStride = 0x10;
        private const int EdgeCenterRed = 0x68;
        private const int EdgeGlowRed = 0x78;
        private const int TrackPoolA = 0x2C8;
        private const int TrackPoolB = 0x2D0;
        private const int TrackEngine = 0x830;
        private const int EngineChartTime = 0xD0;
        private const int TimingGroupMask = 0x1FFFFFFF;

        private struct Applied
        {
            internal IntPtr Original;
            internal uint Rgba;
            internal bool EdgeGlow;
            internal bool Tinted;
            internal bool Hidden;
        }

        private sealed class Layout
        {
            internal int Active = -1;
            internal int Spare = -1;
            internal int Entries = -1;
            internal int Key = -1;
            internal int Value = -1;
            internal int Item4 = -1;
            internal bool Ready;
        }

        private static readonly Dictionary<IntPtr, Layout> _layouts = new Dictionary<IntPtr, Layout>();
        private static readonly Dictionary<IntPtr, Applied> _applied = new Dictionary<IntPtr, Applied>();

        private static readonly Dictionary<(IntPtr Source, uint Rgba, bool EdgeGlow), IntPtr> _clones =
            new Dictionary<(IntPtr, uint, bool), IntPtr>();

        private static readonly Dictionary<long, List<SceneDirectives.Entry>> _colors =
            new Dictionary<long, List<SceneDirectives.Entry>>();

        private static readonly Dictionary<long, List<SceneDirectives.Entry>> _hidden =
            new Dictionary<long, List<SceneDirectives.Entry>>();
        private static SceneDirectives.Entry[] _source = Array.Empty<SceneDirectives.Entry>();
        private static int _failures;
        private static int _swaps;
        private static int _retiredGeneration = -1;
        private static bool _probed;
        private static bool _asked;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;

            bool ok = HarmonyHooks.Patch(
                HarmonyHooks.Target("Track", "Update", "ifapp.Game.Scenes.Game"),
                null,
                HarmonyHooks.Handler(typeof(SceneControl), nameof(UpdatePostfix)));
            ModLog.Load($"scene rows: {(ok ? 1 : 0)}/1 hook installed");

            bool gone = HarmonyHooks.Patch(
                HarmonyHooks.Target("Track", "OnDestroy", "ifapp.Game.Scenes.Game"),
                null,
                HarmonyHooks.Handler(typeof(SceneControl), nameof(DestroyPostfix)));
            ModLog.Load($"scene rows: {(gone ? 1 : 0)}/1 teardown hook installed");
        }

        internal static void Uninstall()
        {
            _asked = false;
            Reset();
        }

        private static void UpdatePostfix(Il2CppObjectBase __instance)
        {
            if (!SceneDirectives.Any) return;
            if (_retiredGeneration == SceneDirectives.Generation) return;

            try
            {
                IntPtr track = HarmonyHooks.Pointer(__instance);
                if (!Memory.LooksLikeObject(track)) return;
                GroupSpeed.Track = track;
                double now = NowMs(track);
                if (double.IsNaN(now)) return;
                Sync();
                Hooks.Alive("scene rows: Track.Update");
                ApplyPool(Memory.Ptr(track + TrackPoolA), now);
                ApplyPool(Memory.Ptr(track + TrackPoolB), now);
                NoInputSky.Tick(track);
                TrackDisplay.Tick(track, now);
                OverlayText.Tick(now);
                TrackMove.Tick(track, now);
                AudioOffset.Tick(now);
                GroupSpeed.Restore();
            }
            catch (Exception e)
            {
                NoteFailure("tick", e);
            }
        }

        private static void DestroyPostfix(Il2CppObjectBase __instance)
        {
            if (!SceneDirectives.Any && !GroupSpeed.Any) return;

            ModLog.Load("scene rows: the gameplay scene is going away; the rows are forgotten");
            Reset();
        }

        private static double NowMs(IntPtr track)
        {
            if (!Memory.LooksLikeObject(track)) return double.NaN;
            IntPtr engine = Memory.Ptr(track + TrackEngine);
            if (!Memory.LooksLikeObject(engine)) return double.NaN;
            return Memory.F64(engine + EngineChartTime) * 1000.0;
        }

        private static void ApplyPool(IntPtr pool, double now)
        {
            if (!Memory.LooksLikeObject(pool)) return;
            Layout layout = LayoutOf(pool);
            if (layout == null) return;
            ApplyDictionary(pool, layout, layout.Active, now);
            ApplyDictionary(pool, layout, layout.Spare, now);
        }

        private static void ApplyDictionary(IntPtr pool, Layout layout, int field, double now)
        {
            if (field < 0) return;
            IntPtr dict = Memory.Ptr(pool + field);
            if (!Memory.LooksLikeObject(dict)) return;
            IntPtr entries = Memory.Ptr(dict + layout.Entries);
            if (!Memory.LooksLikeObject(entries)) return;
            uint capacity = IL2CPP.il2cpp_array_length(entries);
            if (capacity == 0 || capacity > 100000) return;
            uint bytes = IL2CPP.il2cpp_array_get_byte_length(entries);
            if (bytes == 0 || bytes % capacity != 0) return;
            int stride = (int)(bytes / capacity);
            if (stride < 16) return;
            IntPtr data = entries + Offsets.Runtime.ArrayDataOffset;
            bool probe = !_probed;
            if (probe)
            {
                _probed = true;
                ModLog.Load($"scene rows: pool capacity={capacity} stride={stride} " +
                            $"active=0x{layout.Active:X} spare=0x{layout.Spare:X} " +
                            $"entries=0x{layout.Entries:X} key=0x{layout.Key:X} " +
                            $"value=0x{layout.Value:X} item4=0x{layout.Item4:X}");
                ModLog.Load($"scene rows: chart {SceneDirectives.CurrentGroups.Count} timing group(s), " +
                            $"{SceneDirectives.CurrentSkyGroups.Count} sky note(s)");
            }

            int shown = 0;
            int matched = 0;

            for (int i = 0; i < capacity; i++)
            {
                IntPtr entry = data + i * stride;
                if (Memory.I32(entry) < 0) continue;
                long id = Memory.I64(entry + layout.Key);
                IntPtr area = Memory.Ptr(entry + layout.Value + layout.Item4);
                if (!Memory.LooksLikeObject(area)) continue;

                if (probe && shown++ < 8)
                    ModLog.Load($"scene rows: pool id={id} idAt50={Memory.I64(area + AreaNoteId)} " +
                                $"area=0x{area.ToInt64():X}");
                long group = SceneDirectives.SkyGroupOf(id);
                int timingGroup = SceneDirectives.TimingGroupOf(id) & TimingGroupMask;
                if (!_colors.ContainsKey(group) && !_hidden.ContainsKey(timingGroup)) continue;
                matched++;
                Apply(area, group, timingGroup, now);
            }

            if (probe)
                ModLog.Load($"scene rows: pool {matched} bar(s) had a row to apply");
        }

        private static Layout LayoutOf(IntPtr pool)
        {
            IntPtr klass = IL2CPP.il2cpp_object_get_class(pool);
            if (klass == IntPtr.Zero) return null;
            if (_layouts.TryGetValue(klass, out Layout known)) return known.Ready ? known : null;
            var layout = new Layout();
            try
            {
                layout.Active = Field(klass, "_QXA");
                layout.Spare = Field(klass, "_rXA");
                IntPtr dict = layout.Active >= 0 ? Memory.Ptr(pool + layout.Active) : IntPtr.Zero;
                IntPtr dictClass = Memory.LooksLikeObject(dict)
                    ? IL2CPP.il2cpp_object_get_class(dict)
                    : IntPtr.Zero;
                layout.Entries = dictClass != IntPtr.Zero ? Field(dictClass, "_entries") : -1;

                if (layout.Entries >= 0 && Memory.LooksLikeObject(dict))
                {
                    IntPtr entries = Memory.Ptr(dict + layout.Entries);
                    if (Memory.LooksLikeObject(entries))
                    {
                        IntPtr arrayClass = IL2CPP.il2cpp_object_get_class(entries);
                        IntPtr entryClass = IL2CPP.il2cpp_class_get_element_class(arrayClass);
                        layout.Key = Field(entryClass, "key");
                        layout.Value = Field(entryClass, "value");
                        IntPtr valueField = IL2CPP.il2cpp_class_get_field_from_name(entryClass, "value");
                        if (valueField != IntPtr.Zero)
                        {
                            IntPtr valueType = IL2CPP.il2cpp_field_get_type(valueField);
                            IntPtr valueClass = IL2CPP.il2cpp_class_from_type(valueType);
                            layout.Item4 = Field(valueClass, "Item4");
                        }
                    }
                }

                layout.Ready = layout.Active >= 0 && layout.Spare >= 0 && layout.Entries >= 0 &&
                               layout.Key >= 0 && layout.Value >= 0 && layout.Item4 >= 0;
            }
            catch (Exception e)
            {
                NoteFailure("pool layout", e);
            }

            _layouts[klass] = layout;
            if (!layout.Ready)
                ModLog.Warn("scene rows: the sky-area pools could not be read in this build; " +
                            "setcolor and hidegroup will do nothing");
            return layout.Ready ? layout : null;
        }

        private static int Field(IntPtr klass, string name)
        {
            if (klass == IntPtr.Zero) return -1;
            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            if (field == IntPtr.Zero) return -1;
            int offset = (int)IL2CPP.il2cpp_field_get_offset(field);
            if (IL2CPP.il2cpp_class_is_valuetype(klass)) offset -= Offsets.Runtime.ObjectHeader;
            return offset;
        }

        private static void Apply(IntPtr area, long group, int timingGroup, double now)
        {
            if (!Memory.LooksLikeObject(area)) return;
            if (double.IsNaN(now)) return;

            if (!_applied.TryGetValue(area, out Applied applied))
            {
                applied.Original = IntPtr.Zero;
                applied.Tinted = false;
                _applied[area] = applied;
            }

            bool hidden = TryHidden(timingGroup, now, out bool hide) && hide;
            bool hasColor = TryColor(group, now, out uint rgba, out bool edgeGlow);

            SetVisible(area, !hidden, ref applied);

            if (hidden || !hasColor)
            {
                if (applied.Tinted && applied.Original != IntPtr.Zero)
                {
                    Memory.WritePtr(area + AreaColorDetails, applied.Original);
                    applied.Tinted = false;
                }
                _applied[area] = applied;
                return;
            }

            uint tint = rgba;
            bool glow = edgeGlow;

            IntPtr current = Memory.Ptr(area + AreaColorDetails);
            if (current == IntPtr.Zero)
            {
                _applied[area] = applied;
                return;
            }

            if (applied.Tinted && applied.Rgba == tint && applied.EdgeGlow == glow &&
                current != applied.Original)
            {
                _applied[area] = applied;
                return;
            }

            IntPtr source = applied.Original != IntPtr.Zero ? applied.Original : current;
            IntPtr clone = Clone(source, tint, glow);
            if (clone == IntPtr.Zero)
            {
                _applied[area] = applied;
                return;
            }

            applied.Original = source;
            applied.Rgba = tint;
            applied.EdgeGlow = glow;
            applied.Tinted = true;
            _applied[area] = applied;
            Memory.WritePtr(area + AreaColorDetails, clone);

            if (_swaps < 8)
            {
                _swaps++;
                ModLog.Load($"scene rows: group {group} -> #{tint:X8}");
            }
        }

        private static void SetVisible(IntPtr area, bool visible, ref Applied applied)
        {
            applied.Hidden = !visible;

            try
            {
                IntPtr renderer = Memory.Ptr(area + AreaRenderer);
                if (!Memory.LooksLikeObject(renderer)) return;

                var target = new UnityEngineCore::UnityEngine.Renderer(renderer);
                if (target == null) return;
                if (target.enabled == visible) return;

                target.enabled = visible;
            }
            catch (Exception e)
            {
                NoteFailure("visibility", e);
            }
        }

        private static bool TryColor(long group, double now, out uint rgba, out bool edgeGlow)
        {
            rgba = 0;
            edgeGlow = true;
            if (!_colors.TryGetValue(group, out List<SceneDirectives.Entry> rows)) return false;
            bool found = false;
            foreach (SceneDirectives.Entry row in rows)
            {
                if (row.Time > now) break;
                rgba = row.Rgba;
                edgeGlow = row.EdgeGlow;
                found = true;
            }
            return found;
        }

        private static bool TryHidden(int group, double now, out bool hidden)
        {
            hidden = false;
            if (!_hidden.TryGetValue(group, out List<SceneDirectives.Entry> rows)) return false;
            bool found = false;
            foreach (SceneDirectives.Entry row in rows)
            {
                if (row.Time > now) break;
                hidden = row.Value != 0;
                found = true;
            }
            return found;
        }

        internal static bool HiddenNow(int timingGroup, double now) =>
            TryHidden(timingGroup, now, out bool hidden) && hidden;

        internal static IntPtr ColorFor(long group, double now, IntPtr source)
        {
            if (!TryColor(group, now, out uint rgba, out bool edgeGlow)) return IntPtr.Zero;
            if (!Memory.LooksLikeObject(source)) return IntPtr.Zero;
            return Clone(source, rgba, edgeGlow);
        }

        private static IntPtr Clone(IntPtr source, uint rgba, bool edgeGlow)
        {
            var key = (source, rgba, edgeGlow);
            if (_clones.TryGetValue(key, out IntPtr known) && Memory.LooksLikeObject(known))
                return known;

            try
            {
                var original = new UnityEngineCore::UnityEngine.Object(source);
                UnityEngineCore::UnityEngine.Object clone =
                    UnityEngineCore::UnityEngine.Object.Instantiate(original);
                if (clone == null) return IntPtr.Zero;
                IntPtr target = clone.Pointer;
                if (!Memory.LooksLikeObject(target)) return IntPtr.Zero;
                Tint(source, target, rgba, edgeGlow);
                _clones[key] = target;
                return target;
            }
            catch (Exception e)
            {
                NoteFailure("colour clone", e);
                return IntPtr.Zero;
            }
        }

        private static void Tint(IntPtr source, IntPtr target, uint rgba, bool edgeGlow)
        {
            float r = ((rgba >> 24) & 0xFF) / 255f;
            float g = ((rgba >> 16) & 0xFF) / 255f;
            float b = ((rgba >> 8) & 0xFF) / 255f;
            float a = (rgba & 0xFF) / 255f;

            for (int slot = 0; slot < 4; slot++)
            {
                int at = MainDefault + slot * ColorStride;

                if (!edgeGlow && (slot == 1 || slot == 2))
                {
                    for (int c = 0; c < 4; c++) Memory.WriteF32(target + at + c * 4, 0f);
                    int red = slot == 1 ? EdgeCenterRed : EdgeGlowRed;
                    for (int c = 0; c < 4; c++) Memory.WriteF32(target + red + c * 4, 0f);
                    continue;
                }

                float sr = Memory.F32(source + at);
                float sg = Memory.F32(source + at + 4);
                float sb = Memory.F32(source + at + 8);
                float sa = Memory.F32(source + at + 12);
                float lum = 0.2126f * sr + 0.7152f * sg + 0.0722f * sb;
                Memory.WriteF32(target + at, r * lum);
                Memory.WriteF32(target + at + 4, g * lum);
                Memory.WriteF32(target + at + 8, b * lum);
                Memory.WriteF32(target + at + 12, sa * a);
            }
        }

        private static void Sync()
        {
            if (ReferenceEquals(_source, SceneDirectives.Current)) return;
            _source = SceneDirectives.Current;
            _colors.Clear();
            _hidden.Clear();

            foreach (SceneDirectives.Entry row in _source)
            {
                Dictionary<long, List<SceneDirectives.Entry>> table;
                if (row.Kind == SceneDirectives.Kind.SetColor) table = _colors;
                else if (row.Kind == SceneDirectives.Kind.HideGroup) table = _hidden;
                else continue;

                if (!table.TryGetValue(row.Group, out List<SceneDirectives.Entry> list))
                    table[row.Group] = list = new List<SceneDirectives.Entry>();
                list.Add(row);
            }
        }

        internal static void Reset()
        {
            _retiredGeneration = -1;
            Forget();
            NoInputSky.Forget();
            TrackDisplay.Forget();
            OverlayText.Clear();
            TrackMove.Forget();
            AudioOffset.Forget();
            GroupSpeed.Forget();
        }

        internal static void Retire()
        {
            foreach (KeyValuePair<IntPtr, Applied> item in _applied)
            {
                try
                {
                    if (!Memory.LooksLikeObject(item.Key)) continue;

                    if (item.Value.Tinted && item.Value.Original != IntPtr.Zero)
                        Memory.WritePtr(item.Key + AreaColorDetails, item.Value.Original);

                    if (item.Value.Hidden)
                    {
                        IntPtr renderer = Memory.Ptr(item.Key + AreaRenderer);
                        if (Memory.LooksLikeObject(renderer))
                        {
                            var target = new UnityEngineCore::UnityEngine.Renderer(renderer);
                            if (target != null) target.enabled = true;
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (IntPtr clone in _clones.Values)
            {
                try
                {
                    if (Memory.LooksLikeObject(clone))
                        UnityEngineCore::UnityEngine.Object.Destroy(
                            new UnityEngineCore::UnityEngine.Object(clone));
                }
                catch
                {
                }
            }

            NoInputSky.Retire();
            TrackDisplay.Retire();
            OverlayText.Clear();
            TrackMove.Retire();
            _retiredGeneration = SceneDirectives.Generation;
            Forget();
        }

        private static void Forget()
        {
            _applied.Clear();
            _clones.Clear();
            _layouts.Clear();
            _colors.Clear();
            _hidden.Clear();
            _probed = false;
            _source = Array.Empty<SceneDirectives.Entry>();
        }

        private static void NoteFailure(string where, Exception e)
        {
            if (_failures++ < 3)
                ModLog.Warn($"scene rows: {where} failed: {ModLog.Describe(e)}");
        }
    }
}

extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomSong
{
    using UnityEngineCore::UnityEngine;

    internal static class NoInputSky
    {
        private const int NoteSize = 0x80;
        private const int NoteId = 0x00;
        private const int NoteGroup = 0x08;
        private const int NoteSide = 0x10;
        private const int NoteType = 0x14;
        private const int NoteStart = 0x18;
        private const int NoteEnd = 0x1C;
        private const int NoteStartCenter = 0x20;
        private const int NoteEndCenter = 0x2C;
        private const int NoteStartWidth = 0x38;
        private const int NoteEndWidth = 0x44;
        private const int NoteLead = 0x50;
        private const int NoteConsumed = 0x70;
        private const int NoteFlags = 0x74;
        private const int SideSky = 4;
        private const int TypeSkyArea = 5;
        private const int FlagFirst = 0x2000;
        private const int FlagLast = 0x4000;
        private const int AreaMeshRenderer = 0x30;
        private const int AreaColorDetails = 0x38;
        private const int TrackNotesContainer = 0x1F0;
        private const int TrackHomePosition = 0x318;
        private const int TrackRestPosition = 0x38C;
        private const int TrackWidth = 0x478;
        private const int TrackNoteWidthScale = 0x47C;
        private const int TrackBaseSpeed = 0x4A8;
        private const int TrackDepthFactor = 0x4C0;
        private const int TrackEngine = 0x830;
        private const int EngineChartTime = 0xD0;
        private const double NativeQuickLeadMs = 2000.0;
        private const string SceneNamespace = "ifapp.Game.Scenes.Game";
        private const string GameImage = "Game.dll";

        private sealed class Entry
        {
            internal IntPtr Band;
            internal IntPtr Shadow;
            internal IntPtr BandTransform;
            internal IntPtr ShadowTransform;
            internal IntPtr BandArea;
            internal IntPtr ShadowArea;
            internal bool Shown;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Vec2
        {
            internal float X;
            internal float Y;
        }

        private static readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        private static SceneDirectives.SkyVisual[] _source = Array.Empty<SceneDirectives.SkyVisual>();
        private static IntPtr _kzMethod;
        private static bool _askedKz;
        private static bool _unavailable;
        private static int _trackWidth, _noteWidthScale, _baseSpeed, _depthFactor;
        private static int _notesContainer, _restPosition, _homePosition;
        private static int _prefab, _prefabShadow, _prefabSide;
        private static int _areaMeshRenderer = AreaMeshRenderer;
        private static int _areaColor = AreaColorDetails;
        private static int _areaMeshFilter = -1;
        private static int _trackColor = -1;
        private static IntPtr _trackClass;
        private static bool _fieldsAsked;
        private static IntPtr _getComponent;
        private static IntPtr _componentType, _filterType, _rendererType;
        private static IntPtr _filterClass, _rendererClass, _areaClass;
        private static int _failures;
        private static int _built;
        private static int _markers;
        private static int _retiredGeneration = -1;

        internal static void Tick(IntPtr track)
        {
            if (_retiredGeneration >= 0 && _retiredGeneration == SceneDirectives.Generation) return;
            SceneDirectives.SkyVisual[] visuals = SceneDirectives.CurrentVisuals;
            if (visuals == null || visuals.Length == 0)
            {
                if (_source.Length != 0) Forget();
                return;
            }

            if (!Memory.LooksLikeObject(track)) return;

            if (!ReferenceEquals(_source, visuals))
            {
                DestroyAll();
                _source = visuals;
            }

            if (_unavailable) return;
            if (!Fields(track)) return;
            if (!Kz()) return;
            double now = NowMs(track);
            if (double.IsNaN(now)) return;
            double trackLength = Memory.F64(track + _baseSpeed);
            double speed = Memory.F64(track + _depthFactor);
            if (!Finite(trackLength) || !Finite(speed) || Math.Abs(speed) < 1e-9) return;
            float bandScale = Memory.F32(track + _trackWidth);
            float shadowScale = Memory.F32(track + _noteWidthScale);
            double trackScale = trackLength / 3.333333334;
            double visualScale = trackScale * speed;
            if (!Finite(visualScale) || Math.Abs(visualScale) < 1e-9) return;
            double zPerMs = visualScale / 1000.0;
            double maxLead = Math.Max(NativeQuickLeadMs, Math.Abs(trackLength / speed) * 1000.0);
            double noClipTail = Math.Min(120000.0, 8000.0 / Math.Abs(speed));
            IntPtr rest = track + _restPosition;
            IntPtr home = track + _homePosition;

            for (int i = 0; i < visuals.Length; i++)
            {
                SceneDirectives.SkyVisual visual = visuals[i];
                double lead = visual.Start - now;
                double duration = visual.End - visual.Start;
                if (duration <= 0.0) continue;
                double progress = (now - visual.Start) / duration;
                if (progress < 0.0) progress = 0.0;
                if (progress > 1.0) progress = 1.0;

                if (lead > maxLead || (!visual.NoClip && lead + duration < 0.0) ||
                    (visual.NoClip && lead < -noClipTail))
                {
                    Hide(visual.Id);
                    continue;
                }

                int hidden = visual.TimingGroup & 0x1FFFFFFF;
                if (hidden != 0 && SceneControl.HiddenNow(hidden, now))
                {
                    Hide(visual.Id);
                    continue;
                }

                if (!_entries.TryGetValue(visual.Id, out Entry entry))
                {
                    entry = visual.IsSkyNote ? CreateMarker(track, visual) : Create(track, visual.Id);
                    if (entry == null) continue;
                    _entries[visual.Id] = entry;
                }

                float center = visual.StartCenter;
                double bandX = -bandScale * 0.5 + bandScale * center;
                double shadowX = -shadowScale * 0.5 + shadowScale * center;
                double z = zPerMs * lead;
                Show(entry);

                if (visual.IsSkyNote)
                {
                    Place(entry, rest, bandX, z, 0f);
                    continue;
                }

                if (entry.BandArea != IntPtr.Zero)
                {
                    Paint(entry.BandArea, visual.Group, now);
                    Build(entry.BandArea, entry.Band, visual, lead, now, bandScale, visualScale,
                          visualScale, progress, bandX, z, rest);
                }

                if (entry.ShadowArea != IntPtr.Zero)
                {
                    Paint(entry.ShadowArea, visual.Group, now);
                    Build(entry.ShadowArea, entry.Shadow, visual, lead, now, shadowScale, visualScale,
                          trackScale, progress, shadowX, z, home);
                }
            }
        }

        private static unsafe void Build(IntPtr area, IntPtr gameObject, SceneDirectives.SkyVisual visual,
                                         double lead, double now, float scale, double lengthScale,
                                         double zScale, double progress, double x, double z,
                                         IntPtr origin)
        {
            double durationSeconds = (visual.End - visual.Start) / 1000.0;
            double visualLength = durationSeconds * lengthScale;
            double chartSeconds = now / 1000.0;
            double noteStartSeconds = chartSeconds + lead / 1000.0;

            if (visual.NoClip)
            {
                progress = 0.0;
            }

            byte* buffer = stackalloc byte[NoteSize];
            new Span<byte>(buffer, NoteSize).Clear();
            var note = (IntPtr)buffer;
            Memory.WriteI64(note + NoteId, visual.Id);
            Memory.WriteI64(note + NoteGroup, visual.Group);
            Memory.WriteI32(note + NoteSide, SideSky);
            Memory.WriteI32(note + NoteType, TypeSkyArea);
            Memory.WriteI32(note + NoteStart, visual.Start);
            Memory.WriteI32(note + NoteEnd, visual.End);
            WriteBand(note + NoteStartCenter, visual.StartCenter);
            WriteBand(note + NoteEndCenter, visual.EndCenter);
            WriteBand(note + NoteStartWidth, visual.StartWidth);
            WriteBand(note + NoteEndWidth, visual.EndWidth);
            Memory.WriteF64(note + NoteLead, lead);
            Memory.WriteU8(note + NoteConsumed, 0);
            int flags = visual.LeftEase | visual.RightEase;
            if (visual.IsFirst) flags |= FlagFirst;
            if (visual.IsLast) flags |= FlagLast;
            Memory.WriteI32(note + NoteFlags, flags);
            float extra = 0f;
            Vec2 previous = new Vec2 { X = visual.PrevLo, Y = visual.PrevHi };
            Vec2 next = new Vec2 { X = visual.NextLo, Y = visual.NextHi };
            IntPtr* args = stackalloc IntPtr[10];
            args[0] = note;
            args[1] = (IntPtr)(&scale);
            args[2] = (IntPtr)(&chartSeconds);
            args[3] = (IntPtr)(&noteStartSeconds);
            args[4] = (IntPtr)(&visualLength);
            args[5] = (IntPtr)(&zScale);
            args[6] = (IntPtr)(&extra);
            args[7] = (IntPtr)(&progress);
            args[8] = (IntPtr)(&previous);
            args[9] = (IntPtr)(&next);
            IntPtr raised = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(_kzMethod, area, (void**)args, ref raised);
            if (raised != IntPtr.Zero) return;
            if (!Memory.LooksLikeObject(gameObject)) return;
            Transform transform = new GameObject(gameObject).transform;
            if (transform == null) return;

            transform.localPosition = new Vector3(
                Memory.F32(origin) + (float)x,
                Memory.F32(origin + 4),
                Memory.F32(origin + 8) + (float)z);
        }

        private static void WriteBand(IntPtr at, float value)
        {
            const int denominator = 1000000;
            Memory.WriteI32(at, (int)Math.Round(value * denominator));
            Memory.WriteI32(at + 4, denominator);
            Memory.WriteF32(at + 8, value);
        }

        private static void Paint(IntPtr area, long group, double now)
        {
            IntPtr current = Memory.Ptr(area + _areaColor);
            if (!Memory.LooksLikeObject(current)) return;
            IntPtr wanted = SceneControl.ColorFor(group, now, current);
            if (wanted == IntPtr.Zero || wanted == current) return;
            Memory.WritePtr(area + _areaColor, wanted);
        }

        private static Entry Create(IntPtr track, long id)
        {
            try
            {
                IntPtr prefab = Memory.Ptr(track + _prefab);
                IntPtr shadowPrefab = Memory.Ptr(track + _prefabShadow);
                IntPtr container = Memory.Ptr(track + _notesContainer);

                if (!Memory.LooksLikeObject(prefab) || !Memory.LooksLikeObject(container))
                    return null;
                var parent = new Transform(container);
                if (parent == null) return null;
                var entry = new Entry();
                entry.Band = Spawn(prefab, parent, $"CustomSongNoInputSky_{id}");
                entry.Shadow = Spawn(shadowPrefab, parent, $"CustomSongNoInputSkyShadow_{id}");
                if (entry.Band == IntPtr.Zero || entry.Shadow == IntPtr.Zero)
                {
                    Destroy(entry);
                    return null;
                }

                entry.BandTransform = new GameObject(entry.Band).transform.Pointer;
                entry.ShadowTransform = new GameObject(entry.Shadow).transform.Pointer;
                entry.BandArea = Area(entry.Band, track);
                entry.ShadowArea = Area(entry.Shadow, track);

                if (_built++ < 3)
                    {
                    }

                return entry;
            }
            catch (Exception e)
            {
                NoteFailure("build", e);
                return null;
            }
        }

        private static void Place(Entry entry, IntPtr origin, double x, double z, float size)
        {
            if (entry.BandTransform == IntPtr.Zero) return;

            try
            {
                var transform = new Transform(entry.BandTransform);
                if (transform == null) return;

                transform.localPosition = new Vector3(
                    Memory.F32(origin) + (float)x,
                    Memory.F32(origin + 4),
                    Memory.F32(origin + 8) + (float)z);
                if (size > 0f) transform.localScale = new Vector3(size, size, size);
            }
            catch (Exception e)
            {
                NoteFailure("marker", e);
            }
        }

        private static Entry CreateMarker(IntPtr track, SceneDirectives.SkyVisual visual)
        {
            try
            {
                if (_prefabSide < 0) return null;
                IntPtr prefab = Memory.Ptr(track + _prefabSide);
                IntPtr container = Memory.Ptr(track + _notesContainer);

                if (!Memory.LooksLikeObject(prefab) || !Memory.LooksLikeObject(container))
                    return null;
                var parent = new Transform(container);
                if (parent == null) return null;

                var entry = new Entry
                {
                    Band = Spawn(prefab, parent, $"CustomSongSkyNote_{visual.Id}"),
                };
                if (entry.Band == IntPtr.Zero) return null;
                entry.BandTransform = new GameObject(entry.Band).transform.Pointer;
                StripGlow(entry.Band);

                if (_built++ < 6)
                    {
                    }

                return entry;
            }
            catch (Exception e)
            {
                NoteFailure("marker build", e);
                return null;
            }
        }

        private static void StripGlow(IntPtr gameObjectPointer)
        {
            try
            {
                var root = new GameObject(gameObjectPointer);
                Transform[] parts = root.GetComponentsInChildren<Transform>(true);
                int off = 0;
                var names = new System.Text.StringBuilder();

                foreach (Transform part in parts)
                {
                    if (part == null) continue;
                    string name = part.name ?? "";
                    if (name.Length == 0) continue;
                    if (_markers < 2 && names.Length < 200) names.Append(name).Append(", ");
                    string lower = name.ToLowerInvariant();
                    if (!lower.Contains("guide") && !lower.Contains("glow") && !lower.Contains("effect")) continue;
                    GameObject child = part.gameObject;
                    if (child == null) continue;
                    child.SetActive(false);
                    off++;
                }

                if (_markers++ < 2)
                    {
                    }
            }
            catch (Exception e)
            {
                NoteFailure("marker glow", e);
            }
        }

        private static IntPtr Spawn(IntPtr prefab, Transform parent, string name)
        {
            var source = new GameObject(prefab);
            Object spawned = Object.Instantiate(source, parent);
            if (spawned == null) return IntPtr.Zero;
            var copy = new GameObject(spawned.Pointer);
            copy.name = name;
            if (!copy.activeSelf) copy.SetActive(true);
            return copy.Pointer;
        }

        private static IntPtr Area(IntPtr gameObjectPointer, IntPtr track)
        {
            IntPtr area = Component(gameObjectPointer, ref _componentType, ref _areaClass,
                                    SceneNamespace, "SkySafeArea");
            if (area == IntPtr.Zero) return IntPtr.Zero;

            IntPtr filter = Component(gameObjectPointer, ref _filterType, ref _filterClass,
                                      "UnityEngine", "MeshFilter");
            IntPtr renderer = Component(gameObjectPointer, ref _rendererType, ref _rendererClass,
                                        "UnityEngine", "MeshRenderer");

            if (_areaMeshFilter >= 0 && filter != IntPtr.Zero)
                Memory.WritePtr(area + _areaMeshFilter, filter);

            if (_areaMeshRenderer >= 0 && renderer != IntPtr.Zero)
                Memory.WritePtr(area + _areaMeshRenderer, renderer);

            if (renderer != IntPtr.Zero)
            {
                try
                {
                    var target = new MeshRenderer(renderer);
                    target.enabled = true;
                    target.forceRenderingOff = false;
                    Material shared = target.sharedMaterial;
                    if (shared != null) target.sharedMaterial = new Material(shared);
                }
                catch (Exception e)
                {
                    NoteFailure("materials", e);
                }
            }

            IntPtr details = Memory.Ptr(track + _trackColor);
            if (_trackColor >= 0 && Memory.LooksLikeObject(details))
                Memory.WritePtr(area + _areaColor, details);
            return area;
        }

        private static void Show(Entry entry)
        {
            if (entry.Shown) return;

            try
            {
                if (entry.Band != IntPtr.Zero)
                {
                    var go = new GameObject(entry.Band);
                    if (!go.activeSelf) go.SetActive(true);
                }
                if (entry.Shadow != IntPtr.Zero)
                {
                    var go = new GameObject(entry.Shadow);
                    if (!go.activeSelf) go.SetActive(true);
                }
                entry.Shown = true;
            }
            catch
            {
            }
        }

        private static void Hide(long id)
        {
            if (!_entries.TryGetValue(id, out Entry entry)) return;
            if (!entry.Shown) return;

            try
            {
                if (entry.Band != IntPtr.Zero)
                {
                    var go = new GameObject(entry.Band);
                    if (go.activeSelf) go.SetActive(false);
                }
                if (entry.Shadow != IntPtr.Zero)
                {
                    var go = new GameObject(entry.Shadow);
                    if (go.activeSelf) go.SetActive(false);
                }
                entry.Shown = false;
            }
            catch
            {
            }
        }

        internal static void Forget()
        {
            _entries.Clear();
            _source = Array.Empty<SceneDirectives.SkyVisual>();
            _failures = 0;
            _built = 0;
            _retiredGeneration = -1;
        }

        internal static void Retire()
        {
            DestroyAll();
            Forget();
            _retiredGeneration = SceneDirectives.Generation;
        }

        private static void DestroyAll()
        {
            foreach (KeyValuePair<long, Entry> item in _entries)
                Destroy(item.Value);
            _entries.Clear();
        }

        private static void Destroy(Entry entry)
        {
            try
            {
                if (entry.Band != IntPtr.Zero) Object.Destroy(new GameObject(entry.Band));
                if (entry.Shadow != IntPtr.Zero) Object.Destroy(new GameObject(entry.Shadow));
            }
            catch
            {
            }
        }

        private static double NowMs(IntPtr track)
        {
            IntPtr engine = Memory.Ptr(track + TrackEngine);
            if (!Memory.LooksLikeObject(engine)) return double.NaN;
            return Memory.F64(engine + EngineChartTime) * 1000.0;
        }

        private static bool Fields(IntPtr track)
        {
            if (_fieldsAsked) return !_unavailable;
            _fieldsAsked = true;

            try
            {
                _trackClass = IL2CPP.il2cpp_object_get_class(track);
                if (_trackClass == IntPtr.Zero) { Disable("the track class was not readable"); return false; }

                _notesContainer = Field(_trackClass, "notesContainer", TrackNotesContainer);
                _restPosition = Field(_trackClass, "_enA", TrackRestPosition);
                _homePosition = Field(_trackClass, "_ZMA", TrackHomePosition);
                _trackWidth = Field(_trackClass, "_nnA", TrackWidth);
                _noteWidthScale = Field(_trackClass, "_NnA", TrackNoteWidthScale);
                _baseSpeed = Field(_trackClass, "_qnA", TrackBaseSpeed);
                _depthFactor = Field(_trackClass, "_RnA", TrackDepthFactor);
                _prefab = Field(_trackClass, "safeAreaPrefab", -1);
                _prefabShadow = Field(_trackClass, "safeAreaShadowPrefab", -1);
                _prefabSide = Field(_trackClass, "sideNotePrefab", -1);
                IntPtr areaClass = MethodResolver.ClassByFullName(SceneNamespace, "SkySafeArea");
                if (areaClass != IntPtr.Zero)
                {
                    _areaMeshFilter = Field(areaClass, "meshFilter", -1);
                    _areaMeshRenderer = Field(areaClass, "meshRenderer", AreaMeshRenderer);
                    _areaColor = Field(areaClass, "colorDetails", AreaColorDetails);
                }

                _trackColor = Field(_trackClass, "safeAreaColorDetails", -1);

                if (_prefab < 0 || _prefabShadow < 0 || _notesContainer < 0)
                {
                    Disable("this build does not name the sky-area prefabs on Track");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                NoteFailure("fields", e);
                _unavailable = true;
                return false;
            }
        }

        private static void Disable(string why)
        {
            if (_unavailable) return;
            _unavailable = true;
            ModLog.WarnRelease($"noinput sky areas will not be drawn: {why}");
        }

        private static bool Kz()
        {
            if (_kzMethod != IntPtr.Zero) return true;
            if (_askedKz) return false;
            _askedKz = true;
            _kzMethod = MethodResolver.MethodInfoByRuntime("SkySafeArea", "_KZ", GameImage, SceneNamespace);
            if (_kzMethod == IntPtr.Zero)
            {
                Disable("SkySafeArea._KZ was not found");
                return false;
            }

            return true;
        }

        private static int Field(IntPtr klass, string name, int fallback)
        {
            if (klass == IntPtr.Zero) return fallback;
            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            if (field == IntPtr.Zero) return fallback;
            int offset = (int)IL2CPP.il2cpp_field_get_offset(field);
            if (IL2CPP.il2cpp_class_is_valuetype(klass)) offset -= Offsets.Runtime.ObjectHeader;
            return offset;
        }

        private static IntPtr Component(IntPtr gameObject, ref IntPtr type, ref IntPtr klass,
                                        string namespaze, string name)
        {
            if (gameObject == IntPtr.Zero) return IntPtr.Zero;

            if (type == IntPtr.Zero)
            {
                if (klass == IntPtr.Zero)
                    klass = MethodResolver.ClassByFullName(namespaze, name);
                if (klass == IntPtr.Zero) return IntPtr.Zero;
                type = IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(klass));
                if (type == IntPtr.Zero) return IntPtr.Zero;
            }

            if (_getComponent == IntPtr.Zero)
            {
                _getComponent = MethodResolver.MethodInfoByRuntime(
                    "GameObject", "GetComponent", "UnityEngine.CoreModule.dll", "UnityEngine", 1);
                if (_getComponent == IntPtr.Zero) return IntPtr.Zero;
            }

            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = type;
                IntPtr raised = IntPtr.Zero;
                IntPtr component = IL2CPP.il2cpp_runtime_invoke(_getComponent, gameObject,
                                                                (void**)args, ref raised);
                if (raised != IntPtr.Zero) return IntPtr.Zero;
                return Memory.LooksLikeObject(component) ? component : IntPtr.Zero;
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void NoteFailure(string where, Exception e)
        {
            if (_failures++ < 3)
                {
                ModLog.Warn($"scene rows: noinput bar {where} failed: {ModLog.Describe(e)}");
                }
        }
    }
}

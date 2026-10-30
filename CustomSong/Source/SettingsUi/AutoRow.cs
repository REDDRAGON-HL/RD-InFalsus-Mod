extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static unsafe class AutoRow
    {
        private const string Title = "AUTO";
        private const float DefaultPitch = 0.03714882f;
        private const int BatchMain = 0x90;
        private const int BatchOther = 0x98;
        private const int BatchMainCalc = 0xA0;
        private const int BatchOtherCalc = 0xA8;
        private const int CalcSize = 0x3C;
        private const int BatchMainCount = 0xE0;
        private const int BatchOtherCount = 0xE4;
        private const int PendingQueue = 0x38;
        private const int YgItems = 0x10;
        private const int YgCount = 0x18;
        private const int YgCapacity = 0x1C;
        private const int ScrollBound = 0x80;
        private static int _panelSource = 0x58;
        private static int _panelAnchor = 0x68;
        private static int _panelScroll = 0x90;
        private static int _panelTooltip = 0xC0;
        private static int _barSelf = 0x20;
        private static int _barTitle = 0x58;
        private static int _barIndex = 0x70;
        private static int _barValue = 0x74;
        private static int _barSelected = 0x48;
        private static int _barTooltip = 0x78;
        private static int _c2dId = 0x178;
        private static int _c2dOffsetX = 0xE0;
        private static int _c2dOffsetY = 0xE4;
        private static int _c2dSize = 0xC8;
        private static int _c2dType = 0xEC;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> _reset;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void> _wire;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> _select;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> _state;
        private static delegate* unmanaged[Cdecl]<byte, IntPtr, void> _recompute;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort> _admit;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> _touch;
        private static IntPtr _enabled;
        private static IntPtr _setEnabled;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, void> _setText;
        private static IntPtr _panel;
        private static IntPtr _pending;
        private static IntPtr _row;
        private static IntPtr _scroll;
        private static float _bound;
        private static IntPtr _clone;
        private static int _late;
        private static int _repaint;
        private static int _barButtons = 0x28;
        private static bool _resolved;
        private static bool _failed;
        private static int _attempts;

        internal static bool Present
        {
            get
            {
                if (!Memory.LooksLikeObject(_row)) return false;

                try
                {
                    return new Component(_row).gameObject != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void Attach(IntPtr panel)
        {
            if (_failed || !Memory.LooksLikeObject(panel)) return;

            if (panel == _panel && Present)
            {
                Apply();
                return;
            }

            if (panel == _pending) return;
            _pending = panel;
        }

        internal static void Tick()
        {
            if (Present && _late > 0 && --_late == 0) Refresh();
            if (Present && _repaint > 0 && --_repaint == 0) Apply();

            if (!Present && !_failed && _panel != IntPtr.Zero && _attempts < 4)
            {
                _pending = _panel;
                _attempts++;
            }

            if (_pending == IntPtr.Zero || _failed) return;

            try
            {
                GameObject page = new Component(_pending).gameObject;
                if (page == null || !page.activeInHierarchy) return;
            }
            catch (Exception)
            {
                return;
            }

            IntPtr panel = _pending;
            _pending = IntPtr.Zero;
            if (!Resolve()) return;

            try
            {
                if (!Build(panel)) return;
                _panel = panel;
                _attempts = 0;
                Apply();
            }
            catch (Exception e)
            {
                _failed = true;
                ModLog.Error("AUTO row: could not be added: " + ModLog.Describe(e));
            }
        }

        private static bool Build(IntPtr panel)
        {
            IntPtr source = Memory.Ptr(panel + _panelSource);
            if (!Memory.LooksLikeObject(source)) return Fail("the damage row is not readable");
            IntPtr anchor = Memory.Ptr(panel + _panelAnchor);
            if (!Memory.LooksLikeObject(anchor)) return Fail("the last row is not readable");
            GameObject sourceObject = new Component(source).gameObject;
            GameObject anchorObject = new Component(anchor).gameObject;
            if (sourceObject == null || anchorObject == null) return Fail("the row prefabs have no object");
            Transform parent = anchorObject.transform.parent;
            if (parent == null) return Fail("the last row has no parent");
            IntPtr anchorSelf = Memory.Ptr(anchor + _barSelf);
            float pitch = Pitch(anchorObject.transform, anchorSelf);
            float anchorY = Memory.F32(anchorSelf + _c2dOffsetY);

            UnityEngineCore::UnityEngine.Object created =
                UnityEngineCore::UnityEngine.Object.Instantiate(sourceObject, parent);
            if (created == null) return Fail("the row could not be cloned");
            GameObject clone = new GameObject(created.Pointer);
            clone.name = "AUTO";
            IntPtr row = Find(clone, "UiSettingsMultiselectorBar");
            if (row == IntPtr.Zero)
            {
                UnityEngineCore::UnityEngine.Object.Destroy(created);
                return Fail("the clone has no option row");
            }

            try
            {
                clone.transform.SetSiblingIndex(anchorObject.transform.GetSiblingIndex() + 1);
            }
            catch (Exception e)
            {
                ModLog.Warn("AUTO row: the clone could not be placed after the last row: " +
                            ModLog.Describe(e));
            }

            IntPtr cloneSelf = Memory.Ptr(row + _barSelf);
            Memory.WriteF32(cloneSelf + _c2dOffsetX, Memory.F32(anchorSelf + _c2dOffsetX));
            Memory.WriteF32(cloneSelf + _c2dOffsetY, anchorY + pitch);
            int sourceValue = Memory.I32(source + _barValue);
            Memory.WriteI32(row + _barValue, sourceValue > 0 ? sourceValue : 1);
            Memory.WritePtr(row + _barTooltip, Memory.Ptr(source + _barTooltip));

            MakeRoom(parent, parent.localPosition.y, pitch);
            if (_wire != null) _wire(row, panel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            int registered = Register(clone);
            if (registered == 0)
            {
                UnityEngineCore::UnityEngine.Object.Destroy(created);
                return Fail("the clone could not be registered with the C2D batch");
            }

            int synced = Sync(sourceObject.transform, clone.transform);
            int dressed = Dress(clone);
            Owning(clone, row, sourceObject, panel);
            IntPtr container = Find(parent.gameObject, "Constrained2D");
            if (container != IntPtr.Zero && _touch != null) _touch(container, IntPtr.Zero);
            if (_touch != null) _touch(cloneSelf, IntPtr.Zero);
            Recompute();
            IntPtr scroll = Memory.Ptr(panel + _panelScroll);
            if (Memory.LooksLikeObject(scroll))
            {
                float step = Math.Abs(pitch);
                _scroll = scroll;
                _bound = Memory.F32(scroll + ScrollBound) + step;
                Stretch();
            }

            _row = row;
            _clone = created.Pointer;
            _late = 10;
            return true;
        }

        private static float Pitch(Transform anchor, IntPtr anchorSelf)
        {
            Transform parent = anchor.parent;
            int at = anchor.GetSiblingIndex();

            if (parent != null && at > 0)
            {
                Transform previous = parent.GetChild(at - 1);
                IntPtr above = previous == null ? IntPtr.Zero : Find(previous.gameObject, "Constrained2D");
                float mine = anchorSelf == IntPtr.Zero ? 0f : Memory.F32(anchorSelf + _c2dOffsetY);
                float theirs = above == IntPtr.Zero ? 0f : Memory.F32(above + _c2dOffsetY);
                float gap = mine - theirs;

                if (above != IntPtr.Zero && gap != 0f) return gap;
            }

            return DefaultPitch;
        }

        private static void MakeRoom(Transform section, float at, float pitch)
        {
            Transform contents = section.parent;
            if (contents == null) return;
            int moved = 0;
            for (int i = 0; i < contents.childCount; i++)
            {
                Transform child = contents.GetChild(i);
                if (child == null || child.Pointer == section.Pointer) continue;
                if (child.localPosition.y >= at) continue;
                IntPtr c2d = Find(child.gameObject, "Constrained2D");
                if (c2d == IntPtr.Zero) continue;
                Memory.WriteF32(c2d + _c2dOffsetY, Memory.F32(c2d + _c2dOffsetY) + pitch);
                moved++;
            }
        }

        private static int Register(GameObject clone)
        {
            List<IntPtr> c2ds = C2Ds(clone);
            if (c2ds.Count == 0) return 0;
            List<IntPtr> off = Open(clone);
            List<IntPtr> asleep = Enable(c2ds);
            int placed = Place(c2ds);
            int before = Queued();
            foreach (IntPtr c2d in c2ds) _reset(c2d, IntPtr.Zero);
            int queued = Queued() - before;
            if (queued < c2ds.Count) queued += Append(c2ds, c2ds.Count - queued);
            Recompute();
            IntPtr statics = WiStatics();
            if (statics == IntPtr.Zero) return 0;
            HashSet<IntPtr> live = Contents(statics);
            List<IntPtr> missing = new List<IntPtr>();
            foreach (IntPtr c2d in c2ds)
                if (!live.Contains(c2d)) missing.Add(c2d);

            if (missing.Count > 0 && _admit != null && ParentHasLayout(clone))
            {
                foreach (IntPtr c2d in missing) _admit(c2d, IntPtr.Zero);
                Recompute();
                live = Contents(statics);
            }

            int found = 0;
            int ids = 0;
            foreach (IntPtr c2d in c2ds)
            {
                if (Memory.U16(c2d + _c2dId) != 0xFFFF) ids++;
                if (live.Contains(c2d)) found++;
            }

            Restore(off);
            Recompute();
            return found;
        }

        private static int Place(List<IntPtr> c2ds)
        {
            if (_admit == null) return 0;
            int placed = 0;
            foreach (IntPtr c2d in c2ds)
            {
                if (Memory.U16(c2d + _c2dId) != 0xFFFF) continue;

                try
                {
                    ushort slot = _admit(c2d, IntPtr.Zero);
                    if (slot == 0) continue;
                    Memory.WriteU16(c2d + _c2dId, slot);
                    placed++;
                }
                catch (Exception e)
                {
                    ModLog.WarnRelease("AUTO row: a part could not be placed: " + ModLog.Describe(e));
                }
            }

            return placed;
        }

        private static List<IntPtr> Open(GameObject clone)
        {
            var off = new List<IntPtr>();
            Each(clone.transform, t =>
            {
                GameObject go = t.gameObject;
                if (go == null || go.activeSelf) return;
                go.SetActive(true);
                off.Add(go.Pointer);
            });
            return off;
        }

        private static List<IntPtr> Enable(List<IntPtr> c2ds)
        {
            var woke = new List<IntPtr>();
            IntPtr set = _setEnabled;
            if (set == IntPtr.Zero) return woke;

            foreach (IntPtr c2d in c2ds)
            {
                if (BehaviourEnabled(c2d) != 0) continue;

                try
                {
                    ((delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)set)(c2d, 1, IntPtr.Zero);
                    woke.Add(c2d);
                }
                catch (Exception)
                {
                }
            }

            return woke;
        }

        private static void Restore(List<IntPtr> off)
        {
            for (int i = off.Count - 1; i >= 0; i--)
            {
                if (!Memory.LooksLikeObject(off[i])) continue;

                try
                {
                    new GameObject(off[i]).SetActive(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Each(Transform parent, Action<Transform> visit)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                visit(child);
                Each(child, visit);
            }
        }

        private static void Recompute()
        {
            try
            {
                _recompute(1, IntPtr.Zero);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the layout pass failed: " + ModLog.Describe(e));
            }
        }

        private static bool ParentHasLayout(GameObject clone)
        {
            Transform transform = clone.transform;
            Transform parent = transform == null ? null : transform.parent;
            return parent != null && Find(parent.gameObject, "Constrained2D") != IntPtr.Zero;
        }

        private static int Append(List<IntPtr> c2ds, int take)
        {
            IntPtr statics = WiStatics();
            if (statics == IntPtr.Zero) return 0;
            IntPtr list = Memory.Ptr(statics + PendingQueue);
            if (!Memory.LooksLikeObject(list)) return 0;
            IntPtr items = Memory.Ptr(list + YgItems);
            if (!Memory.LooksLikeObject(items)) return 0;
            int count = Memory.I32(list + YgCount);
            int capacity = Memory.I32(list + YgCapacity);
            int length = ArrayLength(items);
            if (count < 0 || capacity < count || length < count) return 0;
            int room = Math.Min(length - count, take);
            for (int i = 0; i < room; i++)
                Memory.WritePtr(items + Offsets.Runtime.ArrayDataOffset + (count + i) * IntPtr.Size, c2ds[i]);
            if (room > 0) Memory.WriteI32(list + YgCount, count + room);
            return room;
        }

        private static int Queued()
        {
            IntPtr statics = WiStatics();
            if (statics == IntPtr.Zero) return 0;
            IntPtr list = Memory.Ptr(statics + PendingQueue);
            return Memory.LooksLikeObject(list) ? Memory.I32(list + YgCount) : 0;
        }

        private static IntPtr WiStatics()
        {
            IntPtr klass = FieldResolver.ClassPointer("_wi");
            return klass == IntPtr.Zero ? IntPtr.Zero : FieldResolver.Statics(klass);
        }

        private static HashSet<IntPtr> Contents(IntPtr statics)
        {
            var live = new HashSet<IntPtr>();
            Collect(live, Memory.Ptr(statics + BatchMain));
            Collect(live, Memory.Ptr(statics + BatchOther));
            return live;
        }

        private static void Collect(HashSet<IntPtr> live, IntPtr array)
        {
            int length = ArrayLength(array);
            for (int i = 0; i < length; i++)
            {
                IntPtr entry = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (entry != IntPtr.Zero) live.Add(entry);
            }
        }

        private static int ArrayLength(IntPtr array)
        {
            if (!Memory.LooksLikeObject(array)) return 0;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            return length > 0 && length <= 0x1_0000 ? length : 0;
        }

        internal static void Apply()
        {
            if (!Present) return;

            try
            {
                int index = AutoSwitch.On ? 1 : 0;
                Memory.WriteI32(_row + _barIndex, index);
                Flip(index);
                if (_select != null) _select(_row, index, IntPtr.Zero);
                Stretch();

                if (!_applied)
                {
                    _applied = true;
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the value could not be shown: " + ModLog.Describe(e));
            }
        }

        private static bool _applied;

        private static void Owning(GameObject clone, IntPtr row, GameObject sourceObject, IntPtr panel)
        {
            try
            {
                IntPtr buttons = Memory.Ptr(row + _barButtons);
                int length = ArrayLength(buttons);
                if (length <= 0) return;
                List<IntPtr> ours = Buttons(clone);
                List<IntPtr> theirs = sourceObject == null ? null : Buttons(sourceObject);
                int inside = 0;
                int wired = 0;
                bool moved = false;
                IntPtr fresh = buttons;

                for (int i = 0; i < length; i++)
                {
                    IntPtr button = Memory.Ptr(buttons + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                    if (Media(ours, button))
                    {
                        inside++;
                    }
                    else
                    {
                        int at = theirs == null ? -1 : theirs.IndexOf(button);
                        if (at < 0 || at >= ours.Count) continue;

                        if (fresh == buttons)
                        {
                            fresh = NewArray(buttons, length);
                            moved = true;
                        }

                        Memory.WritePtr(fresh + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size, ours[at]);
                        button = ours[at];
                        inside++;
                    }

                    if (Memory.LooksLikeObject(Memory.Ptr(button + 0x30))) wired++;
                }

                if (moved) Memory.WritePtr(row + _barButtons, fresh);

                for (int i = 0; i < length; i++)
                {
                    IntPtr button = Memory.Ptr(Memory.Ptr(row + _barButtons) +
                                               Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                }

                if (moved && _wire != null) _wire(row, panel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the buttons could not be checked: " + ModLog.Describe(e));
            }
        }

        private static List<IntPtr> Buttons(GameObject root)
        {
            var found = new List<IntPtr>();
            Component[] parts = root.GetComponentsInChildren<Component>(true);
            if (parts == null) return found;

            foreach (Component part in parts)
            {
                if (part == null) continue;
                if (ClassName(part.Pointer) == "UIButton") found.Add(part.Pointer);
            }

            return found;
        }

        private static IntPtr Component(IntPtr owner, string className)
        {
            try
            {
                GameObject go = new Component(owner).gameObject;
                return go == null ? IntPtr.Zero : Find(go, className);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        private static bool Media(List<IntPtr> haystack, IntPtr needle)        {
            for (int i = 0; i < haystack.Count; i++)
                if (haystack[i] == needle) return true;
            return false;
        }

        private static IntPtr NewArray(IntPtr array, int length)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(array);
            IntPtr created = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(klass, (uint)length);
            if (created == IntPtr.Zero) return array;

            for (int i = 0; i < length; i++)
                Memory.WritePtr(created + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size,
                                Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size));
            return created;
        }

        private static void Refresh()
        {
            if (!Present || !Memory.LooksLikeObject(_clone)) return;

            try
            {
                GameObject clone = new GameObject(_clone);
                int queued = 0;
                foreach (IntPtr c2d in C2Ds(clone))
                {
                    _reset(c2d, IntPtr.Zero);
                    queued++;
                }

                Recompute();
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the row could not be registered again: " +
                                   ModLog.Describe(e));
            }
        }

        private static int Dress(GameObject clone)
        {
            Material spare = null;

            foreach (IntPtr c2d in C2Ds(clone))
            {
                if (MaterialOf(c2d) == null) continue;
                spare = MaterialOf(c2d);
                break;
            }

            if (spare == null) return 0;
            int dressed = 0;
            foreach (IntPtr c2d in C2Ds(clone))
            {
                if (MaterialOf(c2d) != null) continue;

                try
                {
                    IntPtr renderer = Memory.Ptr(c2d + 0x160);
                    if (!Memory.LooksLikeObject(renderer) || ClassName(renderer) != "MeshRenderer") continue;
                    new MeshRenderer(renderer).sharedMaterial = spare;
                    dressed++;
                }
                catch (Exception)
                {
                }
            }

            return dressed;
        }

        private static Material MaterialOf(IntPtr c2d)
        {
            try
            {
                IntPtr renderer = Memory.Ptr(c2d + 0x160);
                if (!Memory.LooksLikeObject(renderer) || ClassName(renderer) != "MeshRenderer") return null;
                return new MeshRenderer(renderer).sharedMaterial;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool Choose(IntPtr row, int index)
        {
            if (row == IntPtr.Zero || row != _row) return false;
            if (Memory.I32(_row + _barIndex) == index && AutoSwitch.On == (index != 0)) return false;
            AutoSwitch.On = index != 0;
            Apply();
            _repaint = 2;
            return true;
        }

        private static int IndexOf(IntPtr button)
        {
            if (!Present || !Memory.LooksLikeObject(button)) return -1;
            IntPtr buttons = Memory.Ptr(_row + _barButtons);
            int length = ArrayLength(buttons);

            for (int i = 0; i < length; i++)
                if (Memory.Ptr(buttons + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size) == button) return i;
            return -1;
        }

        private static void Flip(int index)
        {
            if (_state == null) return;
            IntPtr buttons = Memory.Ptr(_row + _barButtons);
            int length = ArrayLength(buttons);

            for (int i = 0; i < length; i++)
            {
                IntPtr button = Memory.Ptr(buttons + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(button)) continue;
                int want = i == index ? 2 : 1;
                _state(button, want == 1 ? 2 : 1, IntPtr.Zero);
            }
        }

        private static void Stretch()
        {
            if (Memory.LooksLikeObject(_scroll)) Memory.WriteF32(_scroll + ScrollBound, _bound);
        }

        private static int Sync(Transform from, Transform to)
        {
            if (from == null || to == null) return 0;
            int copied = 0;
            IntPtr theirs = Find(from.gameObject, "Constrained2D");
            IntPtr ours = Find(to.gameObject, "Constrained2D");
            if (theirs != IntPtr.Zero && ours != IntPtr.Zero && Copy(theirs, ours)) copied++;
            int pairs = Math.Min(from.childCount, to.childCount);
            for (int i = 0; i < pairs; i++) copied += Sync(from.GetChild(i), to.GetChild(i));
            return copied;
        }

        private static bool Copy(IntPtr from, IntPtr to)
        {
            var taken = new System.Text.StringBuilder();

            try
            {
                IntPtr source = Memory.Ptr(from + 0x160);
                IntPtr target = Memory.Ptr(to + 0x160);

                if (Memory.LooksLikeObject(source) && Memory.LooksLikeObject(target) &&
                    ClassName(source) == "MeshRenderer" && ClassName(target) == "MeshRenderer")
                {
                    var theirs = new MeshRenderer(source);
                    var ours = new MeshRenderer(target);
                    Material material = theirs.sharedMaterial;
                    if (material != null && ours.sharedMaterial != material)
                    {
                        ours.sharedMaterial = material;
                        taken.Append($" material('{material.name}')");
                    }

                    if (ours.enabled != theirs.enabled)
                    {
                        ours.enabled = theirs.enabled;
                        taken.Append($" enabled({theirs.enabled})");
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                IntPtr source = Memory.Ptr(from + 0x168);
                IntPtr target = Memory.Ptr(to + 0x168);

                if (Memory.LooksLikeObject(source) && Memory.LooksLikeObject(target) &&
                    ClassName(source) == "MeshFilter" && ClassName(target) == "MeshFilter")
                {
                    var theirs = new MeshFilter(source);
                    var ours = new MeshFilter(target);
                    Mesh mesh = theirs.sharedMesh;
                    if (mesh != null && ours.sharedMesh != mesh)
                    {
                        ours.sharedMesh = mesh;
                        taken.Append($" mesh('{mesh.name}' {mesh.vertexCount}v)");
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                IntPtr theirs = Memory.Ptr(from + 0x170);
                IntPtr ours = Memory.Ptr(to + 0x170);

                if (Memory.LooksLikeObject(theirs) && Memory.LooksLikeObject(ours))
                {
                    Buffer.MemoryCopy((void*)(theirs + 0x38), (void*)(ours + 0x38), 0x50, 0x50);
                    Memory.WriteF32(ours + 0x88, Memory.F32(theirs + 0x88));
                    Memory.WritePtr(ours + 0xE0, Memory.Ptr(theirs + 0xE0));
                    if (!Memory.LooksLikeObject(Memory.Ptr(ours + 0xE8)))
                        Memory.WritePtr(ours + 0xE8, Memory.Ptr(theirs + 0xE8));
                    bool renderer = Memory.LooksLikeObject(Memory.Ptr(ours + 0x108));
                    bool filter = Memory.LooksLikeObject(Memory.Ptr(ours + 0x110));
                    if (!renderer) Memory.WritePtr(ours + 0x108, Component(to, "MeshRenderer"));
                    if (!filter) Memory.WritePtr(ours + 0x110, Component(to, "MeshFilter"));
                    string share = "no mesh";
                    IntPtr fromFilter = Memory.Ptr(theirs + 0x110);
                    IntPtr toFilter = Memory.Ptr(ours + 0x110);

                    if (!IsTitle(ours) && Memory.LooksLikeObject(fromFilter) &&
                        Memory.LooksLikeObject(toFilter))
                    {
                        Mesh mesh = new MeshFilter(fromFilter).sharedMesh;
                        if (mesh != null)
                        {
                            new MeshFilter(toFilter).sharedMesh = mesh;
                            share = $"{mesh.vertexCount}v";
                        }
                    }

                    taken.Append($" text({Memory.F32(theirs + 0x88):F1}, {share}" +
                                 $"{(renderer ? "" : ", renderer")}{(filter ? "" : ", filter")})");
                }
            }
            catch (Exception)
            {
            }

            if (taken.Length == 0) return false;

            return true;
        }

        private static bool IsTitle(IntPtr text)
        {
            if (!Present || !Memory.LooksLikeObject(text)) return false;
            IntPtr title = Memory.Ptr(_row + _barTitle);
            if (!Memory.LooksLikeObject(title)) return false;
            return Memory.Ptr(title + Offsets.Runtime.ArrayDataOffset) == text;
        }

        private static IntPtr RowSelf() => Present ? Memory.Ptr(_row + _barSelf) : IntPtr.Zero;

        private static int BehaviourEnabled(IntPtr component)
        {
            IntPtr get = _enabled;
            if (get == IntPtr.Zero) return -1;

            try
            {
                return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)get)(component, IntPtr.Zero);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal static bool Ours(IntPtr row) => Present && row == _row;

        internal static void Point()
        {
            if (!Present || !Memory.LooksLikeObject(_panel)) return;

            try
            {
                Memory.WritePtr(_panel + _panelTooltip, RowSelf());
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the tooltip could not be pointed at the row: " +
                                   ModLog.Describe(e));
            }
        }





        private static List<IntPtr> C2Ds(GameObject root)
        {
            var found = new List<IntPtr>();
            Component[] parts = root.GetComponentsInChildren<Component>(true);
            if (parts == null) return found;

            foreach (Component part in parts)
            {
                if (part == null) continue;
                if (ClassName(part.Pointer) == "Constrained2D") found.Add(part.Pointer);
            }

            return found;
        }

        private static IntPtr Find(GameObject go, string className)
        {
            if (go == null) return IntPtr.Zero;
            Component[] parts = go.GetComponents<Component>();
            if (parts == null) return IntPtr.Zero;

            foreach (Component part in parts)
            {
                if (part == null) continue;
                if (ClassName(part.Pointer) == className) return part.Pointer;
            }

            return IntPtr.Zero;
        }

        private static string ClassName(IntPtr component)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(component);
                return klass == IntPtr.Zero ? null : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Fail(string why)
        {
            _failed = true;
            ModLog.Error("AUTO row: " + why + "; the row will not be added");
            return false;
        }

        private static bool Resolve()
        {
            if (_resolved) return _reset != null && _recompute != null;
            _resolved = true;
            _panelSource = Named(_panelSource, "UiSettingGameplayPanel", "damageNumbers");
            _panelAnchor = Named(_panelAnchor, "UiSettingGameplayPanel", "bossVfx");
            _panelScroll = Named(_panelScroll, "UiSettingGameplayPanel", "optionsScrollView");
            _panelTooltip = Named(_panelTooltip, "UiSettingGameplayPanel", "_Zi");
            _barSelf = Named(_barSelf, "UiSettingsMultiselectorBar", "self");
            _barButtons = Named(_barButtons, "UiSettingsMultiselectorBar", "buttons");
            _barTitle = Named(_barTitle, "UiSettingsMultiselectorBar", "titleTexts");
            _barIndex = Named(_barIndex, "UiSettingsMultiselectorBar", "_Kj");
            _barValue = Named(_barValue, "UiSettingsMultiselectorBar", "_lj");
            _barSelected = Named(_barSelected, "UiSettingsMultiselectorBar", "selectedElements");
            _barTooltip = Named(_barTooltip, "UiSettingsMultiselectorBar", "tooltipHoverText");
            _c2dId = Named(_c2dId, "Constrained2D", "Id");
            int block = Named(0xC8, "Constrained2D", "constrained2DTransform");
            _c2dSize = block + Named(0x0, "Constrained2DTransform", "sizeInPixels");
            int offset = Named(0x18, "Constrained2DTransform", "offset");
            _c2dOffsetX = block + offset;
            _c2dOffsetY = block + offset + sizeof(float);
            _c2dType = block + Named(0x24, "Constrained2DTransform", "type");
            _reset = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)Pointer("Constrained2D", "Reset", 0x502110);
            _wire = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)
                Pointer("UiSettingsMultiselectorBar", "_yG", 0x6D63B0);
            _select = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)
                Pointer("UiSettingsMultiselectorBar", "_xG", 0x6D6320);
            _state = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)
                Pointer("UIButton", "_dSA", 0x5082D0);
            _recompute = (delegate* unmanaged[Cdecl]<byte, IntPtr, void>)Pointer("_wi", "_ERA", 0x50F100);
            _admit = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort>)Pointer("_wi", "_bRA", 0x511690);
            _touch = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)Pointer("Constrained2D", "_qQA", 0x503950);
            _enabled = MethodResolver.ByName("Behaviour", "get_enabled");
            _setEnabled = MethodResolver.ByName("Behaviour", "set_enabled");
            IntPtr setText = CustomSong.Auto.AutoResults.StringSetter();
            if (setText != IntPtr.Zero)
                _setText = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, void>)setText;

            if (_reset == null || _recompute == null || _select == null)
            {
                Fail("the row's own methods could not be reached in this build");
                return false;
            }

            return true;
        }

        private static int Named(int fallback, string type, string field)
        {
            int found = FieldResolver.Lookup(type, field);
            return found >= 0 ? found : fallback;
        }

        private static IntPtr Pointer(string type, string method, int rva)
        {
            IntPtr target = MethodResolver.ByName(type, method);
            if (target != IntPtr.Zero) return target;
            return GameAssembly.Base == IntPtr.Zero ? IntPtr.Zero : GameAssembly.Base + rva;
        }
    }
}

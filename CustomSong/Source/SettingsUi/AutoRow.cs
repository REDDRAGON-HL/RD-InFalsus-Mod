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
        private static int _c2dText = 0x170;
        private static int _textFont = 0x20;
        private static int _textBuffer = 0x118;
        private static int _textSubmeshes = 0x130;
        private static int _textScale = 0x13C;
        private const string OffCaption = "OFF";
        private const string OnCaption = "ON";
        private const byte TextType = 4;
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
        private static delegate* unmanaged[Cdecl]<IntPtr, int, float, IntPtr, void> _showTooltip;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> _hideTooltip;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> _layoutTooltip;
        private static IntPtr _panel;
        private static IntPtr _pending;
        private static IntPtr _row;
        private static IntPtr _scroll;
        private static float _bound;
        private static IntPtr _clone;
        private static int _repaint;

        private static int _barButtons = 0x28;
        private static bool _resolved;
        private static bool _failed;

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

        private static int _attempts;

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
            if (Present && _repaint > 0 && --_repaint == 0) Apply();

            if (Present && _hoverNote != 0)
            {
                byte note = _hoverNote;
                _hoverNote = 0;
            }

            if (Present && _labelled && _recheck > 0 && --_recheck == 0)
            {
                _recheck = RecheckInterval;
                Verify();
            }

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

                ModLog.Load($"AUTO row: added below the boss-VFX row " +
                            $"(id={Memory.U16(RowSelf() + _c2dId)}, {Queued()} pending)");
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
            _labelled = false;
            _written.Clear();

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
                if (_hovered && Holds(Memory.Ptr(_row + _barSelected), off[i])) continue;

                try
                {
                    new GameObject(off[i]).SetActive(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool Holds(IntPtr c2d, IntPtr go)
        {
            if (!Memory.LooksLikeObject(c2d) || !Memory.LooksLikeObject(go)) return false;

            try
            {
                GameObject root = new Component(c2d).gameObject;
                if (root == null) return false;
                Transform wanted = new GameObject(go).transform;
                if (wanted == null) return false;

                for (Transform at = wanted; at != null; at = at.parent)
                    if (at.gameObject.Pointer == root.Pointer) return true;
            }
            catch (Exception)
            {
            }

            return false;
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
                    ModLog.Load($"AUTO row: shown {(AutoSwitch.On ? "on" : "off")}");
                }

                Label();
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

        private static int Count(Material[] array) => array == null ? 0 : array.Length;

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

            if (Memory.U8(to + _c2dType) == TextType)
            {
                try
                {
                    Buffer.MemoryCopy((void*)(from + _c2dSize), (void*)(to + _c2dSize), 0x10, 0x10);
                    taken.Append($" rect({Memory.F32(to + _c2dSize):F3}x" +
                                 $"{Memory.F32(to + _c2dSize + 4):F3}," +
                                 $"n{Memory.F32(to + _c2dSize + 8):F3}x" +
                                 $"{Memory.F32(to + _c2dSize + 12):F3})");
                }
                catch (Exception)
                {
                }
            }

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
                    Buffer.MemoryCopy((void*)(theirs + 0x90), (void*)(ours + 0x90), 0x4C, 0x4C);
                    IntPtr font = Memory.Ptr(theirs + _textFont);
                    if (Memory.LooksLikeObject(font)) Memory.WritePtr(ours + _textFont, font);
                    Memory.WriteF64(ours + _textScale, Memory.F64(theirs + _textScale));
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

                    if (Memory.LooksLikeObject(fromFilter) && Memory.LooksLikeObject(toFilter))
                    {
                        Mesh mesh = new MeshFilter(fromFilter).sharedMesh;
                        if (mesh != null)
                        {
                            new MeshFilter(toFilter).sharedMesh = mesh;
                            share = $"{mesh.vertexCount}v{(IsTitle(ours) ? "!" : "")}";
                        }
                    }

                    string materials = "";
                    try
                    {
                        IntPtr fromRenderer = Memory.Ptr(theirs + 0x108);
                        IntPtr toRenderer = Memory.Ptr(ours + 0x108);

                        if (Memory.LooksLikeObject(fromRenderer) && Memory.LooksLikeObject(toRenderer))
                        {
                            var target = new MeshRenderer(toRenderer);
                            if (Count(target.sharedMaterials) == 0)
                            {
                                Material[] array = new MeshRenderer(fromRenderer).sharedMaterials;
                                if (array != null && array.Length > 0)
                                {
                                    target.sharedMaterials = array;
                                    materials = $", {array.Length} material(s) shared";
                                }
                            }
                            else
                            {
                                materials = $", {Count(target.sharedMaterials)} own";
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }

                    taken.Append($" text({Memory.F32(theirs + 0x88):F1}, {share}{materials}" +
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

        private static string Describe(IntPtr statics, IntPtr c2d, string label)
        {
            if (!Memory.LooksLikeObject(c2d)) return label + "=<none>";
            int id = Memory.U16(c2d + _c2dId);
            bool main;
            int slot = Slot(statics, c2d, out main);
            string calc = "no slot";
            if (slot >= 0)
            {
                IntPtr at = Calc(statics, main, slot);
                calc = $"calc[pos {Memory.F32(at + 8):F3},{Memory.F32(at + 12):F3} " +
                       $"a {Memory.F32(at + 0x10):F3} z {Memory.F32(at + 0x20):F3} " +
                       $"mask {Memory.I32(at + 0x34)} d {Memory.I32(at + 0x38)}]";
            }

            return $"{label} '{Name(c2d)}' type={Memory.U8(c2d + _c2dType)} " +
                   $"active={Active(c2d)} id={id} slot={slot} " +
                   $"offset={Memory.F32(c2d + _c2dOffsetX):F3},{Memory.F32(c2d + _c2dOffsetY):F3} " +
                   $"size={Memory.F32(c2d + _c2dSize):F3},{Memory.F32(c2d + _c2dSize + 4):F3} " +
                   $"norm={Memory.F32(c2d + _c2dSize + 8):F3},{Memory.F32(c2d + _c2dSize + 12):F3} {calc}";
        }

        private static void Label()
        {
            if (!Present || _labelled) return;

            if (_setTextParameters == IntPtr.Zero)
            {
                ModLog.WarnRelease("AUTO row: the text parameters setter was not resolved; " +
                                   "the row has no words");
                return;
            }

            _labelled = true;
            int written = 0;
            written += WriteTitle();
            written += WriteCaptions();
            ModLog.Load($"AUTO row: {written} word(s) written");
        }

        private static bool _labelled;

        private static int WriteTitle()
        {
            int written = 0;
            IntPtr array = Memory.Ptr(_row + _barTitle);
            if (Memory.LooksLikeObject(array))
            {
                int length = ArrayLength(array);
                if (length > 0 && length <= 32)
                {
                    for (int i = 0; i < length; i++)
                        written += Write(Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size),
                                         Title);
                }
            }

            foreach (IntPtr text in Texts())
            {
                if (Name(text).IndexOf("TitleText", StringComparison.Ordinal) < 0) continue;
                written += Write(text, Title);
            }

            return written;
        }

        private static unsafe int Write(IntPtr text, string words)
        {
            if (!Memory.LooksLikeObject(text)) return 0;
            if (!_written.Add(text)) return 0;
            IntPtr created = NewString(words);
            if (created == IntPtr.Zero) return 0;

            try
            {
                byte* image = stackalloc byte[TextParametersSize];
                Buffer.MemoryCopy((void*)(text + 0x38), image, TextParametersSize, TextParametersSize);
                *(int*)(image + TextParamKey) = 0;
                *(int*)(image + TextParamLayout) = TextLayoutDirect;
                *(int*)(image + TextParam0Key) = 0;
                *(int*)(image + TextParam1Key) = 0;
                for (int i = 0; i < TextParam1Key - TextParamRuntimeString; i += IntPtr.Size)
                    *(IntPtr*)(image + TextParamRuntimeString + i) = IntPtr.Zero;
                *(IntPtr*)(image + TextParam0String) = created;
                *(int*)(image + TextParam0Length) = words.Length;
                _expected[text] = words;
                _lastWritten[text] = created;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)image;
                Invoke(_setTextParameters, text, args, "text");

                if (Memory.Ptr(text + 0x38 + TextParam0String) != created)
                    Buffer.MemoryCopy(image, (void*)(text + 0x38), TextParametersSize, TextParametersSize);
                Memory.WriteI32(text + 0xF0, Memory.I32(text + 0xF0) | TextDirtyMesh);
                Memory.WritePtr(text + _textBuffer, IntPtr.Zero);
                if (_processDirty != IntPtr.Zero) Invoke(_processDirty, text, null, "dirty pass");
                GameObject host = new Component(text).gameObject;
                if (host != null && host.activeSelf)
                {
                    host.SetActive(false);
                    host.SetActive(true);
                }

                return 1;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: a word could not be written: " + ModLog.Describe(e));
                return 0;
            }
        }

        private const int TextParametersSize = 0x50;
        private const int TextParamKey = 0x00;
        private const int TextParamLayout = 0x04;
        private const int TextParam0Length = 0x08;
        private const int TextParam0String = 0x10;
        private const int TextParam0Key = 0x18;
        private const int TextParamRuntimeString = 0x20;
        private const int TextParam1Key = 0x48;
        private const int TextLayoutDirect = 4;
        private const int TextDirtyMesh = 8 | 16 | 1;
        private static IntPtr _setTextParameters;
        private static IntPtr _processDirty;
        private static readonly HashSet<IntPtr> _written = new HashSet<IntPtr>();
        private const int RecheckInterval = 60;
        private static int _recheck = RecheckInterval;
        private static readonly Dictionary<IntPtr, string> _expected = new Dictionary<IntPtr, string>();

        private static void Verify()
        {
            if (!Present) return;

            try
            {
                int rewrote = 0;

                foreach (IntPtr text in Texts())
                {
                    if (!_expected.TryGetValue(text, out string wanted)) continue;
                    if (!Memory.LooksLikeObject(text)) continue;
                    IntPtr held = Memory.Ptr(text + 0x38 + TextParam0String);
                    if (held == _lastWritten[text] && Memory.I32(text + 0x38 + TextParamLayout) == TextLayoutDirect)
                        continue;
                    _written.Remove(text);
                    rewrote += Write(text, wanted);
                }
            }
            catch (Exception e)
            {
                ModLog.Warn("AUTO row: the words could not be checked: " + ModLog.Describe(e));
            }
        }

        private static readonly Dictionary<IntPtr, IntPtr> _lastWritten = new Dictionary<IntPtr, IntPtr>();

        private static unsafe void Invoke(IntPtr method, IntPtr instance, IntPtr* args, string what)
        {
            if (method == IntPtr.Zero) return;
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return;
            ModLog.WarnRelease($"AUTO row: the {what} call raised " + Name(raised));
        }

        private static unsafe IntPtr NewString(string words)
        {
            if (words == null) words = string.Empty;

            try
            {
                fixed (char* characters = words)
                {
                    IntPtr created = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_new_utf16(
                        characters, words.Length);
                    if (created != IntPtr.Zero) return created;
                }
            }
            catch (Exception)
            {
            }

            return Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(words);
        }

        private static int WriteCaptions()
        {
            IntPtr buttons = Memory.Ptr(_row + _barButtons);
            int length = ArrayLength(buttons);
            if (length <= 0) return 0;
            int written = 0;
            for (int i = 0; i < length; i++)
            {
                IntPtr button = Memory.Ptr(buttons + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(button)) continue;
                GameObject go = new Component(button).gameObject;
                if (go == null) continue;

                foreach (IntPtr text in Texts(go))
                    written += Write(text, i == 0 ? OffCaption : OnCaption);
            }

            return written;
        }

        private static List<IntPtr> Texts()
        {
            var found = new List<IntPtr>();
            if (!Memory.LooksLikeObject(_clone)) return found;
            return Texts(new GameObject(_clone));
        }

        private static List<IntPtr> Texts(GameObject root)
        {
            var found = new List<IntPtr>();
            if (root == null) return found;

            foreach (IntPtr c2d in C2Ds(root))
            {
                if (Memory.U8(c2d + _c2dType) != TextType) continue;
                IntPtr text = Memory.Ptr(c2d + _c2dText);
                if (Memory.LooksLikeObject(text)) found.Add(text);
            }

            return found;
        }

        private static IntPtr Calc(IntPtr statics, bool main, int slot) =>
            Memory.Ptr(statics + (main ? BatchMainCalc : BatchOtherCalc)) +
            Offsets.Runtime.ArrayDataOffset + slot * CalcSize;

        private static int Slot(IntPtr statics, IntPtr c2d, out bool main)
        {
            main = true;
            int index = IndexOf(Memory.Ptr(statics + BatchMain), c2d);
            if (index >= 0) return index;
            main = false;
            return IndexOf(Memory.Ptr(statics + BatchOther), c2d);
        }

        private static int IndexOf(IntPtr array, IntPtr c2d)
        {
            int length = ArrayLength(array);
            for (int i = 0; i < length; i++)
                if (Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size) == c2d) return i;
            return -1;
        }

        private static string Bits(IntPtr statics, int id)
        {
            if (id < 0 || id == 0xFFFF) return "bits-";
            int at = Offsets.Runtime.ArrayDataOffset + id;
            int word = Offsets.Runtime.ArrayDataOffset + id * sizeof(ushort);

            return $"Q{Byte(statics, 0xB0, at)} r{Byte(statics, 0xB8, at)} " +
                   $"R{Byte(statics, 0xC0, at)} s{Byte(statics, 0xC8, at)} " +
                   $"S{Word(statics, 0xD0, word)} t{Word(statics, 0xD8, word)}";
        }

        private static byte Byte(IntPtr statics, int array, int at)
        {
            IntPtr data = Memory.Ptr(statics + array);
            return Memory.LooksLikeObject(data) ? Memory.U8(data + at) : (byte)0;
        }

        private static ushort Word(IntPtr statics, int array, int at)
        {
            IntPtr data = Memory.Ptr(statics + array);
            return Memory.LooksLikeObject(data) ? Memory.U16(data + at) : (ushort)0;
        }

        private static IntPtr Part(GameObject root, string name)
        {
            foreach (IntPtr c2d in C2Ds(root))
                if (Name(c2d) == name) return c2d;
            return IntPtr.Zero;
        }

        private static string Id(IntPtr c2d)
        {
            if (!Memory.LooksLikeObject(c2d)) return "none";
            int id = Memory.U16(c2d + _c2dId);
            string state = "?";

            try
            {
                Component component = new Component(c2d);
                GameObject go = component.gameObject;
                state = $"{(go != null && go.activeSelf ? "on" : "off")}" +
                        $"/{(BehaviourEnabled(c2d) == 1 ? "en" : BehaviourEnabled(c2d) == 0 ? "dis" : "?")}";
            }
            catch (Exception)
            {
            }

            return $"{(id == 0xFFFF ? "no id" : id.ToString())} {state}";
        }

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

        internal static void Describe()
        {
            if (!Present || !Memory.LooksLikeObject(_panel)) return;
            IntPtr box = Tooltip();
            if (box == IntPtr.Zero) return;

            try
            {
                IntPtr text = Memory.Ptr(box + 0x28);
                if (Memory.LooksLikeObject(text) && _setText != null)
                {
                    _setText(text, Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(TooltipText), 0,
                             IntPtr.Zero);
                    if (_layoutTooltip != null) _layoutTooltip(box, IntPtr.Zero);
                }

                if (_showTooltip != null) _showTooltip(box, 1, 0f, IntPtr.Zero);
                Memory.WritePtr(_panel + _panelTooltip, RowSelf());
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the description could not be shown: " +
                                   ModLog.Describe(e));
            }
        }

        internal static void Conceal()
        {
            if (!Present || !Memory.LooksLikeObject(_panel)) return;

            try
            {
                if (Memory.Ptr(_panel + _panelTooltip) != RowSelf()) return;
                IntPtr box = Tooltip();
                if (box != IntPtr.Zero && _hideTooltip != null) _hideTooltip(box, IntPtr.Zero);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the description could not be put away: " +
                                   ModLog.Describe(e));
            }
        }

        private const string TooltipText = "Automatically plays the chart's notes";

        private static IntPtr Tooltip()
        {
            try
            {
                IntPtr scene = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(
                    "Game.dll", "ifapp.Game.Scenes", "CoreScene");
                if (scene == IntPtr.Zero) return IntPtr.Zero;
                IntPtr statics = FieldResolver.Statics(scene);
                if (statics == IntPtr.Zero) return IntPtr.Zero;
                IntPtr instance = Memory.Ptr(statics);
                if (!Memory.LooksLikeObject(instance)) return IntPtr.Zero;
                IntPtr box = Memory.Ptr(instance + 0x48);
                return Memory.LooksLikeObject(box) ? box : IntPtr.Zero;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        internal static string HoverState()
        {
            if (!Present) return "no row";

            return $"selected={Live(_barSelected)} " +
                   $"tooltip={Live(_barTooltip)} value={Memory.I32(_row + _barValue)}";
        }

        internal static void Note(byte entered)
        {
            _hoverNote = (byte)(entered != 0 ? 1 : 2);
            _hovered = entered != 0;
        }

        private static byte _hoverNote;
        private static bool _hovered;

        private static string Live(int field)
        {
            IntPtr at = Memory.Ptr(_row + field);
            return !Memory.LooksLikeObject(at) ? "-" : $"0x{at.ToInt64():X}";
        }

        internal static string Named(IntPtr component) => Name(component);

        private static string Name(IntPtr c2d)
        {
            try
            {
                GameObject go = new Component(c2d).gameObject;
                return go == null ? "<null>" : go.name;
            }
            catch (Exception)
            {
                return "<?>";
            }
        }

        private static bool Active(IntPtr c2d)
        {
            try
            {
                GameObject go = new Component(c2d).gameObject;
                return go != null && go.activeInHierarchy;
            }
            catch (Exception)
            {
                return false;
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
            _textFont = Named(_textFont, "FastText", "fontFamilyAsset");
            _textBuffer = Named(_textBuffer, "FastText", "charBuffer");
            _textSubmeshes = Named(_textSubmeshes, "FastText", "lastSubmeshCount");
            _textScale = Named(_textScale, "FastText", "calculatedC2DScale");
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
            _enabled = NativeEntry("Behaviour", "get_enabled", 0);
            _setEnabled = NativeEntry("Behaviour", "set_enabled", 1);

            _showTooltip = (delegate* unmanaged[Cdecl]<IntPtr, int, float, IntPtr, void>)
                Pointer("UiTextToolTip", "_mH", 0x6D8940);
            _hideTooltip = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)
                Pointer("UiTextToolTip", "_MH", 0x6D8740);
            _layoutTooltip = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)
                Pointer("UiTextToolTip", "_LH", 0x6D3CB0);
            IntPtr setText = CustomSong.Auto.AutoResults.StringSetter();
            if (setText != IntPtr.Zero)
                _setText = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, void>)setText;

            IntPtr fastText = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("FastText.dll", "FastText",
                                                                         "FastText");
            if (fastText != IntPtr.Zero)
            {
                _setTextParameters = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    fastText, "set_TextParameters", 1);
                _processDirty = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    fastText, "EnsureDirtyFlagsProcessed", 0);
            }

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

        private static IntPtr NativeEntry(string type, string method, int parameters)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll",
                                                                     "UnityEngine", type);
            if (klass == IntPtr.Zero)
            {
                ModLog.WarnRelease($"AUTO row: UnityEngine.{type} is not in this build, so its " +
                                   $"{method} cannot be used");
                return IntPtr.Zero;
            }

            IntPtr entry = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                klass, method, parameters);

            if (entry == IntPtr.Zero)
            {
                ModLog.WarnRelease($"AUTO row: UnityEngine.{type}.{method} is not in this build");
                return IntPtr.Zero;
            }

            return *(IntPtr*)entry;
        }
    }
}

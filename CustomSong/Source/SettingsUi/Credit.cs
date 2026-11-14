extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static unsafe class Credit
    {
        private const int LayerIllustrator = 0x2B0;
        private const int C2dAnchor = 0xC8 + 0x10;
        private const int C2dOffset = 0xC8 + 0x18;
        private const int C2dType = 0xC8 + 0x24;
        private const int C2dRenderableScale = 0xC8 + 0x28;
        private const int C2dId = 0x178;
        private const int TextAlignment = 0x34;
        private const int SongCharts = 0x18;
        private const int ChartStride = 0x30;
        private const int ChartDesigner = 0x10;
        private const string CloneName = "ChartDesignerContainer";
        private const string Caption = "谱面设计";
        private const string OriginalCaption = "插画师";
        private const string None = "-";
        private const float MirrorShift = 15f;
        private static IntPtr _clone;
        private static IntPtr[] _texts;
        private static IntPtr _textsOwner;
        private static string _written;
        private static bool _mirrored;
        private static bool _underRow;
        private static float _rowOffsetX;
        private static float _rowOffsetY;

        internal static bool Install()
        {
            if (LevelLabelHooks.WatchesTransition)
            {
                return true;
            }

            ModLog.WarnRelease("chart credit: the transition screen could not be hooked; the " +
                               "designer's name has nowhere to go");
            return false;
        }

        internal static void FromTransition(IntPtr layer, IntPtr songInfo, byte difficulty)
        {
            try
            {
                if (!Memory.LooksLikeObject(layer) || !Memory.LooksLikeObject(songInfo)) return;
                if (Memory.ClassName(layer) != "SongTransitionLayer") return;
                IntPtr block = Memory.Ptr(layer + LayerIllustrator);
                if (!Memory.LooksLikeObject(block)) return;
                int index = SongCatalog.DifficultyIndexOf(difficulty);
                if (index < 0) return;
                ModLog.Load("chart credit: transition: relabel");
                Relabel(block);
                ModLog.Load("chart credit: transition: clone");
                IntPtr copy = Clone(block);
                if (copy == IntPtr.Zero) return;

                if (_texts == null || _texts.Length < 2)
                {
                    var found = new List<IntPtr>();
                    Collect(new GameObject(copy).transform, found);
                    _texts = found.ToArray();
                    _textsOwner = copy;
                    ModLog.Load($"chart credit: the copy has {_texts.Length} text node(s)");
                    if (_texts.Length < 2) return;
                }

                string designer = Field(songInfo, index, ChartDesigner);
                Write(string.IsNullOrEmpty(designer) ? None : designer);
            }
            catch (Exception e)
            {
                Guard("the credit row", e);
            }
        }

        private static IntPtr Clone(IntPtr block)
        {
            if (_clone != IntPtr.Zero)
            {
                if (Alive(_clone)) return _clone;
                _clone = IntPtr.Zero;
                _texts = null;
                _mirrored = false;
            }

            var container = new Component(block);
            if (container == null) return IntPtr.Zero;
            GameObject row = container.gameObject;
            if (row == null) return IntPtr.Zero;
            bool under = row.activeInHierarchy;
            Transform parent = under ? row.transform : row.transform.parent;
            if (parent == null) return IntPtr.Zero;
            Transform existing = parent.Find(CloneName);
            GameObject copy = existing != null ? existing.gameObject : null;

            if (copy == null)
            {
                UnityEngineCore::UnityEngine.Object spawned =
                    UnityEngineCore::UnityEngine.Object.Instantiate(row, parent);
                if (spawned == null) return IntPtr.Zero;
                copy = new GameObject(spawned.Pointer);
                copy.name = CloneName;
                _underRow = under;
                _mirrored = false;

                ModLog.Load($"chart credit: the row was built {(_underRow ? "under" : "beside")} " +
                            "the game's own");
            }

            _clone = copy.Pointer;
            _written = null;
            CaptureRowOffset(row.transform);
            FreshSlots(copy.transform);

            if (!_mirrored)
            {
                Mirror(copy.transform);
                _mirrored = true;
            }

            if (!copy.activeSelf) copy.SetActive(true);
            Place(copy.transform);
            return _clone;
        }

        private static void CaptureRowOffset(Transform row)
        {
            IntPtr rowNode = Constrained(row);
            if (rowNode == IntPtr.Zero) return;
            _rowOffsetX = Memory.F32(rowNode + C2dOffset);
            _rowOffsetY = Memory.F32(rowNode + C2dOffset + 4);
        }

        private static void Place(Transform copy)
        {
            try
            {
                IntPtr node = Constrained(copy);
                if (node == IntPtr.Zero) return;
                float x = (_underRow ? -2f * _rowOffsetX : -_rowOffsetX) + MirrorShift;
                float y = _underRow ? 0f : _rowOffsetY;
                Memory.WriteF32(node + C2dOffset, x);
                Memory.WriteF32(node + C2dOffset + 4, y);
                WidgetText.Relayout(node);
            }
            catch (Exception e)
            {
                Guard("the credit row's place", e);
            }
        }

        private static void Mirror(Transform node)
        {
            if (node == null) return;

            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child == null) continue;

                try
                {
                    IntPtr c2d = Constrained(child);
                    if (c2d != IntPtr.Zero)
                    {
                        int type = Memory.I32(c2d + C2dType);

                        if (type == 1 || type == 3)
                        {
                            float scale = Memory.F32(c2d + C2dRenderableScale);
                            if (scale != 0f) Memory.WriteF32(c2d + C2dRenderableScale, -scale);
                        }
                        else
                        {
                            Memory.WriteF32(c2d + C2dAnchor, 1f - Memory.F32(c2d + C2dAnchor));
                        }

                        Memory.WriteF32(c2d + C2dOffset, -Memory.F32(c2d + C2dOffset));
                        WidgetText.Relayout(c2d);
                    }

                    IntPtr text = Component(child.gameObject.Pointer, "FastText");
                    if (text != IntPtr.Zero) FlipAlignment(text);
                }
                catch (Exception e)
                {
                    Guard("mirroring the credit row", e);
                }

                Mirror(child);
            }
        }

        private static void FlipAlignment(IntPtr text)
        {
            int alignment = Memory.U8(text + TextAlignment);
            int mirrored = alignment switch
            {
                0 => 2,
                2 => 0,
                3 => 5,
                5 => 3,
                6 => 8,
                8 => 6,
                _ => alignment,
            };
            if (mirrored != alignment) Memory.WriteU8(text + TextAlignment, (byte)mirrored);
        }

        private static void FreshSlots(Transform node)
        {
            if (node == null) return;
            IntPtr c2d = Constrained(node);
            if (c2d != IntPtr.Zero)
            {
                Memory.WriteI16(c2d + C2dId, -1);
                WidgetText.Relayout(c2d);
            }

            for (int i = 0; i < node.childCount; i++) FreshSlots(node.GetChild(i));
        }

        private static void Relabel(IntPtr block)
        {
            var container = new Component(block);
            if (container == null) return;
            Transform row = container.gameObject.transform;
            if (row == null) return;
            IntPtr text = FirstText(row);
            if (text == IntPtr.Zero) return;
            if (WidgetText.Shows(text, OriginalCaption)) return;
            LevelLabel.ReleaseFont(text);
            SetText(text, OriginalCaption);
        }

        private static void Write(string designer)
        {
            if (_texts == null || _texts.Length < 2) return;
            if (_written == designer) return;
            LevelLabel.ReleaseFont(_texts[0]);
            LevelLabel.ReleaseFont(_texts[1]);
            bool caption = SetText(_texts[0], Caption);
            bool name = SetText(_texts[1], designer);
            _written = designer;
            ModLog.Load($"chart credit: '{Caption}' / '{designer}' " +
                        $"({(caption && name ? "ok" : "written, one was rebuilt")})");
        }

        private static bool SetText(IntPtr text, string words)
        {
            if (!Memory.LooksLikeObject(text) || words == null) return false;
            IntPtr created = WidgetText.NewString(words);
            if (created == IntPtr.Zero) return false;
            WidgetText.WriteRaw(text, words);
            Toggle(text);
            return WidgetText.Holds(text, created);
        }

        private static void Toggle(IntPtr text)
        {
            try
            {
                GameObject host = new Component(text).gameObject;
                if (host == null || !host.activeSelf) return;
                host.SetActive(false);
                host.SetActive(true);
            }
            catch (Exception)
            {
            }
        }

        private static string Field(IntPtr songInfo, int index, int offset)
        {
            IntPtr charts = Memory.Ptr(songInfo + SongCharts);
            if (!Memory.LooksLikeObject(charts)) return null;
            int count = Memory.I32(charts + Offsets.Runtime.ArrayLength);
            if (index < 0 || index >= count || count > 8) return null;
            int stride = Offsets.Chart.Size > 0 ? Offsets.Chart.Size : ChartStride;
            IntPtr element = charts + Offsets.Runtime.ArrayDataOffset + index * stride;
            return Memory.Text(Memory.Ptr(element + offset), 128);
        }

        private static IntPtr Constrained(Transform node) =>
            node == null ? IntPtr.Zero : Component(node.gameObject.Pointer, "Constrained2D");

        private static IntPtr Component(IntPtr gameObject, string className)
        {
            if (!Memory.LooksLikeObject(gameObject)) return IntPtr.Zero;

            try
            {
                var go = new GameObject(gameObject);
                Component[] parts = go.GetComponents<Component>();
                if (parts == null) return IntPtr.Zero;

                foreach (Component part in parts)
                {
                    if (part == null) continue;
                    IntPtr pointer = part.Pointer;
                    if (pointer == IntPtr.Zero) continue;
                    if (Memory.ClassName(pointer) == className) return pointer;
                }
            }
            catch (Exception)
            {
            }

            return IntPtr.Zero;
        }

        private static IntPtr FirstText(Transform block)
        {
            var found = new List<IntPtr>();
            Collect(block, found);
            return found.Count == 0 ? IntPtr.Zero : found[0];
        }

        private static void Collect(Transform node, List<IntPtr> found)
        {
            if (node == null) return;
            IntPtr text = Component(node.gameObject.Pointer, "FastText");
            if (text != IntPtr.Zero) found.Add(text);
            int children = node.childCount;
            for (int i = 0; i < children; i++) Collect(node.GetChild(i), found);
        }

        private static bool Alive(IntPtr gameObject)
        {
            try
            {
                return new GameObject(gameObject) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int _errors;

        private static void Guard(string what, Exception e)
        {
            if (_errors++ > 4) return;
            ModLog.WarnRelease($"chart credit: {what} threw {ModLog.Describe(e)}");
        }
    }
}

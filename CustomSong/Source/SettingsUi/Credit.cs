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

        private static readonly string[] Captions =
        {
            "谱面设计",
            "譜面設計",
            "Chart Design",
            "譜面デザイン",
            "채보 디자인",
        };

        private static readonly string[] IllustratorCaptions =
        {
            "插画师",
            "插畫師",
            "Illustrator",
            "イラスト",
            "일러스트",
        };
        private const string None = "-";
        private static string Caption() => Captions[PackText.Language()];
        private static string IllustratorCaption() => IllustratorCaptions[PackText.Language()];
        private const float MirrorShift = 15f;
        private static IntPtr _clone;
        private static IntPtr[] _texts;
        private static IntPtr _textsOwner;
        private static string _written;
        private static bool _mirrored;
        private static float _rowOffsetX;
        private static float _rowOffsetY;

        internal static bool Install()
        {
            if (LevelLabelHooks.WatchesTransition)
            {
                return true;
            }

            ModLog.Warn("chart credit: the transition screen could not be hooked; the " +
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
                Relabel(block);
                IntPtr copy = Clone(block);
                if (copy == IntPtr.Zero) return;

                if (_texts == null || _texts.Length < 2)
                {
                    var found = new List<IntPtr>();
                    Collect(new GameObject(copy).transform, found);
                    _texts = found.ToArray();
                    _textsOwner = copy;
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
            var container = new Component(block);
            if (container == null) return IntPtr.Zero;
            GameObject row = container.gameObject;
            if (row == null) return IntPtr.Zero;
            Transform parent = row.transform;
            if (parent == null) return IntPtr.Zero;

            if (!row.activeInHierarchy)
            {
                ForgetWrongCopy(parent);
                return IntPtr.Zero;
            }

            if (_clone != IntPtr.Zero)
            {
                if (Alive(_clone) && ParentedUnder(_clone, parent)) return _clone;
                ForgetWrongCopy(parent);
            }

            UnityEngineCore::UnityEngine.Object spawned =
                UnityEngineCore::UnityEngine.Object.Instantiate(row, parent);
            if (spawned == null) return IntPtr.Zero;
            GameObject copy = new GameObject(spawned.Pointer);
            copy.name = CloneName;
            _clone = copy.Pointer;
            _written = null;
            _mirrored = false;
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

        private static bool ParentedUnder(IntPtr copy, Transform row)
        {
            try
            {
                var at = new GameObject(copy).transform;
                Transform parent = at != null ? at.parent : null;
                return parent != null && parent.Pointer == row.Pointer;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void ForgetWrongCopy(Transform row)
        {
            if (_clone != IntPtr.Zero)
            {
                try
                {
                    if (Alive(_clone) && !ParentedUnder(_clone, row))
                        UnityEngineCore::UnityEngine.Object.Destroy(new GameObject(_clone));
                }
                catch (Exception)
                {
                }
            }

            _clone = IntPtr.Zero;
            _texts = null;
            _written = null;
            _mirrored = false;
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
                float x = -2f * _rowOffsetX + MirrorShift;
                float y = 0f;
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
            string caption = IllustratorCaption();
            if (WidgetText.Shows(text, caption)) return;
            LevelLabel.ReleaseFont(text);
            SetText(text, caption);
        }

        private static void Write(string designer)
        {
            if (_texts == null || _texts.Length < 2) return;
            string caption = Caption();
            string wanted = caption + "\n" + designer;
            if (_written == wanted) return;
            LevelLabel.ReleaseFont(_texts[0]);
            LevelLabel.ReleaseFont(_texts[1]);
            bool drawn = SetText(_texts[0], caption);
            bool named = SetText(_texts[1], designer);
            _written = wanted;
        }

        internal static void Relanguage()
        {
            _written = null;
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
            ModLog.Warn($"chart credit: {what} threw {ModLog.Describe(e)}");
        }
    }
}

extern alias UnityEngineCore;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;

namespace CustomSong
{
    internal static unsafe class WidgetText
    {
        private const int TextParameters = 0x38;
        private const int TpSize = 0x50;
        private const int TpKey = 0x00;
        private const int TpLayout = 0x04;
        private const int TpParam0Length = 0x08;
        private const int TpParam0String = 0x10;
        private const int TpParam0Key = 0x18;
        private const int TpRuntimeString = 0x20;
        private const int TpParam1Key = 0x48;
        private const int LayoutDirect = 4;
        private const int CharBuffer = 0x118;
        private const int DirtyFlags = 0xF0;
        private const int DirtyMesh = 8 | 16 | 1;
        private static IntPtr _set;
        private static IntPtr _process;
        private static IntPtr _relayout;
        private static bool _resolved;

        internal static bool Resolve(IntPtr fastTextClass, IntPtr constrainedClass)
        {
            if (_resolved) return _set != IntPtr.Zero;

            if (fastTextClass != IntPtr.Zero)
            {
                _set = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    fastTextClass, "set_TextParameters", 1);
                _process = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    fastTextClass, "EnsureDirtyFlagsProcessed", 0);
            }

            if (constrainedClass != IntPtr.Zero)
                _relayout = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    constrainedClass, "_qQA", 0);
            _resolved = _set != IntPtr.Zero;

            return _set != IntPtr.Zero;
        }

        private static string Found(IntPtr method) => method != IntPtr.Zero ? "found" : "missing";

        internal static string Read(IntPtr fastText)
        {
            if (!Memory.LooksLikeObject(fastText)) return null;

            try
            {
                return Memory.Text(Memory.Ptr(fastText + TextParameters + TpParam0String), 128);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool Shows(IntPtr fastText, string words) =>
            words != null && string.Equals(Read(fastText), words, StringComparison.Ordinal);

        internal static IntPtr Write(IntPtr fastText, string words)
        {
            if (!Memory.LooksLikeObject(fastText) || words == null) return IntPtr.Zero;
            IntPtr created = NewString(words);
            if (created == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                if (_set != IntPtr.Zero)
                {
                    byte* image = stackalloc byte[TpSize];
                    Buffer.MemoryCopy((void*)(fastText + TextParameters), image, TpSize, TpSize);
                    Stage(image, created, words.Length);
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)image;
                    IntPtr raised = Raised.Call(_set, fastText, args);
                    if (raised != IntPtr.Zero) ModLog.Warn("level labels: the text setter raised " + Raised.Text(raised));
                }

                if (HoldString(fastText) != created) Fill(fastText, created, words.Length);
                Memory.WriteI32(fastText + DirtyFlags, Memory.I32(fastText + DirtyFlags) | DirtyMesh);
                Memory.WritePtr(fastText + CharBuffer, IntPtr.Zero);

                if (_process != IntPtr.Zero)
                {
                    IntPtr raised = Raised.Call(_process, fastText, null);
                    if (raised != IntPtr.Zero) ModLog.Warn("level labels: the dirty pass raised " + Raised.Text(raised));
                }

                return HoldString(fastText) == created ? created : IntPtr.Zero;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("level labels: a text could not be written: " + ModLog.Describe(e));
                return IntPtr.Zero;
            }
        }

        internal static IntPtr WriteRaw(IntPtr fastText, string words)
        {
            if (!Memory.LooksLikeObject(fastText) || words == null) return IntPtr.Zero;
            IntPtr created = NewString(words);
            if (created == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                Fill(fastText, created, words.Length);
                Memory.WriteI32(fastText + DirtyFlags, Memory.I32(fastText + DirtyFlags) | DirtyMesh);
                Memory.WritePtr(fastText + CharBuffer, IntPtr.Zero);
                return created;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("level labels: a text could not be written: " + ModLog.Describe(e));
                return IntPtr.Zero;
            }
        }

        private static unsafe void Stage(byte* image, IntPtr created, int length)
        {
            *(int*)(image + TpKey) = 0;
            *(int*)(image + TpLayout) = LayoutDirect;
            *(int*)(image + TpParam0Key) = 0;
            *(int*)(image + TpParam1Key) = 0;

            for (int i = 0; i < TpParam1Key - TpRuntimeString; i += IntPtr.Size)
                *(IntPtr*)(image + TpRuntimeString + i) = IntPtr.Zero;
            *(IntPtr*)(image + TpParam0String) = created;
            *(int*)(image + TpParam0Length) = length;
        }

        private static unsafe void Fill(IntPtr fastText, IntPtr created, int length)
        {
            byte* image = stackalloc byte[TpSize];
            Buffer.MemoryCopy((void*)(fastText + TextParameters), image, TpSize, TpSize);
            Stage(image, created, length);
            Buffer.MemoryCopy(image, (void*)(fastText + TextParameters), TpSize, TpSize);
        }

        internal static bool Holds(IntPtr fastText, IntPtr expected) =>
            Memory.LooksLikeObject(fastText) && expected != IntPtr.Zero && HoldString(fastText) == expected;

        private static IntPtr HoldString(IntPtr fastText) =>
            Memory.Ptr(fastText + TextParameters + TpParam0String);

        internal static void Relayout(IntPtr node)
        {
            if (!Memory.LooksLikeObject(node) || _relayout == IntPtr.Zero) return;
            IntPtr raised = Raised.Call(_relayout, node, null);
            if (raised != IntPtr.Zero) ModLog.Warn("level labels: the relayout raised " + Raised.Text(raised));
        }

        [DllImport("GameAssembly", EntryPoint = "il2cpp_string_new_utf16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr il2cpp_string_new_utf16(char* text, int length);

        internal static IntPtr NewString(string words)
        {
            if (words == null) words = string.Empty;

            try
            {
                fixed (char* characters = words)
                {
                    IntPtr created = il2cpp_string_new_utf16(characters, words.Length);
                    if (created != IntPtr.Zero) return created;
                }
            }
            catch (Exception e)
            {
                ModLog.Warn("level labels: the utf16 string call failed: " + ModLog.Describe(e));
            }

            return Il2CppInterop.Runtime.IL2CPP.il2cpp_string_new(words);
        }

        internal static int ArrayLength(IntPtr array)
        {
            if (!Memory.LooksLikeObject(array)) return 0;
            int length = Memory.I32(array + Offsets.Runtime.ArrayLength);
            return length > 0 && length <= 64 ? length : 0;
        }

        internal static IntPtr Element(IntPtr array, int index) =>
            Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + index * IntPtr.Size);
        internal const int ConstrainedText = 0x170;

        internal static int WriteConstrained(IntPtr array, string words)
        {
            int length = ArrayLength(array);
            int written = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr node = Element(array, i);
                if (!Memory.LooksLikeObject(node)) continue;
                IntPtr text = Memory.Ptr(node + ConstrainedText);
                if (!Memory.LooksLikeObject(text)) continue;
                LevelLabel.Dress(text, words);
                if (Write(text, words) != IntPtr.Zero) written++;
                Relayout(node);
            }

            return written;
        }

        internal static int WriteTexts(IntPtr array, string words)
        {
            int length = ArrayLength(array);
            int written = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr text = Element(array, i);
                if (!Memory.LooksLikeObject(text)) continue;
                LevelLabel.Dress(text, words);
                if (Write(text, words) != IntPtr.Zero) written++;
            }

            return written;
        }

        internal static void RestoreNode(IntPtr node, int rating)
        {
            if (!Memory.LooksLikeObject(node)) return;
            IntPtr text = Memory.Ptr(node + ConstrainedText);
            if (!Memory.LooksLikeObject(text)) return;
            bool borrowed = LevelLabel.ReleaseFont(text);
            if (rating <= 0)
            {
                if (borrowed) Relayout(node);
                return;
            }

            string words = rating.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (borrowed || !Shows(text, words)) Write(text, words);
            Relayout(node);
        }

        internal static int RestoreConstrained(IntPtr array, int rating)
        {
            string words = rating > 0
                ? rating.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            int length = ArrayLength(array);
            int restored = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr node = Element(array, i);
                if (!Memory.LooksLikeObject(node)) continue;
                IntPtr text = Memory.Ptr(node + ConstrainedText);
                if (!Memory.LooksLikeObject(text)) continue;
                bool borrowed = LevelLabel.ReleaseFont(text);
                if (words == null)
                {
                    if (borrowed) Relayout(node);
                    continue;
                }

                if (borrowed || !Shows(text, words))
                {
                    Write(text, words);
                    restored++;
                }

                Relayout(node);
            }

            return restored;
        }

        internal static int RestoreTexts(IntPtr array, int rating)
        {
            string words = rating > 0
                ? rating.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            int length = ArrayLength(array);
            int restored = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr text = Element(array, i);
                if (!Memory.LooksLikeObject(text)) continue;
                bool borrowed = LevelLabel.ReleaseFont(text);
                if (words == null) continue;

                if (borrowed || !Shows(text, words))
                {
                    Write(text, words);
                    restored++;
                }
            }

            return restored;
        }
    }
}

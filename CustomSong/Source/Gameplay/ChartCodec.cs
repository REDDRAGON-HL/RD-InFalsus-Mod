using System;

namespace CustomSong
{
    internal static unsafe class ChartCodec
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> _gab;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr> _hab;
        private static int _noteSize;
        private const int MaxChartMs = 3_600_000;

        internal static bool Resolve()
        {
            IntPtr p = MethodResolver.ByName("_S", "_Gab");
            if (p == IntPtr.Zero) return false;
            _gab = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)p;

#if DEBUG
            IntPtr reference = MethodResolver.MethodInfoOf("_S", "_Gab");
            if (reference != IntPtr.Zero)
#endif

            IntPtr hab = MethodResolver.ByName("_S", "_hab");
            if (hab == IntPtr.Zero) return false;
            _hab = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)hab;
            return true;
        }

        internal static bool Empty(IntPtr tuple32)
        {
            if (_hab == null)
            {
                ModLog.Error("the game's empty-chart constructor is not in this build; a chart " +
                                  "that fails to decode cannot be served");
                return false;
            }

            _hab(tuple32, IntPtr.Zero);
            return true;
        }

        private const int SideMin = 1;
        private const int SideMax = 4;

        internal static bool TryDecode(byte[] bytes, string seed, IntPtr tuple32, out string reason)
        {
            reason = null;
            bytes = TextChart.Encoded(bytes, seed, out reason);
            if (bytes == null) return false;

            if (_gab == null)
            {
#if DEBUG
                reason = "the chart decoder could not be reached";
#endif
                return false;
            }

            IntPtr name = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(seed);
            if (name == IntPtr.Zero)
            {
#if DEBUG
                reason = $"the name '{seed}' could not be passed to the decoder";
#endif
                return false;
            }

            byte* span = stackalloc byte[Offsets.Runtime.SpanSize];
            fixed (byte* data = bytes)
            {
                *(IntPtr*)span = (IntPtr)data;
                *(int*)(span + Offsets.Runtime.SpanLength) = bytes.Length;
                _gab(tuple32, (IntPtr)span, name, IntPtr.Zero);
            }

            return Inspect(tuple32, seed, out reason);
        }

        private static bool Inspect(IntPtr tuple32, string seed, out string reason)
        {
            reason = null;
            IntPtr notes = Memory.Ptr(tuple32 + Offsets.Runtime.ChartTupleNotes);
            if (!Memory.TryList(notes, out IntPtr items, out int count))
            {
#if DEBUG
                reason = "the decoder returned no note list";
#endif
                return false;
            }
            if (count == 0)
            {
#if DEBUG
                reason = "the decoder found no notes (bad container, or encoded under another name)";
#endif
                return false;
            }

            if (!MeasureNoteSize(items, count))
            {
#if DEBUG
                reason = "the note records could not be read this build";
#endif
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                IntPtr note = items + i * _noteSize;
                int side = Memory.U8(note + Offsets.Note.Side);
                int type = Memory.U8(note + Offsets.Note.Type);
                int start = Memory.I32(note + Offsets.Note.StartMs);
                int end = Memory.I32(note + Offsets.Note.EndMs);

                if (side < SideMin || side > SideMax)
                {
#if DEBUG
                    reason = $"note {i} is on plane {side} under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
                if (!IsNoteType(type))
                {
#if DEBUG
                    reason = $"note {i} has type {type} under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
                if (start < 0 || end < start || end > MaxChartMs)
                {
#if DEBUG
                    reason = $"note {i} runs {start}..{end}ms under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
            }

            return true;
        }

        private static void Dump(IntPtr items, int count)
        {
            int show = count < 4 ? count : 4;
            for (int i = 0; i < show; i++)
            {
                IntPtr note = items + i * _noteSize;
                var text = new System.Text.StringBuilder(_noteSize * 2);
                for (int b = 0; b < _noteSize; b++)
                    text.Append(Memory.U8(note + b).ToString("X2"));
            }
        }

        private static bool IsNoteType(int type) => type == 1 || type == 2 || type == 4 || type == 5;

        private static bool MeasureNoteSize(IntPtr items, int count)
        {
            if (!ResolveNoteFields()) return false;
            if (_strideMissing) return false;

            try
            {
                IntPtr array = items - Offsets.Runtime.ArrayDataOffset;
                long byteLength = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
                int stride = (int)(byteLength / count);

                if (stride > 0)
                {
                    if (_noteSize > 0 && stride != _noteSize)
                        ModLog.Warn($"note records measure {stride} bytes, not the {_noteSize} measured before");
                    _noteSize = stride;
                    return true;
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"could not measure the note stride: {ModLog.Describe(e)}");
            }

            _strideMissing = true;
            ModLog.Error("the note records' stride could not be measured in this build; " +
                              "no chart can be checked, so none will load");
            return false;
        }

        private static bool _offsetsResolved;
        private static bool _noteFieldsMissing;
        private static bool _strideMissing;

        private static bool ResolveNoteFields()
        {
            if (_offsetsResolved) return true;
            if (_noteFieldsMissing) return false;
            if (FieldResolver.ClassPointer("_fA") == IntPtr.Zero) return false;
            Offsets.Note.Side = FieldResolver.Field("_fA", "_Ae");
            Offsets.Note.Type = FieldResolver.Field("_fA", "_be");
            Offsets.Note.StartMs = FieldResolver.Field("_fA", "_Be");
            Offsets.Note.EndMs = FieldResolver.Field("_fA", "_ce");

            if (Offsets.Note.Side < 0 || Offsets.Note.Type < 0 ||
                Offsets.Note.StartMs < 0 || Offsets.Note.EndMs < 0)
            {
                _noteFieldsMissing = true;
                ModLog.Error("the note record's fields could not be read by name in this build; " +
                                  "no chart can be checked, so none will load");
                return false;
            }

            _offsetsResolved = true;
            return true;
        }
    }
}

using System;

namespace CustomSong
{
    internal static unsafe class StreamingAssets
    {
        private static int FieldFiles = Offsets.Unresolved;
        private static int FieldLengths = Offsets.Unresolved;
        private static int FieldFlags = Offsets.Unresolved;
        private static int FieldChunks = Offsets.Unresolved;
        private static int SingletonField = Offsets.Unresolved;
        private static bool _fieldsAsked;
        private static bool _fieldsMissing;
        private const int None = -1;
        private static IntPtr _bg;
        private static int _next;
        internal static bool Resolved => _bg != IntPtr.Zero;

        internal static bool Resolve()
        {
            try
            {
                IntPtr klass = FieldResolver.ClassPointer("_BG");
                if (klass == IntPtr.Zero)
                {
                    ModLog.Warn("_BG could not be found by name; custom audio will not work");
                    return false;
                }

                IntPtr statics = FieldResolver.Statics(klass);
                if (statics == IntPtr.Zero)
                {
                    ModLog.Warn("_BG has no statics block; custom audio will not work");
                    return false;
                }

                if (!_fieldsAsked)
                {
                    _fieldsAsked = true;
                    FieldFiles = FieldResolver.Field("_BG", "_TxA");
                    FieldLengths = FieldResolver.Field("_BG", "_uxA");
                    FieldFlags = FieldResolver.Field("_BG", "_UxA");
                    FieldChunks = FieldResolver.Field("_BG", "_vxA");
                    SingletonField = FieldResolver.Field("_BG", "_RxA");

                    _fieldsMissing = FieldFiles < 0 || FieldLengths < 0 || FieldFlags < 0 ||
                                     FieldChunks < 0 || SingletonField < 0;
                }

                if (_fieldsMissing) return false;
                IntPtr bg = Memory.Ptr(statics + SingletonField);
                if (!Memory.LooksLikeObject(bg))
                {
                    ModLog.Warn("the streaming asset manager singleton is not there yet; " +
                                     "custom audio will not work");
                    return false;
                }

                IntPtr lengths = Memory.Ptr(bg + FieldLengths);
                if (!Memory.LooksLikeObject(lengths))
                {
                    ModLog.Warn("the streaming asset manager has no length table; custom audio " +
                                     "will not work");
                    return false;
                }

                int count = Memory.I32(lengths + Offsets.Runtime.ArrayLength);
                if (count <= 0)
                {
                    ModLog.Warn($"the streaming asset manager lists {count} files; custom audio " +
                                     "will not work");
                    return false;
                }

                _bg = bg;
                _next = count;
                return true;
            }
            catch (Exception e)
            {
                ModLog.Warn($"the streaming asset manager could not be reached: {ModLog.Describe(e)}");
                return false;
            }
        }

        internal static int Add(int length)
        {
            if (!Resolved) return -1;
            int index = _next;
            int size = index + 1;
            if (!Grow(FieldFiles, size, standIn: 0, own: false)) return -1;
            if (!Grow(FieldFlags, size, standIn: None, own: false)) return -1;
            if (!Grow(FieldChunks, size, standIn: 8, own: true)) return -1;
            if (!Grow(FieldLengths, size, standIn: None, own: false)) return -1;
            IntPtr lengths = Memory.Ptr(_bg + FieldLengths);
            Memory.WriteI64(lengths + Offsets.Runtime.ArrayDataOffset + index * sizeof(long), length);
            _next = size;
            return index;
        }

        private static bool Grow(int field, int size, int standIn, bool own)
        {
            IntPtr old = Memory.Ptr(_bg + field);
            if (!Memory.LooksLikeObject(old)) { ModLog.Warn($"field +0x{field:X} is not an array"); return false; }

            int oldCount = Memory.I32(old + Offsets.Runtime.ArrayLength);
            if (size <= oldCount) return true;

            int stride = oldCount > 0
                ? (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(old) / oldCount)
                : sizeof(long);
            if (stride <= 0) return false;
            if (standIn != None && standIn + IntPtr.Size > stride)
            {
                ModLog.Warn($"field +0x{field:X} has {stride}-byte elements with no room at +0x{standIn:X}");
                return false;
            }

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(old);
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element == IntPtr.Zero) { ModLog.Warn($"field +0x{field:X} has no element class"); return false; }

            IntPtr grown = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, (ulong)size);
            if (grown == IntPtr.Zero) return false;
            IntPtr source = old + Offsets.Runtime.ArrayDataOffset;
            IntPtr target = grown + Offsets.Runtime.ArrayDataOffset;
            Buffer.MemoryCopy((void*)source, (void*)target, (long)size * stride, (long)oldCount * stride);

            if (standIn != None && size > oldCount)
            {
                IntPtr stand = IntPtr.Zero;
                for (int i = 0; i < oldCount && stand == IntPtr.Zero; i++)
                {
                    IntPtr value = *(IntPtr*)(source + i * stride + standIn);
                    if (!Memory.LooksLikeObject(value)) continue;
                    stand = own ? EmptyLike(value) : value;
                }

                if (stand == IntPtr.Zero)
                {
                    ModLog.Warn($"no entry of +0x{field:X} has anything at +0x{standIn:X} to go " +
                                     "by; the custom files are not registered");
                    return false;
                }

                for (int i = oldCount; i < size; i++)
                    *(IntPtr*)(target + i * stride + standIn) = stand;
            }

            Memory.WritePtr(_bg + field, grown);
            return true;
        }

        private static IntPtr EmptyLike(IntPtr like)
        {
            if (!Memory.LooksLikeObject(like)) return IntPtr.Zero;
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(like);
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element != IntPtr.Zero)
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, 0);
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(klass);
        }
    }
}

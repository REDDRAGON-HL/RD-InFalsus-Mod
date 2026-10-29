using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CustomSong
{
    internal static unsafe class ReadBookkeeping
    {
        private static int FieldDispatched = Offsets.Unresolved;
        private static int FieldCancelled = Offsets.Unresolved;
        private static int FieldState = Offsets.Unresolved;
        private static bool _fieldsResolved;
        private static bool _fieldsMissing;

        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;
            FieldDispatched = FieldResolver.Field("_hF", "_vSA");
            FieldCancelled = FieldResolver.Field("_hF", "_VSA");
            FieldState = FieldResolver.Field("_hF", "_tSA");

            if (FieldDispatched < 0 || FieldCancelled < 0 || FieldState < 0)
            {
                _fieldsMissing = true;
                ModLog.Error("the read bookkeeping's fields could not be read by name in this build; " +
                                  "a served read will not release its block");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        private const int NotReading = 2;
        private static IntPtr _statics;
        private static IntPtr _remove, _removeInfo;
        private static IntPtr _contains, _containsInfo;
        private static bool _ready;
        private static bool _warned;
        internal static long Skipped;
        internal static long NotTaken;
        internal static long Released;

        internal static void Finished(IntPtr record)
        {
            if (!Ready()) return;
            IntPtr info = Memory.Ptr(record);
            if (info == IntPtr.Zero) return;
            Call(_remove, _removeInfo, Memory.Ptr(_statics + FieldDispatched), info);
            Call(_remove, _removeInfo, Memory.Ptr(_statics + FieldCancelled), info);
            Released++;
        }

        internal static void Observe(IntPtr record)
        {
            if (!Ready()) return;

            if (Memory.I32(_statics + FieldState) == NotReading) { NotTaken++; return; }

            IntPtr info = Memory.Ptr(record);
            if (info == IntPtr.Zero) return;

            if (Call(_contains, _containsInfo, Memory.Ptr(_statics + FieldCancelled), info) != 0)
                Skipped++;
        }

        internal static int Outstanding()
        {
            if (!Ready()) return -1;
            IntPtr dispatched = Memory.Ptr(_statics + FieldDispatched);
            return Memory.LooksLikeObject(dispatched)
                ? Memory.I32(dispatched + Offsets.Runtime.HashSetCount)
                : -1;
        }

        private static bool Ready()
        {
            if (_ready) return true;

            try
            {
                if (_statics == IntPtr.Zero)
                {
                    IntPtr klass = FieldResolver.ClassPointer("_hF");
                    if (klass == IntPtr.Zero) return false;
                    _statics = FieldResolver.Statics(klass);
                    if (_statics == IntPtr.Zero) return false;
                }

                if (!ResolveFields()) return false;
                IntPtr sample = Memory.Ptr(_statics + FieldDispatched);
                if (!Memory.LooksLikeObject(sample)) return false;
                IntPtr setClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(sample);
                if (setClass == IntPtr.Zero) return false;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(setClass);
                _remove = MethodOn(setClass, "Remove", 1, out _removeInfo);
                _contains = MethodOn(setClass, "Contains", 1, out _containsInfo);
                if (_remove == IntPtr.Zero || _contains == IntPtr.Zero)
                {
                    Report("the outstanding-read sets could not be read; a served read will not " +
                           "release its block");
                    return false;
                }

                _ready = true;
                return true;
            }
            catch (Exception e)
            {
                Report($"the outstanding-read sets could not be reached: {ModLog.Describe(e)}");
                return false;
            }
        }

        [Conditional("DEBUG")]
        private static void Report(string why)
        {
            if (_warned) return;
            _warned = true;
            ModLog.Warn(why);
        }

        private static IntPtr MethodOn(IntPtr klass, string name, int parameters, out IntPtr methodInfo)
        {
            methodInfo = IntPtr.Zero;
            IntPtr iter = IntPtr.Zero;
            IntPtr method;
            while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
            {
                if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != name) continue;
                if ((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method) != parameters) continue;
                IntPtr code = *(IntPtr*)method;
                if (code == IntPtr.Zero) continue;
                methodInfo = method;
                return code;
            }

            return IntPtr.Zero;
        }

        private static byte Call(IntPtr code, IntPtr methodInfo, IntPtr set, IntPtr value) =>
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)code)(set, value, methodInfo);
    }
}

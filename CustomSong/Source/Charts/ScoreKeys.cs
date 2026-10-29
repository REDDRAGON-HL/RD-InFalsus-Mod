using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static unsafe class ScoreKeys
    {
        internal static long Refiled;
        private const string Records = "GameResultsV4.highscores";
        private const string KeyCollection = "Dictionary.KeyCollection";
        private const string ValueCollection = "Dictionary.ValueCollection";

        internal static void Migrate(IntPtr table)
        {
            if (table == IntPtr.Zero) return;

            try
            {
                IntPtr tableClass = FieldResolver.ClassPointer("GameResultsV4");
                int highscores = FieldResolver.Field("GameResultsV4", "highscores");
                if (tableClass == IntPtr.Zero || highscores < 0) { MigrateFailed("no table"); return; }

                IntPtr scores = Memory.Ptr(table + highscores);
                if (!Memory.LooksLikeObject(scores)) return;
                var slots = new Dictionary<string, ushort>();
                foreach (KeyValuePair<ushort, ChartInfo> song in SongCatalog.ById)
                    slots[song.Value.BaseName] = song.Key;
                if (slots.Count == 0) return;
                IntPtr scoresClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(scores);
                IntPtr count = Method(scoresClass, "get_Count", 0, Records);
                IntPtr keys = Method(scoresClass, "get_Keys", 0, Records);
                IntPtr values = Method(scoresClass, "get_Values", 0, Records);
                IntPtr take = Method(scoresClass, "Remove", 2, Records);
                IntPtr write = Method(scoresClass, "set_Item", 2, Records);
                if (count == IntPtr.Zero || keys == IntPtr.Zero || values == IntPtr.Zero ||
                    take == IntPtr.Zero || write == IntPtr.Zero)
                { MigrateFailed("no method"); return; }

                IntPtr raised = IntPtr.Zero;
                IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(count, scores, null, ref raised);
                int stored = Memory.LooksLikeObject(boxed) ? Memory.I32(boxed + Offsets.Runtime.BoxedData) : 0;
                if (stored <= 0) return;
                IntPtr collection = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(keys, scores, null, ref raised);
                IntPtr copy = Memory.LooksLikeObject(collection)
                    ? Method(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(collection), "CopyTo", 2, KeyCollection)
                    : IntPtr.Zero;
                IntPtr keyElement = ElementClass(copy);
                if (keyElement == IntPtr.Zero) { MigrateFailed("no key type"); return; }

                IntPtr records = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(values, scores, null, ref raised);
                IntPtr valueCopy = Memory.LooksLikeObject(records)
                    ? Method(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(records), "CopyTo", 2, ValueCollection)
                    : IntPtr.Zero;
                IntPtr recordElement = ElementClass(valueCopy);
                if (recordElement == IntPtr.Zero) { MigrateFailed("no record"); return; }

                IntPtr array = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(keyElement, (ulong)stored);
                IntPtr record = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(recordElement, 1);
                int stride = FieldResolver.ElementSize(array, "GameResultKey.Size");
                int nameAt = FieldResolver.Field("GameResultKey", "BaseName");
                int idAt = FieldResolver.Field("GameResultKey", "SongId");
                int songIdAt = FieldResolver.Field("GameResultV4", "SongId");
                if (!Memory.LooksLikeObject(array) || !Memory.LooksLikeObject(record) ||
                    stride <= 0 || nameAt < 0 || idAt < 0 || songIdAt < 0)
                { MigrateFailed("no shape"); return; }

                IntPtr* copyArgs = stackalloc IntPtr[2];
                int zero = 0;
                copyArgs[0] = array;
                copyArgs[1] = (IntPtr)(&zero);
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(copy, collection, (void**)copyArgs, ref raised);
                Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(array, false);
                Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(record, false);
                IntPtr keysAt = array + Offsets.Runtime.ArrayDataOffset;
                IntPtr recordAt = record + Offsets.Runtime.ArrayDataOffset;
                byte* next = stackalloc byte[stride];
                IntPtr* takeArgs = stackalloc IntPtr[2];
                IntPtr* writeArgs = stackalloc IntPtr[2];

                for (int i = 0; i < stored; i++)
                {
                    IntPtr key = keysAt + i * stride;
                    IntPtr namePointer = Memory.Ptr(key + nameAt);
                    string name = namePointer == IntPtr.Zero ? null : Memory.Text(namePointer);
                    if (name == null || !slots.TryGetValue(name, out ushort slot)) continue;
                    if (Memory.U16(key + idAt) == slot) continue;
                    takeArgs[0] = key;
                    takeArgs[1] = recordAt;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(take, scores, (void**)takeArgs, ref raised);
                    Memory.WriteU16(recordAt + songIdAt, slot);
                    Buffer.MemoryCopy((void*)key, next, stride, stride);
                    Memory.WriteU16((IntPtr)next + idAt, slot);
                    writeArgs[0] = (IntPtr)next;
                    writeArgs[1] = recordAt;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(write, scores, (void**)writeArgs, ref raised);
                    Refiled++;
                }
            }
            catch (Exception e)
            {
                MigrateFailed(ModLog.Describe(e));
            }
        }

        private static void MigrateFailed(string why)
        {
            ModLog.Warn("the stored song ids could not be re-filed: " + why);
            ModLog.Error("the custom results' stored song ids could not be rewritten, so scores " +
                              "recorded under an earlier build's ids will not be found: " + why);
        }

        private static IntPtr ElementClass(IntPtr copy)
        {
            if (copy == IntPtr.Zero) return IntPtr.Zero;
            IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(copy, 0);
            IntPtr arrayClass = type == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(type);
            if (arrayClass == IntPtr.Zero) return IntPtr.Zero;
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(arrayClass);
            if (element == IntPtr.Zero) return IntPtr.Zero;
            IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(element);
            return parent != IntPtr.Zero &&
                   Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(parent) == "ValueType"
                ? element
                : IntPtr.Zero;
        }

        private static IntPtr Method(IntPtr klass, string name, int parameters, string on)
        {
            IntPtr method = klass == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);

            if (method == IntPtr.Zero)
                ModLog.Error($"{on}.{name}: no method by that name in this build");
            return method;
        }
    }
}

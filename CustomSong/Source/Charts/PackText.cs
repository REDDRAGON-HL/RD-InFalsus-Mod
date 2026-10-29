using System;
using System.Collections.Generic;
using System.IO;

namespace CustomSong
{
    internal static unsafe class PackText
    {
        private static int PackAssetsTable = Offsets.Unresolved;
        private static int PackAssetsField = Offsets.Unresolved;
        private static int StringMappingField = Offsets.Unresolved;

        internal static bool TryGetVisuals(out IntPtr packAssets)
        {
            packAssets = IntPtr.Zero;
            if (!ResolveFields()) return false;
            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;
            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;
            packAssets = Memory.Ptr(statics + PackAssetsField);
            return Memory.LooksLikeObject(packAssets);
        }

        internal static bool PrepareVisualRows(IntPtr packAssets, int highestId)
        {
            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table)) return false;
            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            if (count <= 0) return false;
            int wanted = highestId + 1;
            if (count == wanted) return true;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int stride = (int)(bytes / count);
            if (stride <= 0) return false;
            IntPtr grown = SongCatalog.NewArray(table, wanted);
            if (grown == IntPtr.Zero) return false;
            IntPtr source = table + Offsets.Runtime.ArrayDataOffset;
            IntPtr target = grown + Offsets.Runtime.ArrayDataOffset;
            long kept = (long)Math.Min(count, wanted) * stride;
            Buffer.MemoryCopy((void*)source, (void*)target, kept, kept);
            StoreDefaultArt(source, stride);
            IntPtr shape = source + Math.Min(TemplateRow, count - 1) * stride;
            for (int i = count; i < wanted; i++)
                Buffer.MemoryCopy((void*)shape, (void*)(target + i * stride), stride, stride);
            Memory.WritePtr(packAssets + PackAssetsTable, grown);
            return true;
        }

        private const int TemplateRow = 2;
        private static readonly IntPtr[] DefaultArt = new IntPtr[14];
        private static bool _defaultArt;

        private static unsafe void StoreDefaultArt(IntPtr rows, int stride)
        {
            if (_defaultArt) return;

            for (int i = 0; i < VisualSet.Length; i++)
            {
                int offset = FieldResolver.Lookup("SongSelectPackAssets", VisualSet[i].Field);
                if (offset < 0) return;
                DefaultArt[i] = Memory.Ptr(rows + offset);
            }

            _defaultArt = true;
        }

        private static readonly (string Field, string Key, string File)[] VisualSet =
        {
            ("SongSelectLargeNonCompleted", "songSelect", "packvisual-songselect-default.png"),
            ("SongSelectLargeNonCompletedHover", "songSelectHover", "packvisual-songselect-hover.png"),
            ("SongSelectLargeCompleted", "songSelect", "packvisual-songselect-default.png"),
            ("SongSelectLargeCompletedHover", "songSelectHover", "packvisual-songselect-hover.png"),
            ("PackSelectLargeNonCompleted", "selected", "packvisual-selected-default.png"),
            ("PackSelectLargeNonCompletedHover", "selectedHover", "packvisual-selected-hover.png"),
            ("PackSelectLargeCompleted", "selected", "packvisual-selected-default.png"),
            ("PackSelectLargeCompletedHover", "selectedHover", "packvisual-selected-hover.png"),
            ("PackSelectNonCompleted", "deselected", "packvisual-deselected-default.png"),
            ("PackSelectNonCompletedHover", "deselectedHover", "packvisual-deselected-hover.png"),
            ("PackSelectCompleted", "deselected", "packvisual-deselected-default.png"),
            ("PackSelectCompletedHover", "deselectedHover", "packvisual-deselected-hover.png"),
            ("OutlineNonCompleted", "outline", "outline-packvisua.png"),
            ("OutlineCompleted", "outline", "outline-packvisua.png"),
        };

        internal static IntPtr RowMaterial(IntPtr packAssets, int packId, string field)
        {
            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table)) return IntPtr.Zero;
            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            if (packId < 0 || packId >= count) return IntPtr.Zero;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int stride = count > 0 ? (int)(bytes / count) : 0;
            if (stride <= 0) return IntPtr.Zero;
            int offset = FieldResolver.Lookup("SongSelectPackAssets", field);
            if (offset < 0) return IntPtr.Zero;
            return Memory.Ptr(table + Offsets.Runtime.ArrayDataOffset + packId * stride + offset);
        }

        private static readonly Dictionary<string, IntPtr> Built =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        internal static int SetPackVisuals(IntPtr packAssets, int packId, string folder,
                                          Dictionary<string, string> overrides)
        {
            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table)) return 0;
            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            if (packId < 0 || packId >= count) return 0;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int stride = count > 0 ? (int)(bytes / count) : 0;
            if (stride <= 0) return 0;
            IntPtr row = table + Offsets.Runtime.ArrayDataOffset + packId * stride;
            int applied = 0;
            int slot = 0;

            for (int index = 0; index < VisualSet.Length; index++, slot++)
            {
                (string field, string key, string file) = VisualSet[index];
                string path = Picture(folder, overrides, key, file);
                int offset = FieldResolver.Lookup("SongSelectPackAssets", field);
                if (offset < 0) continue;

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    IntPtr fallback = DefaultArt[slot];
                    if (Memory.LooksLikeObject(fallback)) Memory.WritePtr(row + offset, fallback);
                    continue;
                }

                IntPtr source = Memory.Ptr(row + offset);
                if (!Memory.LooksLikeObject(source))
                {
                    ModLog.Warn($"pack {packId} visual '{field}' has no material to clone");
                    continue;
                }

                string materialKey = packId + "|" + key;
                if (!Built.TryGetValue(materialKey, out IntPtr material))
                {
                    material = JacketFactory.Cover(path, source, out string reason);
                    if (material == IntPtr.Zero)
                    {
                        ModLog.Warn($"pack {packId} visual '{field}' failed: {reason}");
                        continue;
                    }

                    Built[materialKey] = material;
                }

                Memory.WritePtr(row + offset, material);
                applied++;
            }

            return applied;
        }

        internal static void ResetVisualCache() => Built.Clear();

        private static string Picture(string folder, Dictionary<string, string> overrides,
                                      string key, string file)
        {
            if (overrides != null && overrides.TryGetValue(key, out string custom) &&
                File.Exists(custom))
                return custom;
            string path = Path.Combine(folder, file);
            if (File.Exists(path)) return path;

            string other = file.Contains("-hover")
                ? file.Replace("-hover", "-default")
                : file.Replace("-default", "-hover");
            path = Path.Combine(folder, other);
            return File.Exists(path) ? path : null;
        }

        internal static void RefreshLookup(IntPtr packData)
        {
            IntPtr klass = FieldResolver.ClassPointer("PackData");
            if (klass == IntPtr.Zero) { ModLog.Warn("PackData could not be found; the pack lookup is stale"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_SkA", 0);
            if (method == IntPtr.Zero)
            {
                ModLog.Warn("PackData._SkA could not be found; the pack lookup is stale");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, packData, null, ref raised);
            if (raised != IntPtr.Zero)
                ModLog.Warn($"the game's pack lookup rebuild raised: {Raised.Text(raised)}");
        }

        private static int PackIdTypeMapping = Offsets.Unresolved;
        private static int SongTitleTypeMapping = Offsets.Unresolved;
        private static int SongArtistTypeMapping = Offsets.Unresolved;
        private static int SongIllustratorTypeMapping = Offsets.Unresolved;
        private static bool _fieldsResolved;
        private static bool _fieldsMissing;

        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;
            if (FieldResolver.ClassPointer("DataAccess") == IntPtr.Zero) return false;
            PackAssetsTable = FieldResolver.Field("PackSelectSceneAssets", "packToAssets");
            PackIdTypeMapping = FieldResolver.Field("DynamicStringMapping", "packIdTypeMapping");
            SongTitleTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdTitleTypeMapping");
            SongArtistTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdArtistTypeMapping");
            SongIllustratorTypeMapping = FieldResolver.Field("DynamicStringMapping",
                                                             "jacketIllustratorNameTypeMapping");
            PackAssetsField = FieldResolver.Field("DataAccess", "_NAb");
            StringMappingField = FieldResolver.Field("DataAccess", "_mAb");

            if (PackAssetsTable < 0 || PackIdTypeMapping < 0 || SongTitleTypeMapping < 0 ||
                SongArtistTypeMapping < 0 || SongIllustratorTypeMapping < 0 ||
                PackAssetsField < 0 || StringMappingField < 0)
            {
                _fieldsMissing = true;
                ModLog.Error("the pack's fields could not be read by name in this build; the custom " +
                                  "pack keeps the game's own visuals and text");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        private static readonly int TextMappingValuesSize = 5 * IntPtr.Size;
        private static readonly int ValueEnglish = 0 * IntPtr.Size;
        private static readonly int ValueJapanese = 1 * IntPtr.Size;
        private static readonly int ValueTraditionalChinese = 3 * IntPtr.Size;
        private static readonly int ValueSimplifiedChinese = 4 * IntPtr.Size;
        private static int _mappingOffset = int.MinValue;

        private enum TextWhat { PackName, SongText }

        internal static bool SetPackName(int packId, string name)
        {
            if (!ResolveFields()) return false;
            return SetText(PackIdTypeMapping, packId, name, TextWhat.PackName);
        }

        internal enum SongText { Title, Artist, Illustrator }

        internal static bool SetSongText(SongText which, int songId, string text)
        {
            if (!ResolveFields()) return false;

            int mappingField = which == SongText.Title ? SongTitleTypeMapping
                             : which == SongText.Artist ? SongArtistTypeMapping
                             : SongIllustratorTypeMapping;
            return SetText(mappingField, songId, text, TextWhat.SongText);
        }

        private static bool SetText(int mappingField, int id, string text, TextWhat what)
        {
            if (!ResolveFields()) return false;
            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;
            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;
            IntPtr mapping = Memory.Ptr(statics + StringMappingField);
            if (!Memory.LooksLikeObject(mapping))
            {
                NotWritten(what, "the localisation table is missing");
                return false;
            }

            IntPtr typeMapping = Memory.Ptr(mapping + mappingField);
            if (!Memory.LooksLikeObject(typeMapping))
            {
                NotWritten(what, $"the localisation mapping at +0x{mappingField:X} is missing");
                return false;
            }

            int at = MappingOffset(typeMapping);
            if (at < 0) return false;
            IntPtr table = Memory.Ptr(typeMapping + at);
            if (!Memory.LooksLikeObject(table))
            {
                NotWritten(what, $"the localisation table at +0x{mappingField:X} is missing");
                return false;
            }

            if (!Insert(table, id, text)) return false;
            return true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void NotWritten(TextWhat what, string where) =>
            ModLog.Warn($"{where}; {Name(what)}");

        private static string Name(TextWhat what)
        {
#if DEBUG
            return what == TextWhat.PackName ? "the custom pack will have no name"
                                             : "the song will have no text of its own";
#else
            return null;
#endif
        }

        private static int MappingOffset(IntPtr typeMapping)
        {
            if (_mappingOffset != int.MinValue) return _mappingOffset;
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(typeMapping);
            if (klass != IntPtr.Zero)
            {
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "Mapping");
                if (field != IntPtr.Zero)
                {
                    int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                    if (offset > 0)
                    {
                        return _mappingOffset = offset;
                    }
                }
            }

            foreach (var entry in InteropTypeIndex.ByName())
            {
                if (!entry.Key.Contains("StringTypeMapping")) continue;
                if (entry.Key.IndexOf('`') >= 0) continue;
                int offset = FieldResolver.Lookup(entry.Key, "Mapping");
                if (offset > 0)
                {
                    return _mappingOffset = offset;
                }
            }

            ModLog.Error("StringTypeMapping.Mapping could not be found on its own class in this " +
                              "build; the custom pack's texts will not be written");
            return _mappingOffset = -1;
        }

        private static unsafe bool Insert(IntPtr table, int packId, string name)
        {
            ushort id = (ushort)packId;
            byte[] raw = new byte[TextMappingValuesSize];
            fixed (byte* values = raw)
            {
                IntPtr text = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
                *(IntPtr*)(values + ValueEnglish) = text;
                *(IntPtr*)(values + ValueJapanese) = text;
                *(IntPtr*)(values + ValueTraditionalChinese) = text;
                *(IntPtr*)(values + ValueSimplifiedChinese) = text;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)values;
                IntPtr dictClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(table);
                IntPtr setItem = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    dictClass, "set_Item", 2);
                if (setItem == IntPtr.Zero)
                {
                    ModLog.Warn("the pack name table has no setter; the custom pack will have no name");
                    return false;
                }

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(setItem, table, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    ModLog.Warn($"writing the pack name raised: {Raised.Text(raised)}");
                    return false;
                }
            }

            return true;
        }
    }
}

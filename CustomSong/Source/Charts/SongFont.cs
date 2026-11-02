using System;
using System.Collections.Generic;
using System.IO;

namespace CustomSong
{
    internal static unsafe class SongFont
    {
        private static HashSet<int> _needed;
        private static int _familyAssets = Offsets.Unresolved;
        private static int _familySets = Offsets.Unresolved;
        private static int _setAssets = Offsets.Unresolved;
        private static int _assetIdentifier = Offsets.Unresolved;
        private static int _assetBytes = Offsets.Unresolved;
        private static bool _resolved;
        private static bool _missing;
        private static readonly HashSet<IntPtr> _seenFamilies = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> _seenAssets = new HashSet<IntPtr>();
        private static byte[] _regularBytes, _boldBytes;
        private static IntPtr _regularArray, _boldArray;
        private static bool _noFont;
        private static byte[] _vivoRegular, _vivoBold;
        private static bool _gameFontsTried;
        private static byte[] _mergedRegular, _mergedBold;
        private static byte[] _gap;
        private static bool _gapTried;

        private static bool Resolve()
        {
            if (_resolved) return true;
            if (_missing) return false;
            _familyAssets = Field("FastTextFontFamily", "fontAssets");
            _familySets = Field("FastTextFontFamily", "nonEnglishLocalizationToFontSet");
            _setAssets = NestedField("FastTextFontFamily", "LocalizationFontSet", "FontAssets");
            _assetIdentifier = Field("FastTextAsset", "Identifier");
            _assetBytes = Field("FastTextAsset", "RawBytes");

            if (_familyAssets < 0 || _assetIdentifier < 0 || _assetBytes < 0)
            {
                _missing = true;
                ModLog.Warn("the font fields could not be read by name in this build; a song title " +
                            "with characters the game's fonts lack will stay blank");
                return false;
            }

            _resolved = true;
            return true;
        }

        private const string FontImage = "FastText.dll";
        private const string FontNamespace = "FastText.Data";

        private static int Field(string type, string field) =>
            Offset(Class(type), field);

        private static int NestedField(string outer, string nested, string field)
        {
            IntPtr klass = Class(outer);
            if (klass == IntPtr.Zero) return Offsets.Unresolved;
            IntPtr iter = IntPtr.Zero;
            IntPtr inner;
            while ((inner = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_nested_types(klass, ref iter)) !=
                   IntPtr.Zero)
            {
                if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(inner) != nested) continue;
                int offset = Offset(inner, field);
                return offset < 0 ? offset : offset - Offsets.Runtime.ObjectHeader;
            }

            return Offsets.Unresolved;
        }

        private static IntPtr Class(string type) =>
            Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(FontImage, FontNamespace, type);

        private static int Offset(IntPtr klass, string field)
        {
            if (klass == IntPtr.Zero) return Offsets.Unresolved;
            IntPtr info = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, field);
            if (info == IntPtr.Zero) return Offsets.Unresolved;
            return (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(info);
        }

        internal static bool Detect(List<ChartPack> packs)
        {
            var needed = new HashSet<int>();

            foreach (ChartPack pack in packs)
            {
                Collect(needed, pack.Name);
                Collect(needed, pack.Names);

                foreach (ChartInfo info in pack.Charts)
                {
                    Collect(needed, info.Name);
                    Collect(needed, info.Composer);
                    Collect(needed, info.Titles);
                    Collect(needed, info.ComposerLanguages);
                    Collect(needed, info.IllustratorLanguages);
                    Collect(needed, info.Charters);
                    foreach (string[] set in info.TitleSets ?? Array.Empty<string[]>()) Collect(needed, set);
                    foreach (string[] set in info.ComposerSets ?? Array.Empty<string[]>()) Collect(needed, set);
                    foreach (string[] set in info.IllustratorSets ?? Array.Empty<string[]>()) Collect(needed, set);
                }
            }

            _needed = needed;
            return needed.Count > 0;
        }

        private static void Collect(HashSet<int> needed, string[] values)
        {
            if (values == null) return;
            foreach (string value in values) Collect(needed, value);
        }

        private static void Collect(HashSet<int> needed, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x80) continue;

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    int pair = char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                    if (!IsHangul(pair)) needed.Add(pair);
                }
                else if (!IsHangul(c))
                {
                    needed.Add(c);
                }
            }
        }

        private static bool IsHangul(int codepoint) =>
            (codepoint >= 0x1100 && codepoint <= 0x11FF) ||
            (codepoint >= 0x3130 && codepoint <= 0x318F) ||
            (codepoint >= 0xAC00 && codepoint <= 0xD7A3);

        internal static void Prepare(IntPtr family)
        {
            if (_needed == null || _needed.Count == 0) return;
            if (!Memory.LooksLikeObject(family)) return;
            if (!_seenFamilies.Add(family)) return;
            if (!Resolve()) return;
            ReplaceIn(Memory.Ptr(family + _familyAssets));
            if (_familySets < 0 || _setAssets < 0) return;
            IntPtr sets = Memory.Ptr(family + _familySets);
            int count = Memory.LooksLikeObject(sets) ? Memory.I32(sets + Offsets.Runtime.ArrayLength) : 0;
            long bytes = count > 0 ? Memory.ArrayBytes(sets) : 0;
            int stride = count > 0 && bytes > 0 ? (int)(bytes / count) : 0;
            if (stride <= 0 || count > 16) return;

            for (int i = 0; i < count; i++)
                ReplaceIn(Memory.Ptr(sets + Offsets.Runtime.ArrayDataOffset + i * stride + _setAssets));
        }

        private static void ReplaceIn(IntPtr assets)
        {
            if (!Memory.LooksLikeObject(assets)) return;
            int count = Memory.I32(assets + Offsets.Runtime.ArrayLength);
            if (count <= 0 || count > 8) return;

            for (int i = 0; i < count; i++)
            {
                IntPtr asset = Memory.Ptr(assets + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(asset) || !_seenAssets.Add(asset)) continue;
                string identifier = Memory.Text(Memory.Ptr(asset + _assetIdentifier), 64);
                if (identifier == null ||
                    identifier.IndexOf("ShinGo", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                IntPtr original = Memory.Ptr(asset + _assetBytes);
                if (Covers(original, _needed)) continue;
                bool bold = identifier.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
                IntPtr array = Replacement(asset, bold);
                if (array == IntPtr.Zero) continue;
                Memory.WritePtr(asset + _assetBytes, array);
                ModLog.Load($"song text: '{identifier}' lacks characters the custom songs use; " +
                            "its bytes are replaced by the game's own face plus the gap glyphs");
            }
        }

        private static IntPtr Replacement(IntPtr asset, bool bold)
        {
            if (bold && _boldArray != IntPtr.Zero) return _boldArray;
            if (!bold && _regularArray != IntPtr.Zero) return _regularArray;
            byte[] bytes = bold ? MergedBytes(true) : MergedBytes(false);
            if (bytes == null) bytes = bold ? BoldBytes() : RegularBytes();
            if (bytes == null) return IntPtr.Zero;
            IntPtr like = Memory.Ptr(asset + _assetBytes);
            if (!Memory.LooksLikeObject(like)) return IntPtr.Zero;
            IntPtr array = SongCatalog.NewArray(like, bytes.Length);
            if (array == IntPtr.Zero) return IntPtr.Zero;

            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, (void*)(array + Offsets.Runtime.ArrayDataOffset),
                                  bytes.Length, bytes.Length);
            if (bold) _boldArray = array;
            else _regularArray = array;
            if (bold) _boldBytes = null;
            else _regularBytes = null;
            return array;
        }

        private static byte[] MergedBytes(bool bold)
        {
            byte[] cached = bold ? _mergedBold : _mergedRegular;
            if (cached != null) return cached;
            FindGameFonts();
            byte[] baseFont = bold ? _vivoBold : _vivoRegular;
            if (baseFont == null) return null;
            byte[] gap = Gap();
            if (gap == null) return null;
            byte[] merged = FontMerge.Append(baseFont, gap);
            if (merged == null || !Covers(merged, _needed))
            {
                ModLog.Warn("the game's title font could not be merged with the gap glyphs; " +
                            "the title falls back to a system font");
                return null;
            }

            ModLog.Load($"song text: using the game's vivoSans with {gap.Length} bytes of gap glyphs");
            if (bold) _mergedBold = merged;
            else _mergedRegular = merged;
            return merged;
        }

        private static byte[] Gap()
        {
            if (_gapTried) return _gap;
            _gapTried = true;

            try
            {
                using Stream raw = typeof(SongFont).Assembly
                    .GetManifestResourceStream("CustomSong.TitleGap.ttf");
                if (raw == null)
                {
                    ModLog.Warn("the embedded title gap font is missing from the assembly");
                    return null;
                }

                using var copy = new MemoryStream();
                raw.CopyTo(copy);
                _gap = copy.ToArray();
                return _gap;
            }
            catch (Exception e)
            {
                ModLog.Warn("the embedded title gap font could not be read: " + ModLog.Describe(e));
                return null;
            }
        }

        private static void FindGameFonts()
        {
            if (_gameFontsTried) return;
            _gameFontsTried = true;

            try
            {
                IntPtr klass = Class("FastTextAsset");
                if (klass == IntPtr.Zero) return;

                IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(klass));
                if (type == IntPtr.Zero) return;

                IntPtr all = MethodResolver.MethodInfoByRuntime(
                    "Resources", "FindObjectsOfTypeAll", "UnityEngine.CoreModule.dll", "UnityEngine", 1);
                Scan(Invoke(all, IntPtr.Zero, type), "loaded assets", true);
                if (!HasExact()) ScanBundles(type);
                Choose();

                ModLog.Load($"song text: game fonts found (regular={_regularName ?? "none"}, " +
                            $"bold={_boldName ?? "none"})");
            }
            catch (Exception e)
            {
                ModLog.Warn("the game's title fonts could not be found: " + ModLog.Describe(e));
            }
        }

        private static readonly List<IntPtr> _candidates = new List<IntPtr>();

        private static bool HasExact()
        {
            bool regular = false, bold = false;

            foreach (IntPtr asset in _candidates)
            {
                string identifier = Identifier(asset);
                if (identifier == "vivoSans-Regular") regular = true;
                else if (identifier == "vivoSans-DemiBold") bold = true;
            }

            return regular && bold;
        }

        private static void Choose()
        {
            foreach (IntPtr asset in _candidates)
                if (Identifier(asset) == "vivoSans-Regular")
                {
                    _vivoRegular = CopyFont(asset);
                    _regularName = "vivoSans-Regular";
                    break;
                }

            foreach (IntPtr asset in _candidates)
                if (Identifier(asset) == "vivoSans-DemiBold")
                {
                    _vivoBold = CopyFont(asset);
                    _boldName = "vivoSans-DemiBold";
                    break;
                }

            if (_vivoRegular == null)
                foreach (IntPtr asset in _candidates)
                    if (IsRegularWeight(Identifier(asset)))
                    {
                        _vivoRegular = CopyFont(asset);
                        _regularName = Identifier(asset);
                        break;
                    }

            if (_vivoBold == null)
                foreach (IntPtr asset in _candidates)
                    if (IsBoldWeight(Identifier(asset)))
                    {
                        _vivoBold = CopyFont(asset);
                        _boldName = Identifier(asset);
                        break;
                    }
        }

        private static string _regularName, _boldName;

        private static string Identifier(IntPtr asset) =>
            Memory.LooksLikeObject(asset) ? Memory.Text(Memory.Ptr(asset + _assetIdentifier), 64) : null;

        private static void ScanBundles(IntPtr type)
        {
            const string image = "UnityEngine.AssetBundleModule.dll";
            const string name = "UnityEngine";
            IntPtr all = MethodResolver.MethodInfoByRuntime("AssetBundle", "GetAllLoadedBundles", image, name, 0);
            IntPtr load = MethodResolver.MethodInfoByRuntime("AssetBundle", "LoadAllAssets", image, name, 1);
            if (all == IntPtr.Zero || load == IntPtr.Zero) return;
            IntPtr bundles = Invoke(all, IntPtr.Zero, IntPtr.Zero);
            if (!Memory.LooksLikeObject(bundles)) return;
            int count = Memory.I32(bundles + Offsets.Runtime.ArrayLength);
            if (count <= 0 || count > 256) return;

            for (int i = 0; i < count; i++)
            {
                IntPtr bundle = Memory.Ptr(bundles + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(bundle)) continue;
                Scan(Invoke(load, bundle, type), "bundle", false);
                if (HasExact()) return;
            }
        }

        private static IntPtr Invoke(IntPtr method, IntPtr instance, IntPtr argument)
        {
            if (method == IntPtr.Zero) return IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = argument;
            IntPtr raised = IntPtr.Zero;
            IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                method, instance, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                ModLog.Warn("a font asset lookup raised: " + Raised.Text(raised));
                return IntPtr.Zero;
            }

            return result;
        }

        private static void Scan(IntPtr array, string source, bool report)
        {
            if (!Memory.LooksLikeObject(array)) return;
            int count = Memory.I32(array + Offsets.Runtime.ArrayLength);
            if (count <= 0 || count > 4096) return;
            var names = report ? new List<string>(count) : null;

            for (int i = 0; i < count; i++)
            {
                IntPtr asset = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(asset)) continue;
                string identifier = Memory.Text(Memory.Ptr(asset + _assetIdentifier), 64);
                if (identifier == null) continue;
                names?.Add(identifier);

                if (identifier.IndexOf("vivoSans", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !_candidates.Contains(asset))
                    _candidates.Add(asset);
            }

            if (report)
            {
                names.RemoveAll(n => n.IndexOf("vivoSans", StringComparison.OrdinalIgnoreCase) < 0);
                ModLog.Load($"song text: {count} {source}, vivoSans among them: " +
                            (names.Count == 0 ? "none" : string.Join(", ", names.ToArray())));
            }
        }

        private static bool IsRegularWeight(string identifier)
        {
            if (identifier.IndexOf("vivoSans", StringComparison.OrdinalIgnoreCase) < 0) return false;

            return identifier.IndexOf("Regular", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("partial400", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBoldWeight(string identifier)
        {
            if (identifier.IndexOf("vivoSans", StringComparison.OrdinalIgnoreCase) < 0) return false;

            return identifier.IndexOf("DemiBold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("partial600", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static byte[] CopyFont(IntPtr asset)
        {
            IntPtr bytes = Memory.Ptr(asset + _assetBytes);
            if (!Memory.LooksLikeObject(bytes)) return null;
            int length = (int)Memory.ArrayBytes(bytes);
            if (length < 12) return null;
            var copy = new byte[length];
            fixed (byte* target = copy)
                Buffer.MemoryCopy((void*)(bytes + Offsets.Runtime.ArrayDataOffset),
                                  target, length, length);
            return copy;
        }

        private static bool Covers(byte[] bytes, HashSet<int> needed)
        {
            if (bytes == null || bytes.Length < 12) return false;

            fixed (byte* font = bytes)
                return Covers(font, bytes.Length, needed);
        }

        private static bool Covers(byte* font, int length, HashSet<int> needed)
        {
            int cmap = Table(font, length, 0x636D6170);
            if (cmap < 0) return false;
            int table = Subtable(font, length, cmap);
            if (table < 0) return false;

            foreach (int codepoint in needed)
                if (!HasCharacter(font, length, table, codepoint)) return false;
            return true;
        }

        private static byte[] RegularBytes() => _regularBytes ??= Load("msyh.ttc", "Deng.ttf", "simhei.ttf");
        private static byte[] BoldBytes() => _boldBytes ??= Load("msyhbd.ttc", "Dengb.ttf", "simhei.ttf");

        private static byte[] Load(params string[] names)
        {
            if (_noFont) return null;
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

            foreach (string name in names)
            {
                try
                {
                    string path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    byte[] bytes = File.ReadAllBytes(path);
                    if (!LooksLikeFont(bytes))
                    {
                        ModLog.Warn($"{path} is not a font file");
                        continue;
                    }

                    ModLog.Load($"song text: using {path} for characters the game's fonts lack");
                    return bytes;
                }
                catch (Exception e)
                {
                    ModLog.Warn($"{name} could not be read: {ModLog.Describe(e)}");
                }
            }

            _noFont = true;
            ModLog.Warn("no system CJK font was found; a song title with characters the game's " +
                        "fonts lack will stay blank");
            return null;
        }

        private static bool LooksLikeFont(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return false;
            uint tag = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            return tag == 0x00010000 || tag == 0x4F54544F || tag == 0x74727565 || tag == 0x74746366;
        }

        private static bool Covers(IntPtr bytes, HashSet<int> needed)
        {
            if (!Memory.LooksLikeObject(bytes)) return false;
            int length = (int)Memory.ArrayBytes(bytes);
            if (length < 12) return false;
            return Covers((byte*)(bytes + Offsets.Runtime.ArrayDataOffset), length, needed);
        }

        private static int Table(byte* font, int length, uint tag)
        {
            uint version = U32(font, 0);
            if (version != 0x00010000 && version != 0x4F54544F && version != 0x74727565) return -1;
            int count = U16(font, 4);
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (record + 16 > length) return -1;
                if (U32(font, record) == tag) return (int)U32(font, record + 8);
            }

            return -1;
        }

        private static int Subtable(byte* font, int length, int cmap)
        {
            int count = U16(font, cmap + 2);
            int best = -1;

            for (int i = 0; i < count; i++)
            {
                int record = cmap + 4 + i * 8;
                if (record + 8 > length) break;
                int platform = U16(font, record);
                int encoding = U16(font, record + 2);
                int offset = cmap + (int)U32(font, record + 4);
                if (offset + 2 > length) continue;
                if (U16(font, offset) != 4) continue;
                if (platform == 3 && encoding == 1) return offset;
                if (best < 0) best = offset;
            }

            return best;
        }

        private static bool HasCharacter(byte* font, int length, int table, int codepoint)
        {
            if (codepoint > 0xFFFF || table + 16 > length) return false;
            int segments = U16(font, table + 6) / 2;
            int ends = table + 14;
            int starts = ends + segments * 2 + 2;
            int deltas = starts + segments * 2;
            int ranges = deltas + segments * 2;
            if (segments <= 0 || ranges + segments * 2 > length) return false;

            for (int i = 0; i < segments; i++)
            {
                if (codepoint > U16(font, ends + i * 2)) continue;
                int start = U16(font, starts + i * 2);
                if (codepoint < start) return false;
                int delta = (short)U16(font, deltas + i * 2);
                int range = U16(font, ranges + i * 2);
                if (range == 0) return ((codepoint + delta) & 0xFFFF) != 0;
                int glyphAt = ranges + i * 2 + range + (codepoint - start) * 2;
                if (glyphAt + 2 > length) return false;
                return U16(font, glyphAt) != 0;
            }

            return false;
        }

        private static int U16(byte* p, int offset) => p[offset] << 8 | p[offset + 1];

        private static uint U32(byte* p, int offset) =>
            (uint)(p[offset] << 24 | p[offset + 1] << 16 | p[offset + 2] << 8 | p[offset + 3]);
    }
}

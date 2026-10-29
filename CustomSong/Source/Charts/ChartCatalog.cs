using System;
using System.Collections.Generic;
using System.IO;

namespace CustomSong
{
    internal static unsafe class ChartCatalog
    {
        private const string Extension = ".spc";

        private static readonly Dictionary<string, ChartInfo> ByBase =
            new Dictionary<string, ChartInfo>(StringComparer.Ordinal);
        internal static void Adopt(ChartInfo info) => ByBase[info.BaseName] = info;
        internal static void Reset() => ByBase.Clear();

        internal static bool TryServe(IntPtr namePtr, IntPtr result)
        {
            string name = Memory.Text(namePtr, 128);
            if (name == null) return false;
            if (!Split(name, out string baseName, out int difficulty)) return false;

            if (!ByBase.TryGetValue(baseName, out ChartInfo info))
            {
                return false;
            }

            string path = info.ChartPaths[difficulty];
            string seed = info.ChartNames[difficulty];
            bool decoded = false;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                decoded = ChartCodec.TryDecode(bytes, seed, result, out string reason);
                if (!decoded) ModLog.Error($"'{info.Name}' difficulty {difficulty}: {reason}");
            }
            catch (Exception e)
            {
                ModLog.Error($"'{info.Name}' difficulty {difficulty} could not be read: " +
                                  ModLog.Describe(e));
            }

            if (!decoded && !ChartCodec.Empty(result)) return false;
            int sourceText = SourceTextField();
            if (sourceText >= 0) *(IntPtr*)(result + sourceText) = IntPtr.Zero;
            return true;
        }

        private static int _sourceTextField = int.MinValue;

        private static int SourceTextField()
        {
            if (_sourceTextField == int.MinValue) _sourceTextField = FieldResolver.Field("_R", "_RC");
            return _sourceTextField;
        }

        private static bool Split(string name, out string baseName, out int difficulty)
        {
            baseName = null;
            difficulty = 0;
            if (!name.EndsWith(Extension, StringComparison.Ordinal)) return false;
            int end = name.Length - Extension.Length;
            if (end < 2) return false;
            char last = name[end - 1];
            if (last < '0' || last > '3') return false;
            difficulty = last - '0';
            baseName = name.Substring(0, end - 1);
            return true;
        }
    }
}

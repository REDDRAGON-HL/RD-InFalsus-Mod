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
            bool fromText = false;
            byte[] bytes = null;
            try
            {
                bytes = File.ReadAllBytes(path);
                fromText = TextChart.LooksJson(bytes) || TextChart.LooksText(bytes);
                if (!fromText) bytes = Icp1Reader.FlattenDefaultSpeeds(bytes, seed);
                decoded = ChartCodec.TryDecode(bytes, seed, result, out string reason);
                if (!decoded) ModLog.Error($"'{info.Name}' difficulty {difficulty}: {reason}");
            }
            catch (Exception e)
            {
                ModLog.Error($"'{info.Name}' difficulty {difficulty} could not be read: " +
                                  ModLog.Describe(e));
            }

            if (!decoded && !ChartCodec.Empty(result)) return false;

            if (decoded)
            {
                if (!fromText) BinaryReadback.Publish(result, seed, bytes);
                SceneControl.Reset();
                SceneDirectives.Activate(seed);
                AddShowTexts(path);
            }

            int sourceText = SourceTextField();
            if (sourceText >= 0) *(IntPtr*)(result + sourceText) = IntPtr.Zero;
            return true;
        }

        private static void AddShowTexts(string chartPath)
        {
            List<ShowTextFile.Line> lines = ShowTextFile.Load(chartPath);
            if (lines.Count == 0) return;
            var byId = new Dictionary<long, ShowTextFile.Line>();
            foreach (ShowTextFile.Line line in lines) byId[line.Id] = line;
            var rows = new List<SceneDirectives.Text>();
            foreach (SceneDirectives.Entry entry in SceneDirectives.Current)
            {
                if (entry.Kind != SceneDirectives.Kind.ShowText) continue;
                if (!byId.TryGetValue(entry.Value, out ShowTextFile.Line line)) continue;

                rows.Add(new SceneDirectives.Text
                {
                    Value = line.Text,
                    Start = entry.Time,
                    End = entry.Time + Math.Max(0, line.Duration),
                    Size = entry.Size > 0 ? entry.Size : ScaleSize(line.Size),
                    Side = 3,
                    X = Math.Clamp(line.X, 0f, 1f),
                    Y = Math.Clamp(line.Y, 0f, 1f),
                    Amp = 0f,
                    Hz = 0f,
                    Spacing = 0f,
                    FadeIn = Math.Max(0, line.FadeIn),
                    FadeOut = Math.Max(0, line.FadeOut),
                    Fill = line.Color,
                    Shadow = 0x000000A6,
                    ShadowPx = 3f,
                });
            }

            SceneDirectives.AddTexts(rows);
        }

        private static float ScaleSize(float size) => size <= 0f ? 40f : size < 16f ? size * 10f : size;
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

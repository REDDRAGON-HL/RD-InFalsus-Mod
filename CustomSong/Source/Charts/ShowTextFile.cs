using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CustomSong
{
    internal static class ShowTextFile
    {
        internal sealed class Line
        {
            internal long Id;
            internal string Text;
            internal float X = 0.5f;
            internal float Y = 0.5f;
            internal float Size;
            internal int Duration = 2000;
            internal int FadeIn = 300;
            internal int FadeOut = 300;
            internal uint Color = 0xFFFFFFFF;
        }

        internal static List<Line> Load(string chartPath)
        {
            var lines = new List<Line>();
            if (string.IsNullOrEmpty(chartPath)) return lines;
            string path = null;
            foreach (string candidate in new[] { chartPath + ".text.json", Path.ChangeExtension(chartPath, ".text.json") })
            {
                if (!File.Exists(candidate)) continue;
                path = candidate;
                break;
            }
            if (path == null) return lines;

            try
            {
                object document = Json.Parse(File.ReadAllText(path, Encoding.UTF8), out string reason);
                if (document == null)
                {
                    ModLog.WarnRelease($"'{Path.GetFileName(path)}' could not be read: {reason}");
                    return lines;
                }

                List<object> rows = Json.Array(Json.Field(Json.Object(document), "texts"));
                if (rows == null) return lines;

                foreach (object item in rows)
                {
                    var row = Json.Object(item);
                    if (row == null) continue;
                    string text = Json.Field(row, "text") as string;
                    if (string.IsNullOrEmpty(text)) continue;

                    var line = new Line
                    {
                        Id = (long)Json.Number(row, "id"),
                        Text = text,
                        X = (float)Json.Number(row, "x", 0.5),
                        Y = (float)Json.Number(row, "y", 0.5),
                        Size = (float)Json.Number(row, "size"),
                        Duration = (int)Json.Number(row, "duration", 2000),
                        FadeIn = (int)Json.Number(row, "fadeIn", 300),
                        FadeOut = (int)Json.Number(row, "fadeOut", 300),
                        Color = ParseColor(Json.Field(row, "color") as string),
                    };
                    lines.Add(line);
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"'{Path.GetFileName(path)}' could not be read: {ModLog.Describe(e)}");
            }

            return lines;
        }

        private static uint ParseColor(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0xFFFFFFFF;
            if (!TextChart.Hex(text, out uint rgba)) return 0xFFFFFFFF;
            return rgba;
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomSong
{
    internal static class FalsumSong
    {
        internal const string Extension = ".falsum";
        internal const string ProjectFileName = "song.json";

        internal static void Import(string archive)
        {
            string folder = Path.Combine(Path.GetDirectoryName(archive) ?? string.Empty,
                                         Path.GetFileNameWithoutExtension(archive));
            string ifPath = Path.Combine(folder, IfReader.InfoFileName);
            if (File.Exists(ifPath)) return;

            try
            {
                if (!File.Exists(Path.Combine(folder, ProjectFileName)))
                {
                    ZipFile.ExtractToDirectory(archive, folder, true);
                    ModLog.Load($"'{Path.GetFileName(archive)}': unpacked into '{Path.GetFileName(folder)}'");
                }

                JsonNode document = Build(folder, out string reason);
                if (document == null)
                {
                    ModLog.WarnRelease($"'{Path.GetFileName(folder)}': its {ProjectFileName} could not be " +
                                       $"used: {reason}");
                    return;
                }

                File.WriteAllText(ifPath, StudioProject.Serialize(document), new UTF8Encoding(false));
                ModLog.Load($"'{Path.GetFileName(folder)}': wrote an if from its {ProjectFileName}");
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"'{Path.GetFileName(archive)}' could not be unpacked: {ModLog.Describe(e)}");
            }
        }

        private static JsonNode Build(string folder, out string reason)
        {
            reason = null;

            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, ProjectFileName)));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || Text(root, "format") != "falsum")
            {
                reason = "it is not a falsum song";
                return null;
            }

            string audio = Named(root, "audio");
            if (string.IsNullOrEmpty(audio) || Path.GetFileName(audio) != audio ||
                !File.Exists(Path.Combine(folder, audio)))
            {
                reason = "the audio it names is not in the archive";
                return null;
            }

            var chart = new JsonArray();
            var charter = new JsonArray();
            var level = new JsonArray();
            int taken = 0;

            if (root.TryGetProperty("difficulties", out JsonElement list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                int slot = 0;

                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (slot >= ChartInfo.Difficulties) break;

                    bool available = item.ValueKind == JsonValueKind.Object &&
                                     item.TryGetProperty("available", out JsonElement flag) &&
                                     flag.ValueKind == JsonValueKind.True;
                    string file = available ? ChartAt(folder, Text(item, "chart")) : null;

                    if (file == null)
                    {
                        chart.Add(null);
                        charter.Add(null);
                        level.Add(null);
                    }
                    else
                    {
                        string designer = Text(item, "chartDesigner");
                        chart.Add(file);
                        charter.Add(string.IsNullOrWhiteSpace(designer) ? "Unknown" : designer);

                        int rating = item.TryGetProperty("rating", out JsonElement rated) &&
                                     rated.TryGetInt32(out int value)
                            ? value
                            : 1;
                        level.Add(rating);
                        taken++;
                    }

                    slot++;
                }
            }

            while (chart.Count < ChartInfo.Difficulties)
            {
                chart.Add(null);
                charter.Add(null);
                level.Add(null);
            }

            if (taken == 0)
            {
                reason = "no difficulty is marked available with a chart file in the archive";
                return null;
            }

            StudioProject.DefaultPreview(AudioCodec.Measure(Path.Combine(folder, audio), out _),
                                         out float from, out float to);

            var result = new JsonObject
            {
                ["id"] = string.IsNullOrWhiteSpace(Text(root, "songId"))
                    ? Path.GetFileName(folder)
                    : Text(root, "songId"),
                ["name"] = string.IsNullOrWhiteSpace(Text(root, "title"))
                    ? Path.GetFileName(folder)
                    : Text(root, "title"),
                ["composer"] = string.IsNullOrWhiteSpace(Text(root, "artist")) ? "Unknown" : Text(root, "artist"),
                ["song"] = audio,
                ["preview_seconds"] = new JsonArray(Rounded(from), Rounded(to)),
                ["charter"] = charter,
                ["chart"] = chart,
                ["lv"] = level,
            };
            string jacket = Picture(folder, Named(root, "cover"), IfReader.ImageExtensions);
            if (jacket != null) result["jacket"] = jacket;
            string background = Picture(folder, Text(root, "background"), IfReader.BackgroundExtensions);
            if (background != null) result["background"] = background;
            string drawn = Text(root, "jacketDesigner");
            if (!string.IsNullOrWhiteSpace(drawn)) result["illust"] = drawn;
            return result;
        }

        private static string ChartAt(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string area = Path.GetFullPath(folder);
            string source = Path.GetFullPath(Path.Combine(area, relative.Replace('\\', '/')
                                                                       .Replace('/', Path.DirectorySeparatorChar)));

            if (!source.StartsWith(area + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(source))
                return null;
            string name = Path.GetFileName(source);
            if (name.Length == 0) return null;
            string target = Path.Combine(area, name);
            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
            {
                File.Move(source, target);
                ModLog.Load($"'{Path.GetFileName(folder)}': moved '{name}' out of {Path.GetDirectoryName(relative)}");
            }

            return name;
        }

        private static string Picture(string folder, string name, string[] allowed)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name) return null;
            string extension = Path.GetExtension(name);
            bool known = false;
            foreach (string candidate in allowed)
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase)) known = true;
            if (!known) return null;
            return File.Exists(Path.Combine(folder, name)) ? name : null;
        }

        private static string Named(JsonElement item, string key)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out JsonElement token))
                return null;
            if (token.ValueKind == JsonValueKind.String) return token.GetString();
            return token.ValueKind == JsonValueKind.Object ? Text(token, "file") : null;
        }

        private static string Text(JsonElement item, string key)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out JsonElement token))
                return null;
            return token.ValueKind == JsonValueKind.String ? token.GetString() : null;
        }

        private static JsonNode Rounded(float seconds) =>
            JsonValue.Create(Math.Round(seconds, 3));
    }
}

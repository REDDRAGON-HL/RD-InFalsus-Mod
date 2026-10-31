using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomSong
{
    internal static class StudioProject
    {
        internal const string FileName = "infalsus.studio.json";
        private const string WorkFolder = ".ifstudio";
        private const int FallbackPreviewSeconds = 30;

        private static readonly JsonWriterOptions Writer = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        internal static string Serialize(JsonNode document)
        {
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, Writer))
                document.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        internal static bool EnsureIf(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            string ifPath = Path.Combine(folder, IfReader.InfoFileName);
            if (File.Exists(ifPath)) return false;
            string projectPath = Path.Combine(folder, FileName);
            if (!File.Exists(projectPath)) return false;
            string name = Path.GetFileName(folder);

            try
            {
                JsonNode document = Build(folder, projectPath, out string reason);

                if (document == null)
                {
                    ModLog.WarnRelease($"'{name}' has a Studio project but nothing could be taken from it: {reason}");
                    return false;
                }

                File.WriteAllText(ifPath, Serialize(document), new UTF8Encoding(false));
                ModLog.Load($"'{name}': wrote an if from its Studio project");
                return true;
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"'{name}': its Studio project could not be read: {ModLog.Describe(e)}");
                return false;
            }
        }

        private static JsonNode Build(string folder, string projectPath, out string reason)
        {
            reason = null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(projectPath));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || Text(root, "format") != "InFalsusStudioSong")
            {
                reason = "it is not an InFalsusStudioSong project";
                return null;
            }

            string song = Text(root, "audio");
            string songPath = Path.Combine(folder, song ?? "");
            if (string.IsNullOrEmpty(song) || !File.Exists(songPath))
            {
                reason = "the audio the project names is not in the folder";
                return null;
            }

            var chart = new JsonArray();
            var charter = new JsonArray();
            var level = new JsonArray();
            var jacket = new JsonArray();
            var illust = new JsonArray();
            string songJacket = PictureAt(folder, Text(root, "jacket"));
            int taken = 0;

            if (root.TryGetProperty("difficulties", out JsonElement list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                int slot = 0;

                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (slot >= ChartInfo.Difficulties) break;
                    bool charted = Whole(item, "rating", out int rating);
                    string file = charted
                        ? ChartOf(folder, Text(item, "working")) ?? ChartOf(folder, Text(item, "source"))
                        : null;

                    if (file == null)
                    {
                        chart.Add(null);
                        charter.Add(null);
                        level.Add(null);
                    }
                    else
                    {
                        chart.Add(file);
                        charter.Add(string.IsNullOrWhiteSpace(Text(item, "designer")) ? "Unknown" : Text(item, "designer"));
                        level.Add(rating);
                        taken++;
                    }

                    string own = Text(item, "jacket");
                    string ownPicture = PictureAt(folder, own);
                    if (!string.IsNullOrWhiteSpace(own) && ownPicture == null)
                        ModLog.WarnRelease($"'{Path.GetFileName(folder)}': the jacket '{own}' is not in the " +
                                           "folder; the song's own picture is used instead");
                    jacket.Add(ownPicture ?? songJacket);
                    string drawn = Text(item, "jacketDesigner");
                    illust.Add(string.IsNullOrEmpty(drawn) ? null : drawn);
                    slot++;
                }
            }

            while (chart.Count < ChartInfo.Difficulties)
            {
                chart.Add(null);
                charter.Add(null);
                level.Add(null);
                jacket.Add(songJacket);
                illust.Add(null);
            }

            string named = null;
            foreach (JsonNode value in jacket)
                if (value != null)
                {
                    named = value.GetValue<string>();
                    break;
                }

            if (named != null)
                for (int i = 0; i < jacket.Count; i++)
                    if (jacket[i] == null) jacket[i] = named;

            if (taken == 0)
            {
                reason = "no difficulty has a chart, a designer and a whole-number rating";
                return null;
            }

            Preview(root, songPath, out float from, out float to);

            var result = new JsonObject
            {
                ["id"] = Path.GetFileName(folder),
                ["name"] = string.IsNullOrEmpty(Text(root, "title")) ? Path.GetFileName(folder) : Text(root, "title"),
                ["composer"] = string.IsNullOrEmpty(Text(root, "artist")) ? "Unknown" : Text(root, "artist"),
                ["song"] = song,
                ["preview_seconds"] = new JsonArray(Number(from), Number(to)),
            };

            if (!AllNull(jacket))
            {
                if (Same(jacket)) result["jacket"] = jacket[0]?.GetValue<string>();
                else result["jacket"] = jacket;
            }

            result["charter"] = charter;
            result["chart"] = chart;
            result["lv"] = level;
            if (!AllNull(illust)) result["illust"] = illust;
            return result;
        }

        private static bool Same(JsonArray values)
        {
            string first = values[0]?.GetValue<string>();
            if (first == null) return false;

            foreach (JsonNode value in values)
                if (value == null || value.GetValue<string>() != first) return false;
            return true;
        }

        private static bool AllNull(JsonArray values)
        {
            foreach (JsonNode value in values)
                if (value != null) return false;
            return true;
        }

        private static JsonNode Number(float seconds) =>
            JsonValue.Create(Math.Round(seconds, 3));

        private static void Preview(JsonElement root, string songPath, out float start, out float end)
        {
            start = Seconds(root, "previewStartSeconds");
            end = Seconds(root, "previewEndSeconds");
            int length = AudioCodec.Measure(songPath, out _);
            float limit = length > 1 ? length - 1f : 0f;

            if (end > start && start < limit)
            {
                if (end > limit) end = limit;
                if (end > start + FallbackPreviewSeconds) end = start + FallbackPreviewSeconds;
                return;
            }

            DefaultPreview(length, out start, out end);
        }

        internal static void DefaultPreview(int length, out float start, out float end)
        {
            float limit = length > 1 ? length - 1f : 0f;
            start = 0f;
            end = length > 2 ? Math.Min(FallbackPreviewSeconds, limit) : Math.Max(length, 1);
        }

        private static float Seconds(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement token)) return 0f;

            if (token.ValueKind == JsonValueKind.Number && token.TryGetDouble(out double number))
                return (float)number;

            if (token.ValueKind == JsonValueKind.String &&
                float.TryParse(token.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            return 0f;
        }

        private static string ChartOf(string folder, string working)
        {
            if (string.IsNullOrEmpty(working)) return null;
            string relative = working.Replace('\\', '/');
            string source = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
            string name = Path.GetFileName(source);
            if (name.Length == 0) return null;
            string beside = Path.Combine(folder, name);
            if (!File.Exists(source)) return File.Exists(beside) ? name : null;

            if (relative.StartsWith(WorkFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                string target = beside;
                if (!File.Exists(target))
                {
                    File.Move(source, target);
                    ModLog.Load($"'{Path.GetFileName(folder)}': moved '{name}' out of {WorkFolder}");
                }

                return name;
            }

            return string.Equals(Path.GetDirectoryName(source), Path.GetFullPath(folder),
                                 StringComparison.OrdinalIgnoreCase)
                ? name
                : null;
        }

        private static string PictureAt(string folder, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string area = Path.GetFullPath(folder);
            string source = Path.GetFullPath(Path.Combine(area, name.Replace('\\', '/')
                                                                    .Replace('/', Path.DirectorySeparatorChar)));
            string bare = Path.GetFileName(source);
            if (bare.Length == 0) return null;
            string target = Path.Combine(area, bare);
            if (!File.Exists(source)) return File.Exists(target) ? bare : null;

            if (!source.StartsWith(area + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
            {
                File.Move(source, target);
                ModLog.Load($"'{Path.GetFileName(folder)}': moved '{bare}' out of " +
                            $"{Path.GetDirectoryName(name)}");
            }

            return bare;
        }

        private static string Text(JsonElement item, string key) =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty(key, out JsonElement token) &&
            token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;

        private static bool Whole(JsonElement item, string key, out int value)
        {
            value = 0;

            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out JsonElement token))
                return false;
            if (token.ValueKind == JsonValueKind.Number) return token.TryGetInt32(out value);

            return token.ValueKind == JsonValueKind.String &&
                   int.TryParse(token.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}

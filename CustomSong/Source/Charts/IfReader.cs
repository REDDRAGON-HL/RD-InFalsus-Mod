using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CustomSong
{
    internal static partial class IfReader
    {
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] BackgroundExtensions = { ".png", ".mp4" };
        internal const string InfoFileName = "if";

        private static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        private static readonly JsonElement Null = NullElement();

        private static JsonElement NullElement()
        {
            using (JsonDocument document = JsonDocument.Parse("null"))
                return document.RootElement.Clone();
        }

        internal static bool TryRead(string folder, out ChartInfo info, out string reason)
        {
            info = null;
            reason = null;
            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath))
            {
#if DEBUG
                reason = $"no {InfoFileName} file";
#endif
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(ifPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} could not be read: {ModLog.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, JsonOptions);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} is not valid JSON: {ModLog.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            using JsonDocument owned = document;
            JsonElement root = owned.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
#if DEBUG
                reason = $"{InfoFileName} is not a JSON object";
#endif
                return false;
            }

            string folderName = Path.GetFileName(folder);
            var built = new ChartInfo { Folder = folder, FolderName = folderName };
            if (!Text(root, "id", out string id, out reason)) return false;
            built.BaseName = id;
            if (!Names(root, out built.Names, out reason)) return false;
            built.Name = built.Names[0];
            if (!Composers(root, out built.Composers, out reason)) return false;
            built.Composer = built.Composers[0];
            if (!Illustrators(root, out built.Illustrators, out reason)) return false;
            if (!Songs(root, folder, out built.Songs, out reason)) return false;
            built.SongPath = built.Songs[0];
            if (!Jackets(root, folder, out built.JacketPaths, out reason)) return false;
            if (!Backgrounds(root, folder, out built.BackgroundPaths, out reason)) return false;
            if (!Previews(root, out built.Previews, out reason)) return false;
            built.PreviewStart = built.Previews[0][0];
            built.PreviewEnd = built.Previews[0][1];
            if (!DifficultyValue(root, "chart", out JsonElement charts, out reason)) return false;
            if (!DifficultyValue(root, "charter", out JsonElement charters, out reason)) return false;
            if (!DifficultyValue(root, "lv", out JsonElement levels, out reason)) return false;
            JsonElement[] chartSlots = Slots(charts);
            JsonElement[] charterSlots = Slots(charters);
            JsonElement[] levelSlots = Slots(levels);
            if (!Aligned(chartSlots, charterSlots, levelSlots, out reason)) return false;
            if (!Texts(charterSlots, "charter", out built.Charters, out reason)) return false;
            if (!Levels(levelSlots, "lv", out built.Level, out reason)) return false;
            if (!ChartFiles(chartSlots, folder, out built.ChartPaths, out built.ChartNames, out built.NoChart,
                            out reason)) return false;
            info = built;
            return true;
        }

        internal static string WhyNot(string folder)
        {
            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath)) return $"no {InfoFileName} file";

            try
            {
                using JsonDocument probe = JsonDocument.Parse(File.ReadAllText(ifPath), JsonOptions);
            }
            catch (Exception e)
            {
                return $"{InfoFileName} is not valid JSON: {e.Message}";
            }

            return $"its {InfoFileName} was read but turned down; install the Debug build for the reason";
        }

        private static bool Composers(JsonElement root, out string[] composers, out string reason)
        {
            composers = null;
            reason = null;

            if (!root.TryGetProperty("composer", out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"composer\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                string one = token.GetString();
                if (string.IsNullOrWhiteSpace(one))
                {
#if DEBUG
                    reason = "\"composer\" is empty";
#endif
                    return false;
                }

                composers = Same(one);
                return true;
            }

            return Texts(root, "composer", out composers, out reason);
        }

        private static bool Text(JsonElement root, string key, out string value, out string reason)
        {
            value = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.ValueKind != JsonValueKind.String)
            {
#if DEBUG
                reason = $"\"{key}\" is not a string";
#endif
                return false;
            }

            value = token.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
#if DEBUG
                reason = $"\"{key}\" is empty";
#endif
                return false;
            }

            return true;
        }

        private static bool LocalFile(JsonElement root, string folder, string key, string[] allowed,
                                      out string path, out string reason)
        {
            path = null;
            if (!Text(root, key, out string name, out reason)) return false;
            return LocalFileAt(folder, key, name, allowed, out path, out reason);
        }

        private static bool LocalFileAt(string folder, string key, string name, string[] allowed,
                                        out string path, out string reason)
        {
            path = null;
            reason = null;

            if (Path.GetFileName(name) != name)
            {
#if DEBUG
                reason = $"\"{key}\" names something outside the folder: {name}";
#endif
                return false;
            }

            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (Array.IndexOf(allowed, extension) < 0)
            {
#if DEBUG
                reason = $"\"{key}\" is {extension}, which the game cannot decode " +
                         $"(accepted: {string.Join(", ", allowed)})";
#endif
                return false;
            }

            path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
#if DEBUG
                reason = $"\"{key}\" points at {name}, which is not in the folder";
#endif
                path = null;
                return false;
            }

            return true;
        }

        private static bool Jackets(JsonElement root, string folder, out string[] paths, out string reason) =>
            OneOrFour(root, folder, "jacket", ImageExtensions, false, out paths, out reason);

        private static bool Backgrounds(JsonElement root, string folder, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            if (!root.TryGetProperty("background", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;
            return OneOrFour(root, folder, "background", BackgroundExtensions, true, out paths, out reason);
        }

        private static bool OneOrFour(JsonElement root, string folder, string key, string[] allowed,
                                      bool entriesMayBeNull, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!LocalFile(root, folder, key, allowed, out string single, out reason)) return false;
                var same = new string[ChartInfo.Difficulties];
                for (int i = 0; i < same.Length; i++) same[i] = single;
                paths = same;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = $"\"{key}\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null && entriesMayBeNull) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }

                if (!LocalFileAt(folder, key, item.GetString(), allowed, out result[i], out reason))
                    return false;
            }

            paths = result;
            return true;
        }

        private static bool Illustrators(JsonElement root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            if (!root.TryGetProperty("illust", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;

            if (token.ValueKind == JsonValueKind.String)
            {
                string single = token.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
#if DEBUG
                    reason = "\"illust\" is empty";
#endif
                    return false;
                }

                names = new string[ChartInfo.Difficulties];
                for (int i = 0; i < names.Length; i++) names[i] = single;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"illust\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"illust\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null)
                {
                    result[i] = string.Empty;
                    continue;
                }

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"illust\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            names = result;
            return true;
        }

        private static bool Preview(JsonElement root, out float start, out float end, out string reason)
        {
            start = 0f;
            end = 0f;
            reason = null;

            if (!root.TryGetProperty("preview_seconds", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"preview_seconds\" is missing";
#endif
                return false;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"preview_seconds\" is not a list";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != 2)
            {
#if DEBUG
                reason = $"\"preview_seconds\" has {count} entries, not 2";
#endif
                return false;
            }

            if (!Seconds(token[0], 0, out start, out reason)) return false;
            if (!Seconds(token[1], 1, out end, out reason)) return false;
            return true;
        }

        private static bool Seconds(JsonElement item, int index, out float value, out string reason)
        {
            value = 0f;
            reason = null;

            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number))
            {
                value = (float)number;
                return true;
            }

            if (item.ValueKind == JsonValueKind.String &&
                float.TryParse(item.GetString(), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out value)) return true;

#if DEBUG
            reason = $"\"preview_seconds\"[{index}] is not a number of seconds";
#endif
            return false;
        }

        private static bool DifficultyValue(JsonElement root, string key, out JsonElement token, out string reason)
        {
            token = default;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement found))
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (found.ValueKind == JsonValueKind.Array && found.GetArrayLength() != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {found.GetArrayLength()} entries, not {ChartInfo.Difficulties} "
                       + "and not one";
#endif
                return false;
            }

            token = found;
            return true;
        }

        private static JsonElement[] Slots(JsonElement token)
        {
            var slots = new JsonElement[ChartInfo.Difficulties];
            if (token.ValueKind != JsonValueKind.Array)
            {
                for (int i = 0; i < slots.Length - 1; i++) slots[i] = Null;
                slots[slots.Length - 1] = token;
                return slots;
            }

            for (int i = 0; i < slots.Length; i++) slots[i] = token[i];
            return slots;
        }

        private static bool Aligned(JsonElement[] charts, JsonElement[] charters, JsonElement[] levels,
                                    out string reason)
        {
            reason = null;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (charts[d].ValueKind == JsonValueKind.Null) continue;

                if (charters[d].ValueKind == JsonValueKind.Null || levels[d].ValueKind == JsonValueKind.Null)
                {
#if DEBUG
                    reason = $"difficulty {d} has a chart but no "
                           + (charters[d].ValueKind == JsonValueKind.Null ? "\"charter\"" : "\"lv\"");
#endif
                    return false;
                }
            }

            return true;
        }

        private static bool Texts(JsonElement[] slots, string key, out string[] values, out string reason)
        {
            values = null;
            reason = null;
            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            values = result;
            return true;
        }

        private static bool Levels(JsonElement[] slots, string key, out int?[] values, out string reason)
        {
            values = null;
            reason = null;
            var result = new int?[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (!Whole(item, out int level))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a whole number";
#endif
                    return false;
                }

                if (level <= 0)
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is {level}";
#endif
                    return false;
                }
                result[i] = level;
            }

            values = result;
            return true;
        }

        private static bool Whole(JsonElement item, out int value)
        {
            value = 0;
            if (item.ValueKind != JsonValueKind.Number) return false;
            string raw = item.GetRawText();
            if (raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0) return false;
            return item.TryGetInt32(out value);
        }

        private static bool ChartFiles(JsonElement[] slots, string folder, out string[] paths,
                                       out string[] chartNames, out bool[] noChart, out string reason)
        {
            paths = null;
            chartNames = null;
            noChart = null;
            reason = null;
            var result = new string[ChartInfo.Difficulties];
            var names = new string[ChartInfo.Difficulties];
            var absent = new bool[ChartInfo.Difficulties];

            for (int i = 0; i < ChartInfo.Difficulties; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null)
                {
                    absent[i] = true;
                    continue;
                }
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] is not a name";
#endif
                    return false;
                }

                string name = item.GetString();
                if (Path.GetFileName(name) != name)
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] names something outside the folder: {name}";
#endif
                    return false;
                }

                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] points at {name}, which is not in the folder";
#endif
                    return false;
                }

                result[i] = path;
                names[i] = name;
            }

            paths = result;
            chartNames = names;
            noChart = absent;
            return true;
        }
    }
}

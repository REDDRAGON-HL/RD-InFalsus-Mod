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
        internal static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        internal static readonly string[] BackgroundExtensions = { ".png", ".mp4" };
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
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(ifPath);
            }
            catch (Exception e)
            {
                _ = e;
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, JsonOptions);
            }
            catch (Exception e)
            {
                _ = e;
                return false;
            }

            using JsonDocument owned = document;
            JsonElement root = owned.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string folderName = Path.GetFileName(folder);
            var built = new ChartInfo { Folder = folder, FolderName = folderName };
            if (!Text(root, "id", out string id, out reason)) return false;
            built.BaseName = id;
            if (!Faces(root, "name", true, out built.Titles, out built.TitleSets, out reason)) return false;
            built.Name = built.Titles[0];

            if (!Faces(root, "composer", true, out built.ComposerLanguages, out built.ComposerSets, out reason))
                return false;
            built.Composer = built.ComposerLanguages[0];

            if (!Faces(root, "illust", false, out built.IllustratorLanguages, out built.IllustratorSets, out reason))
                return false;
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

        private static bool Faces(JsonElement root, string key, bool required,
                                  out string[] languages, out string[][] sets, out string reason)
        {
            languages = null;
            sets = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
                if (!required) return true;
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!One(token, key, out string text, out reason)) return false;
                languages = SameFive(text);
                return true;
            }

            if (token.ValueKind == JsonValueKind.Object)
                return Dictionary(token, key, required, out languages, out sets, out reason);

            if (token.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            int count = token.GetArrayLength();

            if (count == ChartPack.Languages)
            {
                if (!Nested(token, key, out sets, out reason)) return false;
                if (sets == null) return Five(token, key, required, out languages, out reason);
                languages = Base(null, sets, required, out reason);
                if (languages == null) return !required;
                Fill(sets, languages);
                return true;
            }

            if (count != ChartInfo.Difficulties)
            {
                return false;
            }

            var perDifficulty = new string[ChartInfo.Difficulties][];
            bool any = false;

            for (int i = 0; i < ChartInfo.Difficulties; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind == JsonValueKind.Array)
                {
                    return false;
                }

                if (!One(item, key, out string text, out reason)) return false;
                perDifficulty[i] = SameFive(text);
                any = true;
            }

            if (!any) return !required;
            languages = Base(null, perDifficulty, required, out reason);
            if (languages == null) return false;
            sets = perDifficulty;
            return true;
        }

        private static bool Dictionary(JsonElement token, string key, bool required,
                                       out string[] languages, out string[][] sets, out string reason)
        {
            languages = null;
            sets = null;
            reason = null;
            var slots = new string[ChartPack.Languages];
            var perDifficulty = new string[ChartInfo.Difficulties][];
            bool anyText = false;
            bool anySet = false;

            foreach (JsonProperty entry in token.EnumerateObject())
            {
                int language = ChartPack.LanguageIndex(entry.Name);
                if (language < 0)
                {
                    ModLog.WarnRelease($"\"{key}\": '{entry.Name}' is not a language " +
                                       "(sc/tc/en/ja/ko); it is ignored");
                    continue;
                }

                JsonElement value = entry.Value;

                if (value.ValueKind == JsonValueKind.String)
                {
                    if (!One(value, key, out string text, out reason)) return false;
                    slots[language] = text;
                    anyText = true;
                    continue;
                }

                if (value.ValueKind != JsonValueKind.Array ||
                    value.GetArrayLength() != ChartInfo.Difficulties)
                {
                    return false;
                }

                for (int d = 0; d < ChartInfo.Difficulties; d++)
                {
                    JsonElement item = value[d];
                    if (item.ValueKind == JsonValueKind.Null) continue;
                    if (!One(item, key, out string text, out reason)) return false;
                    perDifficulty[d] ??= new string[ChartPack.Languages];
                    perDifficulty[d][language] = text;
                    anySet = true;
                }
            }

            if (!anyText && !anySet) return !required;
            string[] baseSlots = anyText ? slots : null;
            languages = Base(baseSlots, anySet ? perDifficulty : null, required, out reason);
            if (languages == null) return !required;

            if (anySet)
            {
                Fill(perDifficulty, languages);
                sets = perDifficulty;
            }

            return true;
        }

        private static bool Nested(JsonElement token, string key, out string[][] sets, out string reason)
        {
            sets = null;
            reason = null;

            for (int language = 0; language < ChartPack.Languages; language++)
            {
                JsonElement row = token[language];
                if (row.ValueKind != JsonValueKind.Array) return true;

                if (row.GetArrayLength() != ChartInfo.Difficulties)
                {
                    return false;
                }
            }

            var perDifficulty = new string[ChartInfo.Difficulties][];
            for (int difficulty = 0; difficulty < ChartInfo.Difficulties; difficulty++)
                perDifficulty[difficulty] = new string[ChartPack.Languages];

            for (int language = 0; language < ChartPack.Languages; language++)
            {
                JsonElement row = token[language];

                for (int difficulty = 0; difficulty < ChartInfo.Difficulties; difficulty++)
                {
                    JsonElement item = row[difficulty];
                    if (item.ValueKind == JsonValueKind.Null) continue;
                    if (!One(item, key, out string text, out reason)) return false;
                    perDifficulty[difficulty][language] = text;
                }
            }

            sets = perDifficulty;
            return true;
        }

        private static string[] Base(string[] slots, string[][] sets, bool required, out string reason)
        {
            reason = null;
            var languages = new string[ChartPack.Languages];
            bool any = false;

            for (int language = 0; language < languages.Length; language++)
            {
                if (slots != null && slots[language] != null)
                {
                    languages[language] = slots[language];
                    any = true;
                    continue;
                }

                if (sets == null) continue;

                foreach (string[] set in sets)
                {
                    if (set == null || set[language] == null) continue;
                    languages[language] = set[language];
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                if (required)
                {
                }

                return null;
            }

            string first = null;
            foreach (string text in languages)
                if (text != null)
                {
                    first = text;
                    break;
                }

            for (int language = 0; language < languages.Length; language++)
                if (languages[language] == null) languages[language] = first;
            return languages;
        }

        private static void Fill(string[][] sets, string[] languages)
        {
            foreach (string[] set in sets)
            {
                if (set == null) continue;

                for (int language = 0; language < set.Length; language++)
                    if (set[language] == null) set[language] = languages[language];
            }
        }

        private static bool Five(JsonElement token, string key, bool required,
                                 out string[] values, out string reason)
        {
            values = null;
            reason = null;
            var slots = new string[ChartPack.Languages];
            for (int i = 0; i < slots.Length; i++)
            {
                JsonElement entry = token[i];
                if (entry.ValueKind == JsonValueKind.Null && !required) continue;
                if (!One(entry, key, out slots[i], out reason)) return false;
            }

            values = slots;
            return true;
        }

        private static bool One(JsonElement item, string key, out string text, out string reason)
        {
            text = null;
            reason = null;

            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return false;
            }

            text = item.GetString();
            return true;
        }

        internal static string[] SameFive(string text) => new[] { text, text, text, text, text };

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

        private static bool Text(JsonElement root, string key, out string value, out string reason)
        {
            value = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            if (token.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = token.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
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
                return false;
            }

            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (Array.IndexOf(allowed, extension) < 0)
            {
                return false;
            }

            path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
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
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null && entriesMayBeNull) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    return false;
                }

                if (!LocalFileAt(folder, key, item.GetString(), allowed, out result[i], out reason))
                    return false;
            }

            paths = result;
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
                return false;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            int count = token.GetArrayLength();
            if (count != 2)
            {
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

            return false;
        }

        private static bool DifficultyValue(JsonElement root, string key, out JsonElement token, out string reason)
        {
            token = default;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement found))
            {
                return false;
            }

            if (found.ValueKind == JsonValueKind.Array && found.GetArrayLength() != ChartInfo.Difficulties)
            {
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
                    return false;
                }

                if (level <= 0)
                {
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
                    return false;
                }

                string name = item.GetString();
                if (Path.GetFileName(name) != name)
                {
                    return false;
                }

                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
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

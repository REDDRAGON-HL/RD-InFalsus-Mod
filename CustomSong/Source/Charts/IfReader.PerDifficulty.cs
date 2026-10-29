using System.Text.Json;

namespace CustomSong
{
    internal static partial class IfReader
    {
        private static bool Names(JsonElement root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            if (!root.TryGetProperty("name", out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"name\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                string one = token.GetString();
                if (string.IsNullOrWhiteSpace(one))
                {
#if DEBUG
                    reason = "\"name\" is empty";
#endif
                    return false;
                }

                names = Same(one);
                return true;
            }

            return Texts(root, "name", out names, out reason);
        }

        private static bool Songs(JsonElement root, string folder, out string[] songs, out string reason)
        {
            songs = null;
            reason = null;

            if (!root.TryGetProperty("song", out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"song\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!LocalFileAt(folder, "song", token.GetString(), AudioExtensions, out string path, out reason))
                    return false;
                songs = Same(path);
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"song\" is neither a string nor a list";
#endif
                return false;
            }

            var slots = new string[ChartInfo.Difficulties];
            if (!FilesAcross(token, folder, "song", AudioExtensions, slots, out reason)) return false;
            songs = Carry(slots, "song", out reason);
            return songs != null;
        }

        private static bool Previews(JsonElement root, out float[][] previews, out string reason)
        {
            previews = null;
            if (!List(root, "preview_seconds", out JsonElement token, out reason)) return false;

            if (token.GetArrayLength() == 0 || token[0].ValueKind != JsonValueKind.Array)
            {
                if (!Preview(root, out float start, out float end, out reason)) return false;
                var shared = new float[ChartInfo.Difficulties][];
                for (int d = 0; d < shared.Length; d++) shared[d] = new[] { start, end };
                previews = shared;
                return true;
            }

            if (token.GetArrayLength() > ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"preview_seconds\" has {token.GetArrayLength()} entries, " +
                         $"not at most {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var slots = new float[ChartInfo.Difficulties][];
            for (int d = 0; d < token.GetArrayLength(); d++)
            {
                JsonElement item = token[d];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.GetArrayLength() != 2 ||
                    !Seconds(item[0], 0, out float start, out reason) ||
                    !Seconds(item[1], 1, out float end, out reason))
                {
#if DEBUG
                    reason = $"\"preview_seconds\"[{d}]: {reason ?? "is not a pair of seconds"}";
#endif
                    return false;
                }

                slots[d] = new[] { start, end };
            }

            if (slots[0] == null)
            {
#if DEBUG
                reason = "\"preview_seconds\"[0] is missing";
#endif
                return false;
            }

            for (int d = 1; d < slots.Length; d++) slots[d] ??= slots[0];
            previews = slots;
            return true;
        }

        private static bool List(JsonElement root, string key, out JsonElement token, out string reason)
        {
            token = default;
            reason = null;

            if (!root.TryGetProperty(key, out token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = $"\"{key}\" is not a list";
#endif
                return false;
            }

            return true;
        }

        private static bool Texts(JsonElement root, string key, out string[] texts, out string reason)
        {
            texts = null;
            if (!List(root, key, out JsonElement token, out reason)) return false;
            int count = token.GetArrayLength();
            if (count == 0 || count > ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {count} entries, not 1 to {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var slots = new string[ChartInfo.Difficulties];
            for (int d = 0; d < count; d++)
            {
                JsonElement item = token[d];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{d}] is not a name";
#endif
                    return false;
                }

                slots[d] = item.GetString();
            }

            texts = Carry(slots, key, out reason);
            return texts != null;
        }

        private static bool FilesAcross(JsonElement token, string folder, string key, string[] allowed,
                                        string[] slots, out string reason)
        {
            reason = null;
            int count = token.GetArrayLength();
            if (count == 0 || count > ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {count} entries, not 1 to {ChartInfo.Difficulties}";
#endif
                return false;
            }

            for (int d = 0; d < count; d++)
            {
                JsonElement item = token[d];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind != JsonValueKind.String)
                {
#if DEBUG
                    reason = $"\"{key}\"[{d}] is not a string";
#endif
                    return false;
                }

                if (!LocalFileAt(folder, key, item.GetString(), allowed, out string path, out reason))
                {
#if DEBUG
                    reason = $"\"{key}\"[{d}]: {reason}";
#endif
                    return false;
                }

                slots[d] = path;
            }

            return true;
        }

        private static string[] Same(string value)
        {
            var slots = new string[ChartInfo.Difficulties];
            for (int d = 0; d < slots.Length; d++) slots[d] = value;
            return slots;
        }

        private static string[] Carry(string[] slots, string key, out string reason)
        {
            reason = null;

            if (slots[0] == null)
            {
#if DEBUG
                reason = $"\"{key}\"[0] is missing";
#endif
                return null;
            }

            for (int d = 1; d < slots.Length; d++) slots[d] ??= slots[0];
            return slots;
        }
    }
}

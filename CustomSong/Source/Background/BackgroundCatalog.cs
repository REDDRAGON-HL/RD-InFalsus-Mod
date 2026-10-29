using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class BackgroundCatalog
    {
        internal struct Entry
        {
            internal IntPtr Material;
            internal string Video;
            internal bool Said => Material != IntPtr.Zero || Video != null;
        }

        private static readonly Dictionary<string, Entry[]> ByBase =
            new Dictionary<string, Entry[]>(StringComparer.Ordinal);
        internal static int Count => ByBase.Count;
        internal static void Reset() => ByBase.Clear();

        internal static bool Add(ChartInfo info, out string reason)
        {
            reason = null;
            if (info.BackgroundPaths == null) return true;
            var entries = new Entry[ChartInfo.Difficulties];
            bool all = true;

            for (int d = 0; d < entries.Length; d++)
            {
                string path = info.BackgroundPaths[d];
                if (path == null) continue;

                if (IsVideo(path))
                {
                    entries[d].Video = path;
                    continue;
                }

                IntPtr material = JacketFactory.Build(path, "background", out string why);
                if (material == IntPtr.Zero)
                {
                    all = false;
#if DEBUG
                    if (reason == null) reason = $"difficulty {d}: {why}";
#else
                    _ = why;
#endif
                    continue;
                }

                entries[d].Material = material;
            }

            ByBase[info.BaseName] = entries;
            return all;
        }

        internal static bool Find(IntPtr songInfo, int difficulty, out Entry entry, out string why)
        {
            entry = default;
            why = null;
            string baseName = JacketCatalog.NameOf(songInfo);
            if (baseName == null) return false;
            if (!ByBase.TryGetValue(baseName, out Entry[] entries)) return false;

            if (difficulty >= 0 && difficulty < entries.Length)
            {
                entry = entries[difficulty];
            }
            else
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (i == 0) entry = entries[0];
                    else if (entries[i].Material != entry.Material || entries[i].Video != entry.Video)
                    {
#if DEBUG
                        why = "a background is set per difficulty and the selected one could not be read";
#endif
                        return false;
                    }
                }

                if (!entry.Said) return false;
            }

            if (!entry.Said) return false;
            if (entry.Video == null) return true;
            IntPtr material = VideoBackground.Material(entry.Video, out why);
            if (material == IntPtr.Zero) return false;
            entry.Material = material;
            return true;
        }

        private static bool IsVideo(string path) =>
            string.Equals(System.IO.Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase);
    }
}

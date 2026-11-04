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
            internal string Still;
            internal bool Said => Material != IntPtr.Zero || Video != null || Still != null;
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

            for (int d = 0; d < entries.Length; d++)
            {
                string path = info.BackgroundPaths[d];
                if (path == null) continue;
                if (IsVideo(path)) entries[d].Video = path;
                else entries[d].Still = path;
            }

            ByBase[info.BaseName] = entries;
            return true;
        }

        internal static bool Find(IntPtr songInfo, int difficulty, out Entry entry, out string why)
        {
            entry = default;
            why = null;
            string baseName = JacketCatalog.NameOf(songInfo);
            if (baseName == null) return false;
            if (!ByBase.TryGetValue(baseName, out Entry[] entries)) return false;
            int chosen;
            if (difficulty >= 0 && difficulty < entries.Length)
            {
                chosen = difficulty;
            }
            else
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (i == 0) continue;
                    if (entries[i].Material != entries[0].Material ||
                        entries[i].Video != entries[0].Video ||
                        entries[i].Still != entries[0].Still)
                    {
                        return false;
                    }
                }

                chosen = 0;
            }

            Entry found = entries[chosen];
            if (!found.Said) return false;

            if (found.Video != null)
            {
                IntPtr material = VideoBackground.Material(found.Video, out why);
                if (material == IntPtr.Zero) return false;
                found.Material = material;
                found.Video = null;
            }
            else if (found.Material == IntPtr.Zero && found.Still != null)
            {
                string path = found.Still;
                found.Still = null;
                IntPtr material = JacketFactory.Build(path, "background", out why);
                if (material == IntPtr.Zero)
                {
                    entries[chosen] = found;
                    return false;
                }

                found.Material = material;
            }

            entries[chosen] = found;
            entry = found;
            return true;
        }

        private static bool IsVideo(string path) =>
            string.Equals(System.IO.Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase);
    }
}

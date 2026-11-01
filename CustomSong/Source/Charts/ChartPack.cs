using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal sealed class ChartPack
    {
        internal string Folder;
        internal string Name;
        internal string Slug;
        internal string[] Names;

        internal readonly Dictionary<string, string[]> Visuals =
            new Dictionary<string, string[]>(StringComparer.Ordinal);

        internal string Visual(string key, int language)
        {
            if (!Visuals.TryGetValue(key, out string[] slots)) return null;
            if (language >= 0 && language < slots.Length && slots[language] != null) return slots[language];
            if (slots[FallbackLanguage] != null) return slots[FallbackLanguage];

            foreach (string slot in slots)
                if (slot != null) return slot;
            return null;
        }

        internal const int Languages = 5;
        internal const int FallbackLanguage = 2;

        internal static int LanguageIndex(string key)
        {
            if (key == null) return -1;

            switch (key.Trim().ToLowerInvariant())
            {
                case "sc": return 0;
                case "tc": return 1;
                case "en": return 2;
                case "ja": return 3;
                case "ko": return 4;
                default: return -1;
            }
        }

        internal readonly List<ChartInfo> Charts = new List<ChartInfo>();
        internal int PackId = -1;

        internal static readonly string[] PictureKeys =
        {
            "songSelect",
            "songSelectHover",
            "selected",
            "selectedHover",
            "deselected",
            "deselectedHover",
            "outline",
        };
    }
}

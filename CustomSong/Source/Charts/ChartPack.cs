using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal sealed class ChartPack
    {
        internal string Folder;
        internal string Name;
        internal string Slug;
        internal readonly Dictionary<string, string> Visuals = new Dictionary<string, string>(StringComparer.Ordinal);
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

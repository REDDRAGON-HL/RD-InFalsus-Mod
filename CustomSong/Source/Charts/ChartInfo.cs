using System;

namespace CustomSong
{
    internal sealed class ChartInfo
    {
        internal const int Difficulties = 4;
        internal string Folder;
        internal string Name;
        internal string[] Titles;
        internal string[][] TitleSets;
        internal string Composer;
        internal string[] ComposerLanguages;
        internal string[][] ComposerSets;
        internal string[] IllustratorLanguages;
        internal string[][] IllustratorSets;
        internal string SongPath;
        internal string[] Songs;
        internal string[] JacketPaths;
        internal string[] BackgroundPaths;
        internal float PreviewStart;
        internal float PreviewEnd;
        internal float[][] Previews;
        internal string[] ChartPaths;
        internal string[] ChartNames;
        internal bool[] NoChart;
        internal string[] Charters;
        internal int?[] Level;
        internal string FolderName;
        internal string BaseName;
    }
}

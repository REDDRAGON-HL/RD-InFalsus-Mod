using System;

namespace CustomSong
{
    internal static class Offsets
    {
        internal static class Runtime
        {
            internal const int ArrayDataOffset = 0x20;
            internal const int ArrayLength = 0x18;
            internal const int ListItems = 0x10;
            internal const int ListSize = 0x18;
            internal const int DictCount = 0x20;
            internal const int HashSetCount = 0x20;
            internal const int ClassStatics = 0xB8;
            internal const int ObjectHeader = 0x10;
            internal const int BoxedData = 0x10;
            internal const int SpanSize = 0x10;
            internal const int SpanLength = 8;
            internal const int ChartTupleNotes = 0x10;

            internal static class ReadRecord
            {
                internal const int Info = 0;
                internal const int Handle = 8;
                internal const int Size = 40;
                internal const int IndexMask = 0xFFFFFF;
                internal const int Offset = 8;
                internal const int Length = 12;
                internal const int Buffer = 32;
                internal const int BytesRead = 40;
            }

            internal static class Coroutine
            {
                internal const int State = 0x10;
                internal const int Current = 0x18;
            }

            internal const int BitArrayArray = 0x10;
            internal const int BitArrayLength = 0x18;
        }

        internal static class Note
        {
            internal static int Side = Unresolved;
            internal static int Type = Unresolved;
            internal static int StartMs = Unresolved;
            internal static int EndMs = Unresolved;
        }

        internal const int Unresolved = -1;

        internal static class SongData
        {
            internal static int AllSongs = Unresolved;
            internal static int NameIndex = Unresolved;
            internal static int Availability = Unresolved;
        }

        internal static class PackData
        {
            internal static int Packs = Unresolved;
        }

        internal static class Song
        {
            internal static int Size = Unresolved;
            internal static int Id = Unresolved;
            internal static int BaseName = Unresolved;
            internal static int Charts = Unresolved;
            internal static int PreviewStart = Unresolved;
            internal static int PreviewEnd = Unresolved;
            internal static int TitleReading = Unresolved;
            internal static int ArtistReading = Unresolved;
            internal static int RewardStyle = Unresolved;
        }

        internal static class Chart
        {
            internal static int Size = Unresolved;
            internal static int Id = Unresolved;
            internal static int Available = Unresolved;
            internal static int Difficulty = Unresolved;
            internal static int Designer = Unresolved;
            internal static int JacketDesigner = Unresolved;
            internal static int Rating = Unresolved;
            internal static int Section = Unresolved;
        }

        internal static class Pack
        {
            internal static int Size = Unresolved;
            internal static int Id = Unresolved;
            internal static int Slug = Unresolved;
            internal static int Songs = Unresolved;
        }

        private static bool _resolved;
        private static bool _failed;
        internal static bool Ready => _resolved;

        internal static bool Resolve(IntPtr songData, IntPtr packData)
        {
            if (_failed) return false;
            if (_resolved) return true;
            bool ok = true;
            ok &= (SongData.AllSongs = FieldResolver.Field("SongData", "allSongInfo")) >= 0;
            ok &= (SongData.NameIndex = FieldResolver.Field("SongData", "_Efb")) >= 0;
            ok &= (SongData.Availability = FieldResolver.Field("SongData", "_Ffb")) >= 0;
            ok &= (PackData.Packs = FieldResolver.Field("PackData", "PackInfo")) >= 0;
            ok &= (Song.Id = FieldResolver.Field("SongInfo", "Id")) >= 0;
            ok &= (Song.BaseName = FieldResolver.Field("SongInfo", "BaseName")) >= 0;
            ok &= (Song.Charts = FieldResolver.Field("SongInfo", "ChartInfos")) >= 0;
            ok &= (Song.PreviewStart = FieldResolver.Field("SongInfo", "PreviewStartSeconds")) >= 0;
            ok &= (Song.PreviewEnd = FieldResolver.Field("SongInfo", "PreviewEndSeconds")) >= 0;
            ok &= (Song.TitleReading = FieldResolver.Field("SongInfo", "LocalizationToTitleReadingOverride")) >= 0;
            ok &= (Song.ArtistReading = FieldResolver.Field("SongInfo", "ArtistReadingOverride")) >= 0;
            ok &= (Song.RewardStyle = FieldResolver.Field("SongInfo", "RewardStyle")) >= 0;
            ok &= (Chart.Id = FieldResolver.Field("SongChartInfo", "Id")) >= 0;
            ok &= (Chart.Available = FieldResolver.Field("SongChartInfo", "Available")) >= 0;
            ok &= (Chart.Difficulty = FieldResolver.Field("SongChartInfo", "Difficulty")) >= 0;
            ok &= (Chart.Designer = FieldResolver.Field("SongChartInfo", "DisplayChartDesigner")) >= 0;
            ok &= (Chart.JacketDesigner = FieldResolver.Field("SongChartInfo", "DisplayJacketDesigner")) >= 0;
            ok &= (Chart.Rating = FieldResolver.Field("SongChartInfo", "Rating")) >= 0;
            ok &= (Chart.Section = FieldResolver.Field("SongChartInfo", "LevelSectionIndicator")) >= 0;
            ok &= (Pack.Id = FieldResolver.Field("PackInfo", "Id")) >= 0;
            ok &= (Pack.Slug = FieldResolver.Field("PackInfo", "Slug")) >= 0;
            ok &= (Pack.Songs = FieldResolver.Field("PackInfo", "SongIds")) >= 0;

            if (ok)
            {
                IntPtr songs = Memory.Ptr(songData + SongData.AllSongs);
                Song.Size = FieldResolver.ElementSize(songs, "SongInfo.Size");
                ok &= Song.Size > 0;
                Chart.Size = FieldResolver.ElementSize(ChartTemplate(songs), "SongChartInfo.Size");
                ok &= Chart.Size > 0;
                Pack.Size = FieldResolver.ElementSize(Memory.Ptr(packData + PackData.Packs), "PackInfo.Size");
                ok &= Pack.Size > 0;
            }

            if (!ok)
            {
                _failed = true;
                ModLog.Error("the game's song and pack tables could not be read by name in this build; " +
                                  "no custom song will be registered");
                return false;
            }

            _resolved = true;
            return true;
        }

        private static IntPtr ChartTemplate(IntPtr songs)
        {
            if (!Memory.LooksLikeObject(songs)) return IntPtr.Zero;
            int count = Memory.I32(songs + Runtime.ArrayLength);
            long bytes = Memory.ArrayBytes(songs);
            int stride = Song.Size;

            for (int i = 0; i < count && stride > 0 && (long)(i + 1) * stride <= bytes; i++)
            {
                IntPtr charts = Memory.Ptr(songs + Runtime.ArrayDataOffset + i * stride + Song.Charts);
                if (Memory.LooksLikeObject(charts) && Memory.I32(charts + Runtime.ArrayLength) > 0)
                    return charts;
            }

            return IntPtr.Zero;
        }
    }
}

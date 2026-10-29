using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static unsafe class SongCatalog
    {
        private static int SongDataAllSongs => Offsets.SongData.AllSongs;
        private static int SongDataAvailability => Offsets.SongData.Availability;
        private const int BitArrayArray = Offsets.Runtime.BitArrayArray;
        private const int BitArrayLength = Offsets.Runtime.BitArrayLength;

        private static bool GrowAvailability(IntPtr songData, int count)
        {
            IntPtr bits = Memory.Ptr(songData + SongDataAvailability);
            if (!Memory.LooksLikeObject(bits))
            {
                ModLog.Warn("SongData has no availability bits");
                return false;
            }

            int oldLength = Memory.I32(bits + BitArrayLength);
            if (oldLength >= count) return true;
            int oldWords = (oldLength + 31) / 32;
            int newWords = (count + 31) / 32;
            IntPtr words = Memory.Ptr(bits + BitArrayArray);
            if (!Memory.LooksLikeObject(words))
            {
                ModLog.Warn($"the availability bits have no backing array (0x{words.ToInt64():x})");
                return false;
            }

            IntPtr grown = NewArray(words, newWords);
            if (grown == IntPtr.Zero)
            {
                ModLog.Warn($"no room for {newWords} availability word(s)");
                return false;
            }

            long bytes = (long)Memory.I32(words + Offsets.Runtime.ArrayLength) * sizeof(int);
            Buffer.MemoryCopy((void*)(words + Offsets.Runtime.ArrayDataOffset),
                              (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);

            for (int i = 0; i < newWords; i++)
            {
                int mask = i < oldWords ? Memory.I32(words + Offsets.Runtime.ArrayDataOffset + i * sizeof(int))
                                        : 0;
                for (int b = 0; b < 32; b++)
                {
                    int index = i * 32 + b;
                    if (index < oldLength || index >= count) continue;
                    mask |= 1 << b;
                }
                Memory.WriteI32(grown + Offsets.Runtime.ArrayDataOffset + i * sizeof(int), mask);
            }

            Memory.WritePtr(bits + BitArrayArray, grown);
            Memory.WriteI32(bits + BitArrayLength, count);
            return true;
        }
        private static int SongDataNameIndex => Offsets.SongData.NameIndex;
        private const string FilteredPrefix = "tutorial";
        private const int LocaleLimit = 64;
        private static int PackDataPacks => Offsets.PackData.Packs;
        private static int SongInfoSize => Offsets.Song.Size;
        private static int SongInfoId => Offsets.Song.Id;
        private static int SongInfoBaseName => Offsets.Song.BaseName;
        private static int SongInfoCharts => Offsets.Song.Charts;
        private static int SongInfoPreviewStart => Offsets.Song.PreviewStart;
        private static int SongInfoPreviewEnd => Offsets.Song.PreviewEnd;
        private static int SongInfoTitleReading => Offsets.Song.TitleReading;
        private static int SongInfoArtistReading => Offsets.Song.ArtistReading;
        private static int SongInfoRewardStyle => Offsets.Song.RewardStyle;
        private const int RewardStyleSupress = 0;
        private static int ChartInfoSize => Offsets.Chart.Size;
        private static int ChartInfoId => Offsets.Chart.Id;
        private static int ChartInfoAvailable => Offsets.Chart.Available;
        private static int ChartInfoDifficulty => Offsets.Chart.Difficulty;
        private static int ChartInfoDesigner => Offsets.Chart.Designer;
        private static int ChartInfoJacketDesigner => Offsets.Chart.JacketDesigner;
        private static int ChartInfoRating => Offsets.Chart.Rating;
        private static int ChartInfoSection => Offsets.Chart.Section;
        private static int PackInfoSize => Offsets.Pack.Size;
        private static int PackInfoId => Offsets.Pack.Id;
        private static int PackInfoSlug => Offsets.Pack.Slug;
        private static int PackInfoSongs => Offsets.Pack.Songs;
        internal static int CustomPackId = -1;
        internal static readonly List<int> CustomPackIds = new List<int>();
        internal static bool IsCustomPack(int packId) => CustomPackIds.Contains(packId);
        internal const int CustomPackIndex = 1;
        private static byte[] _baseSongs;
        private static byte[] _basePacks;
        private static byte[] _baseBits;
        private static int _baseSongCount;
        private static int _basePackCount;
        private static int _baseBitLength;
        private static bool _baseline;
        internal static readonly HashSet<ushort> CustomSongIds = new HashSet<ushort>();

        internal static readonly Dictionary<ushort, ChartInfo> ById =
            new Dictionary<ushort, ChartInfo>();
        private static readonly Dictionary<ushort, int> IllustratorShown = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, int> TitleShown = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, int> ArtistShown = new Dictionary<ushort, int>();
        private static readonly Dictionary<ushort, int> PreviewShown = new Dictionary<ushort, int>();
        internal const string PackName = "Custom Song";
        private static readonly byte[] DifficultyFlags = { 1, 2, 4, 8 };

        internal static int DifficultyIndexOf(byte flag)
        {
            for (int i = 0; i < DifficultyFlags.Length; i++)
                if (DifficultyFlags[i] == flag) return i;
            return -1;
        }

        private static int _songDataField = int.MinValue;
        private static int _packDataField = int.MinValue;
        private static bool _unusable;
        internal static bool Unusable => _unusable;

        internal static bool TryGetAssets(out IntPtr songData, out IntPtr packData)
        {
            songData = IntPtr.Zero;
            packData = IntPtr.Zero;
            if (_unusable) return false;
            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;
            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            if (_songDataField == int.MinValue)
            {
                _songDataField = FieldResolver.Field("DataAccess", "_JAb");
                _packDataField = FieldResolver.Field("DataAccess", "_lAb");
            }

            if (_songDataField < 0 || _packDataField < 0)
            {
                _unusable = true;
                ModLog.Error("the game's data assets could not be found by name in this build; " +
                                  "no custom song will be registered");
                return false;
            }

            songData = Memory.Ptr(statics + _songDataField);
            packData = Memory.Ptr(statics + _packDataField);
            if (!Memory.LooksLikeObject(songData) || !Memory.LooksLikeObject(packData)) return false;

            if (!Offsets.Resolve(songData, packData))
            {
                _unusable = true;
                return false;
            }

            return true;
        }

        internal static List<ChartInfo> SettleNames(List<ChartInfo> charts, IntPtr songData)
        {
            var admitted = new List<ChartInfo>(charts.Count);
            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            int count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;
            var taken = new HashSet<string>(StringComparer.Ordinal);
            const int SlotsToCheck = 4;
            int stride = SongInfoSize;
            long byteLength = Memory.ArrayBytes(songs);

            for (int i = 0; i < count && byteLength > 0 && (long)(i + 1) * stride <= byteLength; i++)
            {
                IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + i * stride;
                ushort id = Memory.U16(slot + SongInfoId);
                if (i < SlotsToCheck && id != 0 && id != (ushort)i)
                {
                    ModLog.Error($"the song table does not index the way this build expects " +
                                      $"(slot {i} carries id {id}, not {i}); no custom song was " +
                                      "registered, because their names cannot be checked against the game's");
                    return admitted;
                }

                string text = Memory.Text(Memory.Ptr(slot + SongInfoBaseName));
                if (text != null) taken.Add(text);
            }

            foreach (ChartInfo chart in charts)
            {
                if (taken.Contains(chart.BaseName))
                {
                    ModLog.WarnRelease($"'{chart.BaseName}' is already the name of a song this game " +
                                            $"has, so '{chart.FolderName}' was not loaded. Change the id " +
                                            "in its if file.");
                    continue;
                }

                taken.Add(chart.BaseName);
                admitted.Add(chart);
                ChartCatalog.Adopt(chart);
            }

            return admitted;
        }

        internal static void CaptureBaseline(IntPtr songData, IntPtr packData)
        {
            if (_baseline) return;
            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            _baseSongCount = Memory.I32(songs + Offsets.Runtime.ArrayLength);
            _baseSongs = CopyArray(songs, _baseSongCount, ElementStride(songs, _baseSongCount));
            IntPtr packs = Memory.Ptr(packData + PackDataPacks);
            _basePackCount = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            _basePacks = CopyArray(packs, _basePackCount, ElementStride(packs, _basePackCount));
            IntPtr bits = Memory.Ptr(songData + SongDataAvailability);
            _baseBitLength = Memory.I32(bits + BitArrayLength);
            IntPtr words = Memory.Ptr(bits + BitArrayArray);
            _baseBits = CopyArray(words, (_baseBitLength + 31) / 32, sizeof(int));
            _baseline = true;
        }

        internal static void RestoreBaseline(IntPtr songData, IntPtr packData)
        {
            if (!_baseline) return;
            IntPtr currentSongs = Memory.Ptr(songData + SongDataAllSongs);
            IntPtr songs = NewArray(currentSongs, _baseSongCount);
            CopyBytes(songs, _baseSongs);
            Memory.WritePtr(songData + SongDataAllSongs, songs);
            IntPtr currentPacks = Memory.Ptr(packData + PackDataPacks);
            IntPtr packs = NewArray(currentPacks, _basePackCount);
            CopyBytes(packs, _basePacks);
            Memory.WritePtr(packData + PackDataPacks, packs);
            IntPtr bits = Memory.Ptr(songData + SongDataAvailability);
            IntPtr currentWords = Memory.Ptr(bits + BitArrayArray);
            IntPtr words = NewArray(currentWords, _baseBits.Length / sizeof(int));
            CopyBytes(words, _baseBits);
            Memory.WritePtr(bits + BitArrayArray, words);
            Memory.WriteI32(bits + BitArrayLength, _baseBitLength);
            ResetCustom();
        }

        internal static void ResetCustom()
        {
            CustomPackId = -1;
            CustomPackIds.Clear();
            CustomSongIds.Clear();
            ById.Clear();
            IllustratorShown.Clear();
            TitleShown.Clear();
            ArtistShown.Clear();
            PreviewShown.Clear();
        }

        private static byte[] CopyArray(IntPtr array, int count, int stride)
        {
            if (count <= 0 || stride <= 0) return Array.Empty<byte>();
            var bytes = new byte[count * stride];
            fixed (byte* target = bytes)
                Buffer.MemoryCopy((void*)(array + Offsets.Runtime.ArrayDataOffset),
                                  target, bytes.Length, bytes.Length);
            return bytes;
        }

        private static void CopyBytes(IntPtr array, byte[] bytes)
        {
            if (bytes.Length == 0) return;
            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, (void*)(array + Offsets.Runtime.ArrayDataOffset),
                                  bytes.Length, bytes.Length);
        }

        internal static List<ChartPack> SettlePacks(List<ChartPack> packs, IntPtr songData)
        {
            var admitted = new List<ChartPack>(packs.Count);
            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            int count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;
            var taken = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + i * SongInfoSize;
                string text = Memory.Text(Memory.Ptr(slot + SongInfoBaseName));
                if (text != null) taken.Add(text);
            }

            foreach (ChartPack pack in packs)
            {
                var kept = new List<ChartInfo>(pack.Charts.Count);
                foreach (ChartInfo chart in pack.Charts)
                {
                    if (taken.Contains(chart.BaseName))
                    {
                        ModLog.WarnRelease($"'{chart.BaseName}' is already the name of a song this game has, " +
                                           $"so '{chart.FolderName}' was not loaded. Change the id in its if file.");
                        continue;
                    }

                    taken.Add(chart.BaseName);
                    kept.Add(chart);
                    ChartCatalog.Adopt(chart);
                }

                if (kept.Count == 0) continue;
                pack.Charts.Clear();
                pack.Charts.AddRange(kept);
                admitted.Add(pack);
            }

            return admitted;
        }

        internal static int InjectPacks(IntPtr songData, IntPtr packData, List<ChartPack> packs)
        {
            int total = 0;
            foreach (ChartPack pack in packs) total += pack.Charts.Count;
            if (total == 0) return 0;
            IntPtr oldSongs = Memory.Ptr(songData + SongDataAllSongs);
            if (!Memory.LooksLikeObject(oldSongs)) { ModLog.Warn("SongData has no song array"); return 0; }

            int oldCount = Memory.I32(oldSongs + Offsets.Runtime.ArrayLength);
            if (oldCount <= 0) { ModLog.Warn("SongData has no songs to take a template from"); return 0; }

            int newCount = oldCount + total;
            if (!GrowAvailability(songData, newCount)) return 0;
            IntPtr songs = GrowArray(oldSongs, newCount);
            if (songs == IntPtr.Zero) return 0;
            IntPtr chartTemplate = FirstCharts(oldSongs, oldCount);
            if (chartTemplate == IntPtr.Zero) return 0;
            IntPtr titleTemplate = FirstReadings(oldSongs, oldCount);
            var packIds = new List<IntPtr>(packs.Count);
            int running = 0;

            foreach (ChartPack pack in packs)
            {
                IntPtr songIds = AllocateSongIds(packData, pack.Charts.Count);
                if (songIds == IntPtr.Zero) return 0;
                packIds.Add(songIds);

                for (int i = 0; i < pack.Charts.Count; i++, running++)
                {
                    ChartInfo chart = pack.Charts[i];
                    int id = oldCount + running;
                    CustomSongIds.Add((ushort)id);
                    ById[(ushort)id] = chart;
                    IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + id * SongInfoSize;
                    Buffer.MemoryCopy((void*)(oldSongs + Offsets.Runtime.ArrayDataOffset), (void*)slot,
                                      SongInfoSize, SongInfoSize);
                    Memory.WritePtr(slot + SongInfoTitleReading, BuildReadings(titleTemplate, chart.Name));
                    Memory.WritePtr(slot + SongInfoArtistReading, Str(chart.Composer));
                    Memory.WriteU16(slot + SongInfoId, (ushort)id);
                    Memory.WritePtr(slot + SongInfoBaseName, Str(chart.BaseName));
                    Memory.WriteF32(slot + SongInfoPreviewStart, chart.PreviewStart);
                    Memory.WriteF32(slot + SongInfoPreviewEnd, chart.PreviewEnd);
                    Memory.WriteI32(slot + SongInfoRewardStyle, RewardStyleSupress);
                    IntPtr chartInfos = BuildCharts(chartTemplate, chart);
                    Memory.WritePtr(slot + SongInfoCharts, chartInfos);
                    JacketCatalog.RegisterCharts(chartInfos, chart);
                    WriteSongId(songIds, i, (ushort)id);
                }
            }

            Memory.WritePtr(songData + SongDataAllSongs, songs);
            running = 0;
            foreach (ChartPack pack in packs)
            {
                foreach (ChartInfo chart in pack.Charts)
                {
                    int id = oldCount + running++;
                    PackText.SetSongText(PackText.SongText.Title, id, chart.Name);
                    PackText.SetSongText(PackText.SongText.Artist, id, chart.Composer);
                    PackText.SetSongText(PackText.SongText.Illustrator, id, IllustratorOf(chart, 0));
                }
            }

            Refresh(songData);
            AddPacks(packData, packs, packIds);
            return total;
        }

        private static bool AddPacks(IntPtr packData, List<ChartPack> packs, List<IntPtr> songIds)
        {
            IntPtr oldPacks = Memory.Ptr(packData + PackDataPacks);
            if (!Memory.LooksLikeObject(oldPacks)) { ModLog.Warn("PackData has no pack array"); return false; }

            int oldCount = Memory.I32(oldPacks + Offsets.Runtime.ArrayLength);
            if (oldCount <= CustomPackIndex)
            {
                ModLog.Warn($"the pack list has {oldCount} entries; there is no room before the first pack");
                return false;
            }

            int nextId = NextPackId(oldPacks, oldCount);
            IntPtr packsArray = NewArray(oldPacks, oldCount + packs.Count);
            if (packsArray == IntPtr.Zero) return false;
            IntPtr source = oldPacks + Offsets.Runtime.ArrayDataOffset;
            IntPtr data = packsArray + Offsets.Runtime.ArrayDataOffset;
            long head = (long)CustomPackIndex * PackInfoSize;
            Buffer.MemoryCopy((void*)source, (void*)data, head, head);

            for (int i = 0; i < packs.Count; i++)
            {
                IntPtr mine = data + (CustomPackIndex + i) * PackInfoSize;
                int packId = nextId + i;
                Memory.WriteU16(mine + PackInfoId, (ushort)packId);
                Memory.WritePtr(mine + PackInfoSlug, Str(packs[i].Slug));
                Memory.WritePtr(mine + PackInfoSongs, songIds[i]);
                packs[i].PackId = packId;
                CustomPackIds.Add(packId);
            }

            long rest = (long)(oldCount - CustomPackIndex) * PackInfoSize;
            Buffer.MemoryCopy((void*)(source + (int)head),
                              (void*)(data + (CustomPackIndex + packs.Count) * PackInfoSize), rest, rest);
            Memory.WritePtr(packData + PackDataPacks, packsArray);
            for (int i = 0; i < oldCount + packs.Count && i < 14; i++)
            {
                IntPtr slot = packsArray + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize;
                int id = Memory.U16(slot + PackInfoId);
                string slug = Memory.Text(Memory.Ptr(slot + PackInfoSlug));
            }

            return true;
        }

        private static string IllustratorOf(ChartInfo info, int difficulty) =>
            info.Illustrators == null ? string.Empty : info.Illustrators[difficulty];

        private static bool VariesByDifficulty(ChartInfo info)
        {
            string[] names = info.Illustrators;
            if (names == null) return false;

            for (int i = 1; i < names.Length; i++)
                if (names[i] != names[0]) return true;
            return false;
        }

        internal static void ShowForDifficulty(ushort songId, byte difficultyFlag)
        {
            int difficulty = DifficultyIndexOf(difficultyFlag);
            if (difficulty < 0) return;

            foreach (KeyValuePair<ushort, ChartInfo> entry in ById)
            {
                ShowTitle(entry.Key, entry.Value, difficulty);
                ShowArtist(entry.Key, entry.Value, difficulty);
                ShowIllustrator(entry.Key, entry.Value, difficulty);
            }

            if (ById.TryGetValue(songId, out ChartInfo info)) ShowPreview(songId, info, difficulty);
        }

        private static void ShowTitle(ushort songId, ChartInfo info, int difficulty)
        {
            if (info.Names == null) return;
            string title = info.Names[difficulty];
            if (title == null) return;
            if (TitleShown.TryGetValue(songId, out int shown) && shown == difficulty) return;

            if (PackText.SetSongText(PackText.SongText.Title, songId, title))
                TitleShown[songId] = difficulty;
        }

        private static void ShowArtist(ushort songId, ChartInfo info, int difficulty)
        {
            if (info.Composers == null) return;
            string artist = info.Composers[difficulty];
            if (artist == null) return;
            if (ArtistShown.TryGetValue(songId, out int shown) && shown == difficulty) return;

            if (PackText.SetSongText(PackText.SongText.Artist, songId, artist))
                ArtistShown[songId] = difficulty;
        }

        private static void ShowIllustrator(ushort songId, ChartInfo info, int difficulty)
        {
            if (!VariesByDifficulty(info)) return;
            if (IllustratorShown.TryGetValue(songId, out int shown) && shown == difficulty) return;

            if (PackText.SetSongText(PackText.SongText.Illustrator, songId, info.Illustrators[difficulty]))
                IllustratorShown[songId] = difficulty;
        }

        private static void ShowPreview(ushort songId, ChartInfo info, int difficulty)
        {
            if (info.Previews == null) return;
            float[] window = info.Previews[difficulty];
            if (window == null) return;
            if (!TryGetAssets(out IntPtr songData, out IntPtr _)) return;
            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            int count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;
            if (songId == 0 || songId >= count) return;
            IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + songId * SongInfoSize;
            WritePreview(slot, window);
        }

        internal static void ApplyPreviewTo(IntPtr songInfo, int difficulty)
        {
            if (songInfo == IntPtr.Zero || difficulty < 0 || !Offsets.Ready) return;
            ushort songId = Memory.U16(songInfo + SongInfoId);
            if (!ById.TryGetValue(songId, out ChartInfo info) || info.Previews == null) return;
            float[] window = info.Previews[difficulty];
            if (window == null) return;
            WritePreview(songInfo, window);
            if (PreviewShown.TryGetValue(songId, out int shown) && shown == difficulty) return;
            PreviewShown[songId] = difficulty;
            ModLog.Load($"preview {songId}: difficulty {difficulty} " +
                        $"{window[0]:F1}..{window[1]:F1}s");
        }

        private static void WritePreview(IntPtr songInfo, float[] window)
        {
            Memory.WriteF32(songInfo + SongInfoPreviewStart, window[0]);
            Memory.WriteF32(songInfo + SongInfoPreviewEnd, window[1]);
        }

        internal static void RefreshList(IntPtr songData) => Refresh(songData);

        private static void Refresh(IntPtr songData)
        {
            Memory.WritePtr(songData + SongDataNameIndex, IntPtr.Zero);
            IntPtr klass = FieldResolver.ClassPointer("SongData");
            if (klass == IntPtr.Zero) { ModLog.Error("SongData could not be found; the new songs will not show"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_yOA", 0);
            if (method == IntPtr.Zero)
            {
                ModLog.Error("SongData._yOA could not be found; the new songs will not show");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, songData, null, ref raised);

            if (raised != IntPtr.Zero)
                ModLog.Error($"the game's song list refresh raised: {Raised.Text(raised)}");
        }

        private static IntPtr BuildReadings(IntPtr template, string text)
        {
            if (!Memory.LooksLikeObject(template)) return IntPtr.Zero;
            int locales = Memory.I32(template + Offsets.Runtime.ArrayLength);
            if (locales <= 0 || locales > LocaleLimit)
            {
                ModLog.Warn($"the shipped title readings hold no locale count ({locales}); " +
                                 $"'{text}' keeps its name from the localisation table");
                return IntPtr.Zero;
            }

            IntPtr array = NewArray(template, locales);
            if (array == IntPtr.Zero) return IntPtr.Zero;
            IntPtr value = Str(text);
            for (int i = 0; i < locales; i++)
                Memory.WritePtr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size, value);
            return array;
        }

        private static IntPtr BuildCharts(IntPtr template, ChartInfo info)
        {
            IntPtr array = NewArray(template, ChartInfo.Difficulties);
            if (array == IntPtr.Zero) return IntPtr.Zero;
            IntPtr source = template + Offsets.Runtime.ArrayDataOffset;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                IntPtr slot = array + Offsets.Runtime.ArrayDataOffset + d * ChartInfoSize;
                Buffer.MemoryCopy((void*)source, (void*)slot, ChartInfoSize, ChartInfoSize);

                if (info.NoChart[d])
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(info.BaseName + "_" + (d + 1)));
                    Memory.WriteU8(slot + ChartInfoAvailable, 0);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }
                else
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(Path.GetFileName(info.ChartPaths[d])));
                    Memory.WriteU8(slot + ChartInfoAvailable, 1);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }

                Memory.WritePtr(slot + ChartInfoDesigner, Str(info.Charters[d] ?? string.Empty));
                Memory.WritePtr(slot + ChartInfoJacketDesigner, Str(IllustratorOf(info, d)));
                int? level = info.Level[d];
                Memory.WriteI32(slot + ChartInfoRating, level ?? 0);
                Memory.WritePtr(slot + ChartInfoSection,
                                Str(level is int rated
                                    ? rated.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    : string.Empty));
            }

            return array;
        }

        private static int NextPackId(IntPtr packs, int count)
        {
            int next = 0;

            for (int i = 0; i < count; i++)
            {
                int id = Memory.U16(packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize + PackInfoId);
                if (id >= next) next = id + 1;
            }

            return next;
        }

        private static IntPtr AllocateSongIds(IntPtr packData, int count)
        {
            IntPtr packs = Memory.Ptr(packData + PackDataPacks);
            int packCount = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            if (packCount <= 0) { ModLog.Warn("PackData has no packs to take a template from"); return IntPtr.Zero; }

            IntPtr firstPack = packs + Offsets.Runtime.ArrayDataOffset;
            IntPtr template = Memory.Ptr(firstPack + PackInfoSongs);
            if (!Memory.LooksLikeObject(template)) { ModLog.Warn("a shipped pack has no song ids"); return IntPtr.Zero; }

            return NewArray(template, count);
        }

        private static IntPtr FirstCharts(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr charts = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset + i * SongInfoSize + SongInfoCharts);
                if (Memory.LooksLikeObject(charts) &&
                    Memory.I32(charts + Offsets.Runtime.ArrayLength) >= ChartInfo.Difficulties)
                    return charts;
            }

            ModLog.Warn("no shipped song has four difficulty records to take a shape from");
            return IntPtr.Zero;
        }

        private static IntPtr FirstReadings(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr readings = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset +
                                             i * SongInfoSize + SongInfoTitleReading);
                if (Memory.LooksLikeObject(readings) &&
                    Memory.I32(readings + Offsets.Runtime.ArrayLength) > 0)
                    return readings;
            }

            ModLog.Warn("no shipped song carries a title-reading array to take a shape from");
            return IntPtr.Zero;
        }

        internal static IntPtr GrowArray(IntPtr like, int count)
        {
            int oldCount = Memory.I32(like + Offsets.Runtime.ArrayLength);
            IntPtr grown = NewArray(like, count);
            if (grown == IntPtr.Zero) return IntPtr.Zero;
            int stride = ElementStride(like, oldCount);
            if (stride <= 0) return IntPtr.Zero;
            long bytes = (long)oldCount * stride;
            if (bytes > 0)
            {
                Buffer.MemoryCopy((void*)(like + Offsets.Runtime.ArrayDataOffset),
                                  (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);
            }

            return grown;
        }

        internal static IntPtr NewArray(IntPtr like, int count)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(like);
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element == IntPtr.Zero) { ModLog.Warn("an array has no element class"); return IntPtr.Zero; }

            IntPtr array = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, (ulong)count);
            if (array == IntPtr.Zero) ModLog.Warn($"could not allocate an array of {count}");
            return array;
        }

        private static int ElementStride(IntPtr array, int count)
        {
            if (count <= 0) return 0;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
            return (int)(bytes / count);
        }

        private static void WriteSongId(IntPtr array, int index, ushort value) =>
            Memory.WriteU16(array + Offsets.Runtime.ArrayDataOffset + index * 2, value);

        private static IntPtr Str(string text) =>
            Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
    }
}

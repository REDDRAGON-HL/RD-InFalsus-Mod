using System;
using System.Collections.Generic;

namespace CustomSong
{
    internal static class JacketCatalog
    {
        private static int SongInfoBaseName => Offsets.Song.BaseName;

        private static readonly Dictionary<string, IntPtr[]> ByBase =
            new Dictionary<string, IntPtr[]>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string[]> Paths =
            new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly List<string> Waiting = new List<string>();
        private static readonly HashSet<string> Imported = new HashSet<string>(StringComparer.Ordinal);
        private static IntPtr _armed;
        private static IntPtr _armedMaterial;
        private static bool _armedBackground;
        internal static int Count => Paths.Count;

        internal static void Reset()
        {
            ByBase.Clear();
            Paths.Clear();
            Waiting.Clear();
            Imported.Clear();
            ChartArrays.Clear();
            Disarm();
        }

        internal static void Step()
        {
            if (Waiting.Count == 0) return;
            string baseName = Waiting[0];
            Waiting.RemoveAt(0);
            Materials(baseName);
        }

        internal static long Asked, Claimed;
        private static int ChartInfoSize => Offsets.Chart.Size;

        private static readonly List<(IntPtr Charts, ChartInfo Info)> ChartArrays =
            new List<(IntPtr, ChartInfo)>();

        internal static void RegisterCharts(IntPtr charts, ChartInfo info)
        {
            if (info == null || !Memory.LooksLikeObject(charts)) return;
            ChartArrays.Add((charts, info));
        }

        internal static bool FindByChart(IntPtr chartInfo, out IntPtr material)
        {
            material = IntPtr.Zero;
            if (!Memory.LooksLikeObject(chartInfo)) return false;

            foreach ((IntPtr charts, ChartInfo info) in ChartArrays)
            {
                IntPtr first = charts + Offsets.Runtime.ArrayDataOffset;
                for (int d = 0; d < ChartInfo.Difficulties; d++)
                {
                    if (first + d * ChartInfoSize != chartInfo) continue;
                    IntPtr[] byDifficulty = Materials(info.BaseName);
                    if (byDifficulty == null) return false;
                    material = At(byDifficulty, d);
                    return material != IntPtr.Zero;
                }
            }

            string id = Memory.Text(Memory.Ptr(chartInfo), 128);
            if (id == null) return false;
            if (!Offsets.Ready) return false;
            int at = SongCatalog.DifficultyIndexOf(Memory.U8(chartInfo + Offsets.Chart.Difficulty));
            if (at < 0) return false;

            if (!CurrentSong.TryReadSong(out IntPtr playing))
            {
                ModLog.Warn($"no song is selected to match the chart '{id}' against; the game's own " +
                                 "picture is used");
                return false;
            }

            if (!SongCatalog.ById.TryGetValue(Memory.U16(playing + Offsets.Song.Id), out ChartInfo owner))
                return false;

            if (owner.ChartNames[at] != id)
            {
                ModLog.Warn($"the song being played does not file a chart called '{id}' at this " +
                                 "difficulty; the game's own picture is used");
                return false;
            }

            IntPtr[] ownerMaterials = Materials(owner.BaseName);
            if (ownerMaterials == null) return false;
            material = At(ownerMaterials, at);
            return material != IntPtr.Zero;
        }

        private static IntPtr At(IntPtr[] materials, int difficulty)
        {
            if (difficulty >= 0 && difficulty < materials.Length) return materials[difficulty];
            IntPtr only = IntPtr.Zero;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == IntPtr.Zero) return IntPtr.Zero;
                if (i == 0) only = materials[i];
                else if (materials[i] != only) return IntPtr.Zero;
            }

            return only;
        }

        internal static bool Add(ChartInfo info, out string reason)
        {
            reason = null;
            Imported.Add(info.BaseName);
            Paths[info.BaseName] = info.JacketPaths;
            ByBase.Remove(info.BaseName);
            if (!Waiting.Contains(info.BaseName)) Waiting.Add(info.BaseName);

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                string path = info.JacketPaths[d];
                if (path != null && System.IO.File.Exists(path)) continue;
                reason = $"difficulty {d} has no picture";
                return false;
            }

            return true;
        }

        private static IntPtr[] Materials(string baseName)
        {
            if (ByBase.TryGetValue(baseName, out IntPtr[] built)) return built;
            if (!Paths.TryGetValue(baseName, out string[] paths)) return null;
            var materials = new IntPtr[ChartInfo.Difficulties];
            var made = new Dictionary<string, IntPtr>(StringComparer.Ordinal);

            for (int d = 0; d < materials.Length; d++)
            {
                string path = paths[d];
                if (path == null) continue;

                if (!made.TryGetValue(path, out IntPtr material))
                {
                    material = JacketFactory.Build(path, out string why);
                    made[path] = material;

                    if (material == IntPtr.Zero)
                        ModLog.WarnRelease($"jacket '{System.IO.Path.GetFileName(path)}' could not be " +
                                           "built: " + why);
                }

                materials[d] = material;
            }

            ByBase[baseName] = materials;
            return materials;
        }

        internal static bool IsOurs(IntPtr songInfo)
        {
            string baseName = NameOf(songInfo);
            return baseName != null && Imported.Contains(baseName);
        }

        internal static string NameOf(IntPtr songInfo)
        {
            if (!Offsets.Ready) return null;
            if (!Memory.LooksLikeObject(songInfo)) return null;
            return Memory.Text(Memory.Ptr(songInfo + SongInfoBaseName), 128);
        }

        internal static bool Find(IntPtr songInfo, out IntPtr material)
        {
            material = IntPtr.Zero;
            string baseName = NameOf(songInfo);
            if (baseName == null) return false;
            IntPtr[] materials = Materials(baseName);
            if (materials != null)
            {
                material = At(materials, CurrentSong.Difficulty());
                if (material != IntPtr.Zero)
                {
                    Asked++;
                    return true;
                }
            }

            if (Imported.Contains(baseName))
            {
                Hooks.ApaAsked++;
                ModLog.Warn($"jacket asked for '{baseName}' - ours, but no picture was available " +
                                 "for the selected difficulty");
            }

            return false;
        }

        internal static void Arm(IntPtr reference, IntPtr material, bool background)
        {
            _armed = reference;
            _armedMaterial = material;
            _armedBackground = background;
        }

        internal static void Disarm()
        {
            _armed = IntPtr.Zero;
            _armedMaterial = IntPtr.Zero;
            _armedBackground = false;
        }

        internal static bool TryClaim(IntPtr reference, out IntPtr material, out bool background)
        {
            material = IntPtr.Zero;
            background = false;
            if (_armed == IntPtr.Zero || reference != _armed) return false;
            material = _armedMaterial;
            background = _armedBackground;
            Disarm();
            Claimed++;
            return true;
        }
    }
}

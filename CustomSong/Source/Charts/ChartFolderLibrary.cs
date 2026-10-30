using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CustomSong
{
    internal static class ChartFolderLibrary
    {
        private const string FolderName = "Charts";

        private const string DefaultPack = "Custom Song";

        private static string _located;

        internal static string ModFolder()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location)) return Path.GetDirectoryName(location);
            }
            catch (Exception e)
            {
                ModLog.Warn($"could not read this assembly's path: {ModLog.Describe(e)}");
            }
            return null;
        }

        private static string GameFolder()
        {
#if BEPINEX
            try
            {
                string root = BepInEx.Paths.GameRootPath;
                if (!string.IsNullOrEmpty(root)) return root;
            }
            catch (Exception e)
            {
                ModLog.Warn($"could not ask BepInEx for the game folder: {ModLog.Describe(e)}");
            }
#endif
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                    return baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception e)
            {
                ModLog.Warn($"could not read the process's base directory: {ModLog.Describe(e)}");
            }

            return null;
        }

        internal static string Locate()
        {
            if (_located != null) return _located;

            string mod = ModFolder();
            if (mod == null) return null;
            DirectoryInfo parent = Directory.GetParent(mod);
            string game = GameFolder();
            string[] candidates =
            {
                game == null ? null : Path.GetFullPath(Path.Combine(game, FolderName)),
#if BEPINEX
                parent?.Parent == null
                    ? null
                    : Path.GetFullPath(Path.Combine(parent.Parent.FullName, FolderName)),
#endif
                parent == null ? null : Path.GetFullPath(Path.Combine(parent.FullName, FolderName)),
                Path.GetFullPath(Path.Combine(mod, FolderName)),
            };

            foreach (string candidate in candidates)
            {
                if (candidate == null) continue;
                if (Directory.Exists(candidate))
                {
                    _located = candidate;
                    ModLog.Load($"{FolderName} folder: {candidate}");
                    return candidate;
                }
            }

            string preferred = candidates[0] ?? candidates[1];
            if (preferred == null)
            {
                ModLog.Warn("there is nowhere to put a charts folder");
                return null;
            }

            try
            {
                Directory.CreateDirectory(preferred);
                Directory.CreateDirectory(Path.Combine(preferred, DefaultPack));
                _located = preferred;
                ModLog.Load($"{FolderName} folder: {preferred} (created)");
                return preferred;
            }
            catch (Exception e)
            {
                ModLog.Warn($"no {FolderName} folder, and one could not be created at {preferred}: " +
                                 ModLog.Describe(e));
                return preferred;
            }
        }

        internal static List<ChartPack> ImportPacks(string root, Func<ChartInfo, string> validateCharts)
        {
            var packs = new List<ChartPack>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                ModLog.WarnRelease($"no {FolderName} folder at {root}; nothing to load");
                return packs;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease($"{root} could not be listed: {ModLog.Describe(e)}");
                return packs;
            }

            if (folders.Length == 0) ModLog.WarnRelease($"'{root}' has no pack folder inside it");

            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);

                if (File.Exists(Path.Combine(folder, "if")))
                {
                    ModLog.Warn($"'{name}' is a song directly under Charts; put it inside a pack folder instead");
                    continue;
                }

                ChartPack pack = ReadPack(folder);
                string[] songs;
                try
                {
                    songs = Directory.GetDirectories(folder);
                    Array.Sort(songs, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception e)
                {
                    ModLog.Warn($"pack folder '{name}' could not be listed: {ModLog.Describe(e)}");
                    continue;
                }

                foreach (string song in songs)
                {
                    if (!File.Exists(Path.Combine(song, "if")))
                    {
                        string deeper = Nested(song);
                        ModLog.WarnRelease(deeper == null
                            ? $"chart folder '{Path.GetFileName(song)}' has no if; skipped"
                            : $"chart folder '{Path.GetFileName(song)}' has no if, but its '{deeper}' " +
                              "does; the song is one folder too deep");
                        continue;
                    }

                    AddSong(pack, song, validateCharts);
                }

                if (pack.Charts.Count > 0) packs.Add(pack);
                else ModLog.WarnRelease($"pack folder '{name}' has no chart folder; skipped");
            }

            int songsTotal = 0;
            foreach (ChartPack pack in packs) songsTotal += pack.Charts.Count;
            ModLog.Load($"packs: {packs.Count}, charts: {songsTotal} imported");
            return packs;
        }

        private static ChartPack ReadPack(string folder)
        {
            string name = Path.GetFileName(folder);
            var pack = new ChartPack { Folder = folder, Name = name, Slug = name };
            string descriptor = Path.Combine(folder, "pack.json");
            if (File.Exists(descriptor))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(descriptor),
                        new JsonDocumentOptions
                        {
                            CommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                        });
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("name", out JsonElement title) &&
                            title.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(title.GetString()))
                            pack.Name = title.GetString().Trim();

                        if (root.TryGetProperty("visuals", out JsonElement visuals) &&
                            visuals.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty entry in visuals.EnumerateObject())
                            {
                                if (Array.IndexOf(ChartPack.PictureKeys, entry.Name) < 0)
                                {
                                    ModLog.Warn($"pack.json in '{name}': '{entry.Name}' is not one of the " +
                                                "seven pictures; it is ignored");
                                    continue;
                                }

                                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                                string path = LocalPicture(folder, entry.Value.GetString());
                                if (path != null) pack.Visuals[entry.Name] = path;
                                else ModLog.Warn($"pack.json in '{name}': '{entry.Name}' points at " +
                                                 $"'{entry.Value.GetString()}', which is not a picture in the pack");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ModLog.Warn($"pack.json in '{name}' could not be read: {ModLog.Describe(e)}");
                }
            }

            return pack;
        }

        private static string LocalPicture(string folder, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (Path.GetFileName(name) != name) return null;
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") return null;
            string path = Path.Combine(folder, name);
            return File.Exists(path) ? path : null;
        }

        private static string Nested(string folder)
        {
            try
            {
                foreach (string inner in Directory.GetDirectories(folder))
                    if (File.Exists(Path.Combine(inner, IfReader.InfoFileName)))
                        return Path.GetFileName(inner);
            }
            catch (Exception e)
            {
                ModLog.Warn($"'{folder}' could not be listed: {ModLog.Describe(e)}");
            }

            return null;
        }

        private static void AddSong(ChartPack pack, string folder, Func<ChartInfo, string> validateCharts)
        {
            string name = Path.GetFileName(folder);

            if (!IfReader.TryRead(folder, out ChartInfo info, out string reason))
            {
                ModLog.WarnRelease($"chart folder '{name}' skipped: {reason ?? IfReader.WhyNot(folder)}");
                return;
            }

            reason = validateCharts(info);
            if (reason != null)
            {
                ModLog.WarnRelease($"chart folder '{name}' skipped: {reason}");
                return;
            }

            pack.Charts.Add(info);
        }

        private static string Levels(ChartInfo info)
        {
            var text = new string[ChartInfo.Difficulties];
            for (int d = 0; d < text.Length; d++) text[d] = info.Level[d]?.ToString() ?? "-";
            return string.Join("/", text);
        }
    }
}

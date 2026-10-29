using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CustomSong
{
    internal static class CustomSongCore
    {
        internal const string Version = "1.0.0";
        private static List<ChartPack> _packs = new List<ChartPack>();
        private static bool _registered;
        private static bool _packVisualsDone;
        private static bool _visualsWaitingLogged;
        private static IntPtr _songData;
        private static IntPtr _packData;
        private static string _root;
        private static string _fingerprint;
        private static int _rescanIn;
        private static int _warm;

        internal static void Loaded()
        {
            ModLog.Load("Custom Song loading...");
        }

        internal static void Initialize()
        {
            try
            {
                if (!ChartCodec.Resolve())
                {
                    ModLog.Error("the game's chart decoder could not be reached; no charts will load");
                    return;
                }

                StreamingAssets.Resolve();
                _root = ChartFolderLibrary.Locate();

                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _fingerprint = Fingerprint(_root);

                int total = 0;
                foreach (ChartPack pack in _packs) total += pack.Charts.Count;
                ModLog.Load($"packs: {_packs.Count}, charts: {total}");

                ChartPoolPatch.Apply();
                Hooks.Install();
                AutoSwitchHook.Install();
                AutoRowHooks.Install();
                Auto.AutoHooks.Install();
                Auto.AutoResults.Install();
            }
            catch (Exception e)
            {
                ModLog.Error($"chart loading failed: {ModLog.Describe(e)}");
            }
        }

        internal static void Update()
        {
            VideoBackground.Follow();
            AutoSwitch.NoteOpen("frame");
            AutoRow.Tick();

            if (_rescanIn > 0 && --_rescanIn == 0) HotReloadIfChanged();

            if (++_warm >= 20)
            {
                _warm = 0;
                JacketCatalog.Step();
            }

            if (_registered)
            {
                if (!_packVisualsDone) ApplyPackVisuals();
                return;
            }

            if (_packs.Count == 0) return;

            try
            {
                if (!SongCatalog.TryGetAssets(out _songData, out _packData))
                {
                    if (SongCatalog.Unusable) _registered = true;
                    return;
                }

                SongCatalog.CaptureBaseline(_songData, _packData);
                ModLog.Load(FieldResolver.Stats());
                RegisterPacks();
                ScoreStore.Prepare();
                _registered = true;
                ApplyPackVisuals();
                ModLog.Load("charts registered");
            }
            catch (Exception e)
            {
                _registered = true;
                ModLog.Error($"registering the custom songs failed: {ModLog.Describe(e)}");
            }
        }

        internal static void RescanSoon()
        {
            if (_rescanIn <= 0) _rescanIn = 2;
        }

        internal static void HotReloadIfChanged()
        {
            string current = Fingerprint(_root);
            if (current == _fingerprint) return;

            if (!_registered || _songData == IntPtr.Zero || _packData == IntPtr.Zero)
            {
                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _fingerprint = current;
                return;
            }

            try
            {
                ModLog.Load("charts changed on disk; reloading");
                SongCatalog.RestoreBaseline(_songData, _packData);
                _visualTables.Clear();
                PackText.ResetVisualCache();
                ChartCatalog.Reset();
                AudioCatalog.Reset();
                JacketCatalog.Reset();
                BackgroundCatalog.Reset();

                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _fingerprint = current;

                if (_packs.Count == 0)
                {
                    SongCatalog.RefreshList(_songData);
                    PackText.RefreshLookup(_packData);
                    ModLog.Load("charts: 0 registered");
                    return;
                }

                RegisterPacks();
                ScoreStore.Refile();
                ApplyPackVisuals();
                ModLog.Load("charts reloaded");
            }
            catch (Exception e)
            {
                ModLog.Error($"hot reload failed: {ModLog.Describe(e)}");
            }
        }

        private static void RegisterPacks()
        {
            List<ChartPack> ready = SongCatalog.SettlePacks(_packs, _songData);
            if (ready.Count == 0)
            {
                ModLog.Error("no custom song could be registered");
                return;
            }

            int audio = RegisterAudio(ready);
            int added = SongCatalog.InjectPacks(_songData, _packData, ready);
            _packs = ready;

            if (added == 0)
            {
                ModLog.Error("the custom songs could not be added to the game's song list");
                return;
            }

            _packVisualsDone = false;
            ModLog.Load($"audio: {audio}, songs: {added}, packs: {ready.Count}");
        }

        private static void ApplyPackVisuals()
        {
            if (_packs.Count == 0) return;

            if (!PackText.TryGetVisuals(out IntPtr assets))
            {
                if (!_visualsWaitingLogged)
                {
                    _visualsWaitingLogged = true;
                    ModLog.Load("pack visuals: not ready");
                }
                return;
            }

            _visualsWaitingLogged = false;

            if (!ApplyVisualsTo(assets)) return;

            PackText.RefreshLookup(_packData);
            _packVisualsDone = true;
            ModLog.Load("pack visuals applied");
        }

        internal static bool ApplyVisualsTo(IntPtr assets)
        {
            if (_packs.Count == 0) return false;
            if (_visualTables.Contains(assets)) return true;
            int highestId = 0;
            foreach (ChartPack pack in _packs)
            {
                if (pack.PackId < 0) return false;
                highestId = Math.Max(highestId, pack.PackId);
            }

            if (!PackText.PrepareVisualRows(assets, highestId))
            {
                ModLog.Load("pack visual rows: could not be prepared");
                return false;
            }

            foreach (ChartPack pack in _packs)
            {
                PackText.SetPackName(pack.PackId, pack.Name);
                int applied = PackText.SetPackVisuals(assets, pack.PackId, pack.Folder, pack.Visuals);
            }

            _visualTables.Add(assets);
            return true;
        }

        private static readonly List<IntPtr> _visualTables = new List<IntPtr>();
        internal static void Shutdown() => Hooks.Uninstall();

        private static int RegisterAudio(List<ChartPack> packs)
        {
            if (!StreamingAssets.Resolve())
            {
                ModLog.Error("the game's asset manager could not be reached, so no custom audio can be registered");
                return 0;
            }

            int count = 0;
            foreach (ChartPack pack in packs)
            {
                foreach (ChartInfo info in pack.Charts)
                {
                    if (AudioCatalog.Add(info.BaseName, info.SongPath, -1, out string reason)) count++;
                    else ModLog.Error($"'{info.Name}' was left out: {reason}");
                    if (info.Songs == null) continue;

                    for (int d = 0; d < info.Songs.Length; d++)
                    {
                        if (info.Songs[d] == null || info.Songs[d] == info.SongPath) continue;
                        if (AudioCatalog.Add(info.BaseName, info.Songs[d], d, out reason)) count++;
                        else ModLog.Error($"'{info.Name}' difficulty {d} audio was left out: {reason}");
                    }
                }
            }

            return count;
        }

        private static string Fingerprint(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return string.Empty;
            var text = new StringBuilder();
            try
            {
                foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!Relevant(path)) continue;
                    var file = new FileInfo(path);
                    text.Append(path).Append('|').Append(file.Length).Append('|')
                        .Append(file.LastWriteTimeUtc.Ticks).Append('\n');
                }
            }
            catch (Exception e)
            {
                ModLog.Warn($"could not fingerprint the charts folder: {ModLog.Describe(e)}");
            }

            return text.ToString();
        }

        private static bool Relevant(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name == "if" || name == "pack.json") return true;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".spc" || extension == ".ogg" || extension == ".mp3" ||
                   extension == ".wav" || extension == ".png" || extension == ".jpg" ||
                   extension == ".jpeg" || extension == ".mp4";
        }

        private static string Validate(ChartInfo info)
        {
            byte[] scratch = new byte[32];
            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (info.NoChart[d]) continue;
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(info.ChartPaths[d]);
                }
                catch (Exception e)
                {
                    return $"difficulty {d} could not be read: {ModLog.Describe(e)}";
                }

                unsafe
                {
                    fixed (byte* tuple = scratch)
                    {
                        if (!ChartCodec.TryDecode(bytes, info.ChartNames[d], (IntPtr)tuple, out string reason))
                            return $"difficulty {d}: {reason}";
                    }
                }
            }

            if (!JacketCatalog.Add(info, out string jacketReason))
                ModLog.Warn($"'{info.Name}' has no jacket of its own: {jacketReason}");

            if (!BackgroundCatalog.Add(info, out string backgroundReason))
                ModLog.Warn($"'{info.Name}' has no background of its own: {backgroundReason}");

            return null;
        }
    }
}

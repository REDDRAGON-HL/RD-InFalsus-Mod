using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CustomSong
{
    internal static class CustomSongCore
    {
        internal const string Version = "1.1.5";
        private static List<ChartPack> _packs = new List<ChartPack>();
        private static bool _registered;
        private static bool _visualsWaitingLogged;
        private static IntPtr _songData;
        private static IntPtr _packData;
        private static string _root;
        private static string _fingerprint;
        private static FileSystemWatcher _watcher;
        private static volatile bool _chartsDirty;

        internal static void Loaded()
        {
            ModLog.Load("Custom Song loading...");
        }

        internal static void Initialize()
        {
            try
            {
                long mark = Timing.Now;

                if (!ChartCodec.Resolve())
                {
                    ModLog.Error("the game's chart decoder could not be reached; no charts will load");
                    return;
                }

                bool assetsReady = StreamingAssets.Resolve();
                _root = ChartFolderLibrary.Locate();
                StartWatcher(_root);
                mark = Timing.Now;
                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _chartsDirty = false;
                mark = Timing.Now;
                if (_watcher == null) _fingerprint = Fingerprint(_root);
                int total = 0;
                foreach (ChartPack pack in _packs) total += pack.Charts.Count;
                ModLog.Load($"packs: {_packs.Count}, charts: {total}");

                try
                {
                    if (SongFont.Detect(_packs)) Hooks.InstallFont();
                }
                catch (Exception e)
                {
                    ModLog.Warn("the song titles' font could not be hooked: " + ModLog.Describe(e));
                }

                mark = Timing.Now;
                ChartPoolPatch.Apply();
                mark = Timing.Now;
            }
            catch (Exception e)
            {
                ModLog.Error($"chart loading failed: {ModLog.Describe(e)}");
            }
        }

        internal static void Update()
        {
            MainThread.Capture();
            if (!_hooksInstalled) InstallHooks();
            VideoBackground.Follow();
            AutoSwitch.NoteOpen("frame");
            AutoRow.Tick();
            Hooks.TickStripWindow();
            Auto.AutoResults.Poll();

            if (_registered) return;
            if (_packs.Count == 0) return;

            try
            {
                if (!SongCatalog.TryGetAssets(out _songData, out _packData))
                {
                    if (SongCatalog.Unusable) _registered = true;
                    return;
                }

                SongCatalog.CaptureBaseline(_songData, _packData);
                RegisterPacks();
                ScoreStore.Prepare();
                ScoreStore.ScanResults(_songData);
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

        private static void InstallHooks()
        {
            _hooksInstalled = true;
            long mark = Timing.Now;
            Hooks.Install();
            AutoSwitchHook.Install();
            AutoRowHooks.Install();
            Auto.AutoHooks.Install();
            Auto.AutoResults.Install();
            PackText.Install();
        }

        private static bool _hooksInstalled;

        internal static void OnSceneTransition(string destination)
        {
            if (!_registered) return;

            if (!_warmed && destination == HubSceneName)
            {
                _warmed = true;
                JacketCatalog.WarmAll();
            }

            if (destination != SongSelectSceneName) return;
            HotReloadIfChanged();
        }

        private static bool _warmed;
        private const string SongSelectSceneName = "SongSelectScene";
        private const string HubSceneName = "HubScene";

        internal static void HotReloadIfChanged()
        {
            long mark;
            if (_watcher != null)
            {
                if (!_chartsDirty) return;
                _chartsDirty = false;
            }
            else
            {
                mark = Timing.Now;
                string current = Fingerprint(_root);
                if (current == _fingerprint) return;
            }

            if (!_registered || _songData == IntPtr.Zero || _packData == IntPtr.Zero)
            {
                mark = Timing.Now;
                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _chartsDirty = false;
                if (_watcher == null) _fingerprint = Fingerprint(_root);
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
                JacketFactory.Reset();
                BackgroundCatalog.Reset();
                mark = Timing.Now;
                _packs = ChartFolderLibrary.ImportPacks(_root, Validate);
                _chartsDirty = false;
                if (_watcher == null) _fingerprint = Fingerprint(_root);

                if (_packs.Count == 0)
                {
                    SongCatalog.RefreshList(_songData);
                    PackText.RefreshLookup(_packData);
                    ModLog.Load("charts: 0 registered");
                    return;
                }

                RegisterPacks();
                ScoreStore.Refile();
                ScoreStore.ScanResults(_songData);
                mark = Timing.Now;
                ApplyPackVisuals();
                JacketCatalog.WarmAll();
                _warmed = true;
                ModLog.Load("charts reloaded");
            }
            catch (Exception e)
            {
                ModLog.Error($"hot reload failed: {ModLog.Describe(e)}");
            }
        }

        private static void RegisterPacks()
        {
            long mark = Timing.Now;
            List<ChartPack> ready = SongCatalog.SettlePacks(_packs, _songData);
            if (ready.Count == 0)
            {
                ModLog.Error("no custom song could be registered");
                return;
            }
            mark = Timing.Now;
            int audio = RegisterAudio(ready);
            mark = Timing.Now;
            int added = SongCatalog.InjectPacks(_songData, _packData, ready);
            _packs = ready;

            if (added == 0)
            {
                ModLog.Error("the custom songs could not be added to the game's song list");
                return;
            }

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
            long mark = Timing.Now;
            if (!ApplyVisualsTo(assets)) return;
            PackText.RefreshLookup(_packData);
            ModLog.Load("pack visuals applied");
        }

        internal static bool ApplyVisualsTo(IntPtr assets)
        {
            if (_packs.Count == 0) return false;
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

            bool first = _visualTables.Add(assets);
            foreach (ChartPack pack in _packs)
            {
                if (first)
                {
                    if (pack.Names != null) PackText.SetPackName(pack.PackId, pack.Names);
                    else PackText.SetPackName(pack.PackId, pack.Name);
                }

                int applied = PackText.SetPackVisuals(assets, pack.PackId, pack);

                if (first)
                {
                }
            }

            return true;
        }

        private static readonly HashSet<IntPtr> _visualTables = new HashSet<IntPtr>();
        internal static void InvalidateVisualTables() => _visualTables.Clear();

        internal static void RescanResults()
        {
            if (!ScoreStore.Ready || _songData == IntPtr.Zero) return;
            ScoreStore.ScanResults(_songData);
        }

        internal static void Shutdown()
        {
            _watcher?.Dispose();
            _watcher = null;
            Hooks.Uninstall();
        }

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

        private static void StartWatcher(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            try
            {
                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                _watcher.Changed += OnChartsChanged;
                _watcher.Created += OnChartsChanged;
                _watcher.Deleted += OnChartsChanged;
                _watcher.Renamed += OnChartsRenamed;
                _watcher.Error += OnChartsWatcherError;
                _watcher.EnableRaisingEvents = true;
                ModLog.Load("charts folder is watched for changes");
            }
            catch (Exception e)
            {
                ModLog.Warn("the charts folder could not be watched; changes are found by hashing it " +
                            "when the song-select page opens: " + ModLog.Describe(e));
                _watcher = null;
            }
        }

        private static void OnChartsChanged(object sender, FileSystemEventArgs e) => _chartsDirty = true;
        private static void OnChartsRenamed(object sender, RenamedEventArgs e) => OnChartsChanged(sender, e);
        private static void OnChartsWatcherError(object sender, ErrorEventArgs e) => _chartsDirty = true;

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
                   extension == ".jpeg" || extension == ".mp4" || extension == FalsumSong.Extension;
        }

        private static string Validate(ChartInfo info)
        {
            long mark = Timing.Now;
            byte[] scratch = new byte[32];
            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (info.NoChart[d]) continue;
                string path = info.ChartPaths[d];
                if (!checkedPaths.Add(path)) continue;
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(path);
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

            double charts = Timing.Ms(mark);
            mark = Timing.Now;

            if (!JacketCatalog.Add(info, out string jacketReason))
                ModLog.Warn($"'{info.Name}' has no jacket of its own: {jacketReason}");
            double jacket = Timing.Ms(mark);
            mark = Timing.Now;

            if (!BackgroundCatalog.Add(info, out string backgroundReason))
                ModLog.Warn($"'{info.Name}' has no background of its own: {backgroundReason}");

            return null;
        }
    }
}

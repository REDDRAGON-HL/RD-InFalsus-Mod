using System;
using CustomSong.Auto;

namespace CustomSong.Replay
{
    internal static class ReplayCore
    {
        internal static bool Pending;
        internal static bool Playing;
        internal static bool Ran;
        internal static ReplayData Data;
        private static ReplayRecorder _recorder;
        private static ReplayPlayer _player;
        private static bool _faulted;
        private static int _faults;
        private static bool _toResults;
        internal static bool HasRecording => Data != null && Data.Recorded > 0;

        internal static void OnSong()
        {
            if (_toResults) return;
            bool wasPlaying = Playing;
            ReplayData data = Data;
            _player = null;
            _faulted = false;

            if (Pending && data != null && data.Recorded > 0)
            {
                Pending = false;
                Playing = true;
                Ran = true;
                Data = data;
                AutoMark.Ran = false;
                _recorder = null;
                AutoResults.BlockParticles(true);
                return;
            }

            if (wasPlaying && data != null)
            {
                Playing = true;
                Ran = true;
                Data = data;
                AutoMark.Ran = false;
                _recorder = null;
                AutoResults.BlockParticles(true);
                return;
            }

            Playing = false;
            Ran = false;
            AutoResults.BlockParticles(AutoSwitch.On);
        }

        internal static void Frame(IntPtr engine, double seconds)
        {
            if (_faulted || _toResults) return;

            try
            {
                AutoSong song = AutoSong.Current;
                if (!song.Chart.Loaded) return;
                double chartMs = Memory.F64(engine + AutoOffsets.Engine.ChartTime) * 1000.0;

                if (Playing)
                {
                    if (_player == null)
                    {
                        if (Data == null || !Data.Matches(song.Chart))
                        {
                            Stop("the chart does not match the recording");
                            return;
                        }

                        _player = new ReplayPlayer(song.Chart, Data);
                    }

                    _player.Frame(engine, seconds, chartMs);
                    return;
                }

                if (Ran) return;

                if (AutoMark.Ran)
                {
                    _recorder = null;
                    return;
                }

                if (_recorder == null)
                    _recorder = new ReplayRecorder(song.Chart);
                else if (_recorder.Chart != song.Chart)
                {
                    if (_recorder.CanAdopt(song.Chart, chartMs)) _recorder.Adopt(song.Chart);
                    else _recorder = new ReplayRecorder(song.Chart);
                }

                _recorder.Frame(engine, chartMs);
            }
            catch (Exception e)
            {
                Fault("frame", e);
            }
        }

        internal static void OzPost(IntPtr engine)
        {
            if (_faulted) return;

            try
            {
                if (!Memory.LooksLikeObject(engine)) return;
                double chartMs = Memory.F64(engine + AutoOffsets.Engine.ChartTime) * 1000.0;

                if (Playing)
                {
                    _player?.Correct(chartMs);
                    return;
                }

                if (Ran || _recorder == null) return;
                _recorder.OzPost(chartMs);
            }
            catch (Exception e)
            {
                Fault("judgement", e);
            }
        }

        internal static void Hb(IntPtr engine, double chartMs)
        {
            if (_faulted) return;

            try
            {
                if (Playing)
                {
                    _player?.Hb(engine, chartMs);

                    _player?.Correct(chartMs);
                    return;
                }

                if (!Ran) _recorder?.Hb(chartMs);
            }
            catch (Exception e)
            {
                Fault("note refresh", e);
            }
        }

        internal static int Flick(IntPtr note)
        {
            if (_faulted || !Playing || _player == null) return 0;

            try
            {
                return _player.Flick(note);
            }
            catch (Exception e)
            {
                Fault("flick", e);
                return 0;
            }
        }

        internal static void OnTransition(string from, string to)
        {
            if (Playing || Ran || Pending)
                {
                }

            if (from == "GameScene" && to == "ResultsScene")
            {
                _toResults = true;
                ReplayScreen.Finished();
                OnGameplayLeft();
                if (Ran) ReplayScreen.RestoreResultBlock();
                return;
            }

            if (from == "GameScene" && to == "GameScene")
            {
                if (Playing) return;
                Forget();
                return;
            }

            if (from == "GameScene")
            {
                Forget();
                return;
            }

            if (from == "ResultsScene" && to != "GameScene")
            {
                Forget();
                ReplayScreen.Forget();
            }
        }

        internal static void OnLeavingResults()
        {
            ReplayScreen.Forget();
        }

        internal static void ResultsScreenUp()
        {
            _toResults = false;
            ReplayScreen.Finished();
        }
        private static void OnGameplayLeft()
        {
            if (_player != null)
                {
                }

            if (_recorder != null)
            {
                if (!Ran) Data = _recorder.Finish();
                _recorder = null;
            }

            _player = null;
            Playing = false;
        }

        private static void Stop(string reason)
        {
            ModLog.Warn($"replay: {reason}; this run will be played normally");
            Playing = false;
            Ran = false;
            Data = null;
            AutoMark.Ran = false;
            AutoResults.BlockParticles(AutoSwitch.On);
        }

        private static void Forget()
        {
            Data = null;
            _recorder = null;
            _player = null;
            Playing = false;
            Ran = false;
            Pending = false;
            _toResults = false;
        }

        private static void Fault(string where, Exception e)
        {
            _faulted = true;
            if (++_faults > 1) return;
            ModLog.Error($"replay: {where} faulted, replay disabled: {ModLog.Describe(e)}");
        }
    }
}

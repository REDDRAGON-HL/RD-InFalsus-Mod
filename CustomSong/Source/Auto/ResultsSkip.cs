using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Runtime.InteropServices;
using CustomSong.Replay;

namespace CustomSong.Auto
{
    internal static unsafe class ResultsSkip
    {
        private const string GameplayScene = "GameScene";
        private const string ResultsScene = "ResultsScene";
        private const string SongListScene = "SongSelectScene";
        private const int RvaTransition = 0x6E3BE0;
        private const int RvaLeaveGameplay = 0x6F2220;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr TransitionFn(IntPtr self, int flags, IntPtr unloadName, IntPtr loadName,
                                             IntPtr onUnload, IntPtr onLoaded, IntPtr onLoadedSecond,
                                             IntPtr onReady);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LeaveFn(IntPtr argument);
        private static NativeHook<TransitionFn> _transition;
        private static TransitionFn _transitionTramp;
        private static LeaveFn _leave;
        private static IntPtr _finishInfo;
        internal static long Skips;
        private const string OptInName = ".skip-autoplay-results";
        private static bool? _askedFor;

        private static bool AskedFor
        {
            get
            {
                if (_askedFor.HasValue) return _askedFor.Value;
                bool found;
                try
                {
                    string root = ChartFolderLibrary.Locate();
                    found = !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, OptInName));
                }
                catch
                {
                    found = false;
                }

                _askedFor = found;
                return found;
            }
        }

        private static bool _asked;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            IntPtr target = MethodResolver.ByName("CoreScene", "_bj");
            if (target == IntPtr.Zero)
                target = MethodResolver.ByRuntime("CoreScene", "_bj", "Game.dll", "ifapp.Game.Scenes");
            if (target == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                target = GameAssembly.Base + RvaTransition;

            if (target == IntPtr.Zero)
            {
                ModLog.Error("autoplay: the scene transition could not be found, so an autoplayed " +
                             "song will still end at the results screen");
                return;
            }

            _transition = new NativeHook<TransitionFn> { Target = target, Detour = (IntPtr)(delegate* unmanaged[Cdecl]<
                IntPtr, int, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&Detour };
            _transition.Attach();
            _transitionTramp = _transition.Trampoline;
            IntPtr leave = MethodResolver.ByName("GameScene", "_Ok");
            if (leave == IntPtr.Zero)
                leave = MethodResolver.ByRuntime("GameScene", "_Ok", "Game.dll", "ifapp.Game.Scenes");
            if (leave == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                leave = GameAssembly.Base + RvaLeaveGameplay;

            if (leave != IntPtr.Zero)
                _leave = (LeaveFn)Marshal.GetDelegateForFunctionPointer(leave, typeof(LeaveFn));
            else
                ModLog.WarnRelease("autoplay: GameScene._Ok could not be found; the play will be sent " +
                                   "to the song list without it");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Detour(IntPtr self, int flags, IntPtr unloadName, IntPtr loadName,
                                     IntPtr onUnload, IntPtr onLoaded, IntPtr onLoadedSecond, IntPtr onReady)
        {
            try
            {
                string from = Memory.Text(unloadName);
                string to = Memory.Text(loadName);
                CustomSongCore.OnSceneTransition(to);
                ReplayCore.OnTransition(from, to);
                if (from == ResultsScene && to == GameplayScene) ReplayScreen.RetryStarting();
                if (from == ResultsScene) AutoResults.OnLeavingResults();

                if (Redirects(from, to, out IntPtr destination))
                    return _transitionTramp(self, flags, unloadName, destination,
                                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("autoplay: the scene transition could not be redirected: " +
                                   ModLog.Describe(e));
            }

            return _transitionTramp(self, flags, unloadName, loadName,
                                    onUnload, onLoaded, onLoadedSecond, onReady);
        }

        private static bool Redirects(string from, string to, out IntPtr destination)
        {
            destination = IntPtr.Zero;
            if (!AutoMark.Ran || ReplayCore.Ran) return false;
            if (!AskedFor) return false;
            if (from != GameplayScene || to != ResultsScene) return false;
            _leave?.Invoke(IntPtr.Zero);
            destination = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_new(SongListScene);
            if (destination == IntPtr.Zero)
            {
                ModLog.WarnRelease("autoplay: the song list's scene name could not be built, so this " +
                                   "play will reach the results screen");
                return false;
            }

            Skips++;
            return true;
        }

        internal static bool FinishGameplay()
        {
            IntPtr scene = Hooks.ChartScene;
            if (!Memory.LooksLikeObject(scene)) return false;

            if (_finishInfo == IntPtr.Zero)
            {
                _finishInfo = MethodResolver.MethodInfoByRuntime("GameScene", "_Zk", "Game.dll",
                                                                 "ifapp.Game.Scenes", 1);
                if (_finishInfo == IntPtr.Zero)
                {
                    ModLog.WarnRelease("replay: GameScene._Zk could not be found; the replay cannot " +
                                       "finish the song itself");
                    return false;
                }
            }

            int phase = Memory.I32(scene + GameScenePhase);
            if (phase != 3)
            {
                return false;
            }

            unsafe
            {
                byte arg = 0;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&arg);
                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_finishInfo, scene, (void**)args, ref raised);

                if (raised != IntPtr.Zero)
                {
                    ModLog.WarnRelease("replay: starting the game's end raised " + Raised.Text(raised));
                    return false;
                }
            }

            return true;
        }

        internal static bool GameplayRunning =>
            Memory.LooksLikeObject(Hooks.ChartScene) &&
            Memory.I32(Hooks.ChartScene + GameScenePhase) == 3;
        private const int GameScenePhase = 0x230;
    }
}

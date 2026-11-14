using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class LevelLabelHooks
    {
        private const string Scenes = "ifapp.Game.Scenes";
        private const string SongSelect = "ifapp.Game.Scenes.SongSelect";
        private const string GameUi = "ifapp.Game.Scenes.Game.UI";
        private const int RvaTransition = 0x47AE10;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CardFn(IntPtr self, IntPtr current, IntPtr animation);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ButtonFn(IntPtr self, byte a, byte b, int level, byte c, byte d);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HudFn(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr extra);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SmallCardFn(IntPtr self, int position, IntPtr current);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResultsFn(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr extra);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TransitionFn(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr result, byte k);
        private static NativeHook<CardFn> _card;
        private static NativeHook<ButtonFn> _button;
        private static NativeHook<HudFn> _hud;
        private static NativeHook<SmallCardFn> _smallCard;
        private static NativeHook<ResultsFn> _results;
        private static NativeHook<TransitionFn> _transition;
        private static CardFn _cardTramp;
        private static ButtonFn _buttonTramp;
        private static HudFn _hudTramp;
        private static SmallCardFn _smallCardTramp;
        private static ResultsFn _resultsTramp;
        private static TransitionFn _transitionTramp;

        internal static bool Install(out int installed, out int attempted)
        {
            int took = 0;
            int tried = 0;

            void Count(bool ok) { tried++; if (ok) took++; }

            Count(InstallCard());
            Count(InstallButton());
            Count(InstallHud());
            Count(InstallSmallCard());
            Count(InstallResults());
            Count(InstallTransition());
            installed = took;
            attempted = tried;
            return took > 0;
        }

        internal static void Uninstall()
        {
            _card?.Detach();
            _button?.Detach();
            _hud?.Detach();
            _smallCard?.Detach();
            _results?.Detach();
            _transition?.Detach();
            _card = null;
            _button = null;
            _hud = null;
            _smallCard = null;
            _results = null;
            _transition = null;
            _entries.Clear();
        }

        internal static bool WatchesTransition => _transition != null;

        private static bool Attach<T>(ref NativeHook<T> hook, string label, IntPtr target, Delegate detour)
            where T : Delegate
        {
            if (target == IntPtr.Zero)
            {
                ModLog.WarnRelease($"level labels: {label} was not found; that place keeps its number");
                return false;
            }

            hook = new NativeHook<T>
            {
                Target = target,
                Detour = Marshal.GetFunctionPointerForDelegate(detour),
            };

            if (!hook.Attach())
            {
                hook = null;
                return false;
            }

            _entries.Add((target, Marshal.GetFunctionPointerForDelegate(detour)));
            return true;
        }

        private static readonly List<(IntPtr Target, IntPtr Thunk)> _entries = new();

        private static bool InstallCard()
        {
            IntPtr target = MethodResolver.ByRuntime("LargeSongCard", "_OS", "Game.dll", SongSelect);
            if (!Attach(ref _card, "LargeSongCard._OS", target, (CardFn)CardDetour)) return false;
            _cardTramp = _card.Trampoline;
            return true;
        }

        private static bool InstallButton()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectDifficultyButton", "_Sp", "Game.dll", Scenes);
            if (!Attach(ref _button, "SongSelectDifficultyButton._Sp", target, (ButtonFn)ButtonDetour)) return false;
            _buttonTramp = _button.Trampoline;
            return true;
        }

        private static bool InstallHud()
        {
            IntPtr target = MethodResolver.ByRuntime("SongInfoContainer", "_qCA", "Game.dll", GameUi);
            if (!Attach(ref _hud, "SongInfoContainer._qCA", target, (HudFn)HudDetour)) return false;
            _hudTramp = _hud.Trampoline;
            return true;
        }

        private static bool InstallSmallCard()
        {
            IntPtr target = MethodResolver.ByRuntime("SmallSongCard", "_tS", "Game.dll", SongSelect);
            if (!Attach(ref _smallCard, "SmallSongCard._tS", target, (SmallCardFn)SmallCardDetour)) return false;
            _smallCardTramp = _smallCard.Trampoline;
            return true;
        }

        private static bool InstallResults()
        {
            IntPtr target = MethodResolver.ByRuntime("ResultsJacketContainer", "_wm", "Game.dll", Scenes);
            if (!Attach(ref _results, "ResultsJacketContainer._wm", target, (ResultsFn)ResultsDetour)) return false;
            _resultsTramp = _results.Trampoline;
            return true;
        }

        private static bool InstallTransition()
        {
            IntPtr module = GameAssembly.Base;
            IntPtr target = module == IntPtr.Zero ? IntPtr.Zero : module + RvaTransition;

            if (!NativeEntry.IsPrologue(target))
                target = MethodResolver.ByRuntime("SongTransitionLayer", "_GA", "Game.dll", null);

            if (!Attach(ref _transition, "SongTransitionLayer._GA", target, (TransitionFn)TransitionDetour))
                return false;
            _transitionTramp = _transition.Trampoline;
            return true;
        }

        private static void CardDetour(IntPtr self, IntPtr current, IntPtr animation)
        {
            _cardTramp(self, current, animation);

            try
            {
                IntPtr slot = current != IntPtr.Zero ? Memory.Ptr(current) : IntPtr.Zero;
                LevelLabel.Card(self, slot);
            }
            catch (Exception e)
            {
                Guard("the song card", e);
            }
        }

        private static void ButtonDetour(IntPtr self, byte a, byte b, int level, byte c, byte d)
        {
            _buttonTramp(self, a, b, level, c, d);

            try
            {
                LevelLabel.Button(self, level);
            }
            catch (Exception e)
            {
                Guard("the difficulty button", e);
            }
        }

        private static void HudDetour(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr extra)
        {
            _hudTramp(self, songInfo, difficulty, extra);

            try
            {
                LevelLabel.Hud(self, songInfo, difficulty);
            }
            catch (Exception e)
            {
                Guard("the in-game hud", e);
            }
        }

        private static void SmallCardDetour(IntPtr self, int position, IntPtr current)
        {
            _smallCardTramp(self, position, current);

            try
            {
                LevelLabel.SmallCard(self, current);
            }
            catch (Exception e)
            {
                Guard("the song strip", e);
            }
        }

        private static void ResultsDetour(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr extra)
        {
            _resultsTramp(self, songInfo, difficulty, extra);

            try
            {
                LevelLabel.Results(self, songInfo, difficulty);
            }
            catch (Exception e)
            {
                Guard("the results jacket", e);
            }
        }

        private static void TransitionDetour(IntPtr self, IntPtr songInfo, byte difficulty, IntPtr result, byte k)
        {
            _transitionTramp(self, songInfo, difficulty, result, k);

            try
            {
                LevelLabel.Transition(self, songInfo, difficulty, "transition");
            }
            catch (Exception e)
            {
                Guard("the transition", e);
            }

            Credit.FromTransition(self, songInfo, difficulty);
        }

        private static int _errors;
        private static int _checks;
        private static bool _reported;

        internal static void Check()
        {
            if (_reported) return;
            if (++_checks % 120 != 0) return;

            foreach ((IntPtr target, IntPtr thunk) in _entries)
            {
                IntPtr now = NativeEntry.JumpTarget(target);
                if (now == IntPtr.Zero || now == thunk) continue;
                _reported = true;
                ModLog.WarnRelease($"level labels: another mod hooked 0x{target.ToInt64():X} over this " +
                                   "one. A second detour on the same function recurses until the stack " +
                                   "ends — remove the older plugin (ChartDesignerCredit / SpecialLevel); " +
                                   "their features are part of this mod now.");
                return;
            }
        }

        private static void Guard(string what, Exception e)
        {
            if (_errors++ > 4) return;
            ModLog.WarnRelease($"level labels: {what} threw {ModLog.Describe(e)}");
        }
    }
}

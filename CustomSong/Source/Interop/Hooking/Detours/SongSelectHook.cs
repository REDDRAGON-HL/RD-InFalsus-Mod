using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        private const string SceneImage = "Game.dll";
        private const string SceneNamespace = "ifapp.Game.Scenes";
        internal static long ApplyCalls;
        internal static long ApplyReloaded;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ApplyFn(IntPtr self, ushort songId, byte difficulty, int transition,
                                      IntPtr methodInfo);
        private static NativeHook<ApplyFn> _apply;
        private static ApplyFn _applyTramp;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PreviewRecordFn(IntPtr self, IntPtr songInfo, IntPtr methodInfo);
        private static NativeHook<PreviewRecordFn> _preview;
        private static PreviewRecordFn _previewTrampoline;

        private static bool InstallSongSelect()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_MN", SceneImage, SceneNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("SongSelectScene._MN could not be resolved; a song with a picture per " +
                                 "difficulty will keep showing the one it was first drawn with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _apply = new NativeHook<ApplyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, byte, int, IntPtr, byte>)&MnDetour,
            };
            _apply.Attach();
            _applyTramp = _apply.Trampoline;
            bool applied = Landed(Hook.SongSelectApply, target, prologue);
            return applied & InstallPreviewRecord();
        }

        private static bool InstallPreviewRecord()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_qN", SceneImage, SceneNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("SongSelectScene._qN could not be resolved; the preview will keep the " +
                                 "excerpt of the first difficulty this song was shown with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _preview = new NativeHook<PreviewRecordFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&PreviewRecordDetour,
            };
            _preview.Attach();
            _previewTrampoline = _preview.Trampoline;
            return Landed(Hook.PreviewRecord, target, prologue);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PreviewRecordDetour(IntPtr self, IntPtr songInfo, IntPtr methodInfo)
        {
            if (!Faulted)
            {
                try
                {
                    SongCatalog.ApplyPreviewTo(songInfo, CurrentSong.AppliedDifficulty);
                }
                catch (Exception e)
                {
                    Fault(Hook.PreviewRecord, e);
                }
            }

            _previewTrampoline(self, songInfo, methodInfo);
        }

        private static void DetachSongSelect()
        {
            _apply?.Detach();
            _apply = null;
            _applyTramp = null;
            _preview?.Detach();
            _preview = null;
            _previewTrampoline = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte MnDetour(IntPtr self, ushort songId, byte difficulty, int transition,
                                     IntPtr methodInfo)
        {
            ApplyCalls++;
            bool shadowed = false;
            if (!Faulted)
            {
                try
                {
                    bool changed = CurrentSong.Applying(self, songId, difficulty);
                    SongCatalog.ShowForDifficulty(songId, difficulty);
                    shadowed = CurrentSong.ShadowUnlessSongChanged(self, songId, changed);
                    if (shadowed) ApplyReloaded++;
                }
                catch (Exception e)
                {
                    Fault(Hook.SongSelectApply, e);
                }
            }

            byte applied = _applyTramp(self, songId, difficulty, transition, methodInfo);
            if (shadowed) CurrentSong.Unshadow(self, songId);
            return applied;
        }

        private const string SaveNamespace = "ifapp.Game";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte DifficultyStateFn(IntPtr save, IntPtr access, IntPtr songInfo,
                                                byte difficulty, byte flags, IntPtr methodInfo);
        private static NativeHook<DifficultyStateFn> _state;
        private static DifficultyStateFn _stateTramp;
        internal static long DifficultyAsked, DifficultyOurs;

        private static bool InstallDifficultyState()
        {
            IntPtr target = MethodResolver.ByRuntime("_NH", "_UoA", SceneImage, SaveNamespace);
            if (target == IntPtr.Zero)
            {
                ModLog.Error("_NH._UoA: no method by that name in this build; a custom song's " +
                                  "difficulties will follow the game's own locks, and a fresh save may " +
                                  "leave them locked");
                return false;
            }

            byte[] prologue = Prologue(target);
            _state = new NativeHook<DifficultyStateFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, byte, IntPtr, byte>)&DifficultyDetour,
            };
            _state.Attach();
            _stateTramp = _state.Trampoline;
            return Landed(Hook.DifficultyState, target, prologue);
        }

        private static void DetachDifficultyState()
        {
            _state?.Detach();
            _state = null;
            _stateTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte DifficultyDetour(IntPtr save, IntPtr access, IntPtr songInfo, byte difficulty,
                                             byte flags, IntPtr methodInfo)
        {
            DifficultyAsked++;

            if (!Faulted)
            {
                try
                {
                    if (Ours(songInfo, difficulty, out ChartInfo info, out int index))
                    {
                        DifficultyOurs++;
                        return info.NoChart[index] ? (byte)0 : (byte)2;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.DifficultyState, e);
                }
            }

            return _stateTramp(save, access, songInfo, difficulty, flags, methodInfo);
        }

        private static bool Ours(IntPtr songInfo, byte difficulty, out ChartInfo info, out int index)
        {
            info = null;
            index = -1;
            if (!Offsets.Ready || !Memory.LooksLikeObject(songInfo)) return false;
            if (!SongCatalog.ById.TryGetValue(Memory.U16(songInfo + Offsets.Song.Id), out info)) return false;
            index = SongCatalog.DifficultyIndexOf(difficulty);
            return index >= 0;
        }
    }
}

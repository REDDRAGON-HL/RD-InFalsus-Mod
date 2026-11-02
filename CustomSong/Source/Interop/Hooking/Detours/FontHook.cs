using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr EnsureLoadedFn(IntPtr result, IntPtr family, int localization,
                                               int atlasWidth, IntPtr methodInfo);
        private static NativeHook<EnsureLoadedFn> _ensureLoaded;
        private static EnsureLoadedFn _ensureLoadedTramp;

        internal static bool InstallFont()
        {
            if (_ensureLoaded != null) return true;

            IntPtr target = MethodResolver.ByRuntime("FastTextProcessor", "EnsureLoaded",
                                                     "FastText.dll", "FastText");
            if (target == IntPtr.Zero)
            {
                ModLog.Warn("FastTextProcessor.EnsureLoaded could not be found; a song title with " +
                                 "characters the game's fonts lack will stay blank");
                return false;
            }

            byte[] prologue = Prologue(target);
            _ensureLoaded = new NativeHook<EnsureLoadedFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, IntPtr, IntPtr>)
                    &EnsureLoadedDetour,
            };
            _ensureLoaded.Attach();
            _ensureLoadedTramp = _ensureLoaded.Trampoline;
            return Landed(Hook.Font, target, prologue);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr EnsureLoadedDetour(IntPtr result, IntPtr family, int localization,
                                                 int atlasWidth, IntPtr methodInfo)
        {
            Alive(Hook.Font);

            if (!Faulted)
            {
                try
                {
                    SongFont.Prepare(family);
                }
                catch (Exception e)
                {
                    if (!_fontReported)
                    {
                        _fontReported = true;
                        ModLog.Error("the song titles' font could not be prepared: " + ModLog.Describe(e));
                    }
                }
            }

            return _ensureLoadedTramp(result, family, localization, atlasWidth, methodInfo);
        }

        private static bool _fontReported;

        private static void DetachFont()
        {
            _ensureLoaded?.Detach();
            _ensureLoaded = null;
            _ensureLoadedTramp = null;
        }
    }
}

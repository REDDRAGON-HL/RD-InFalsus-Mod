using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MusicPauseFn(IntPtr self, double at, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MusicResumeFn(IntPtr self, float volume, double time, IntPtr methodInfo);
        private static NativeHook<MusicPauseFn> _musicPause;
        private static MusicPauseFn _musicPauseTramp;
        private static NativeHook<MusicResumeFn> _musicResume;
        private static MusicResumeFn _musicResumeTramp;

        private static bool InstallPause()
        {
            IntPtr pause = MethodResolver.ByName("_Dg", "_hiA");
            if (pause == IntPtr.Zero) return false;
            IntPtr resume = MethodResolver.ByName("_Dg", "_KiA");
            if (resume == IntPtr.Zero) return false;
            byte[] pausePrologue = Prologue(pause);
            _musicPause = new NativeHook<MusicPauseFn>
            {
                Target = pause,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, IntPtr, void>)&MusicPauseDetour,
            };
            _musicPause.Attach();
            _musicPauseTramp = _musicPause.Trampoline;
            byte[] resumePrologue = Prologue(resume);
            _musicResume = new NativeHook<MusicResumeFn>
            {
                Target = resume,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, float, double, IntPtr, void>)&MusicResumeDetour,
            };
            _musicResume.Attach();
            _musicResumeTramp = _musicResume.Trampoline;
            return Landed(Hook.MusicPause, pause, pausePrologue) & Landed(Hook.MusicResume, resume, resumePrologue);
        }

        private static void DetachPause()
        {
            _musicResume?.Detach();
            _musicResume = null;
            _musicResumeTramp = null;
            _musicPause?.Detach();
            _musicPause = null;
            _musicPauseTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void MusicPauseDetour(IntPtr self, double at, IntPtr methodInfo)
        {
            MusicPauses++;
            _musicPauseTramp(self, at, methodInfo);

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Hold(at);
                }
                catch (Exception e)
                {
                    Fault(Hook.MusicPause, e);
                }
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void MusicResumeDetour(IntPtr self, float volume, double time, IntPtr methodInfo)
        {
            MusicResumes++;
            _musicResumeTramp(self, volume, time, methodInfo);

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Resume(atMusicReturn: true);
                }
                catch (Exception e)
                {
                    Fault(Hook.MusicResume, e);
                }
            }
        }
    }
}

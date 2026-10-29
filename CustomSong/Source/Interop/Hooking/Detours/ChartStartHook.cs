using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RkFn(IntPtr self, IntPtr methodInfo);
        private static NativeHook<RkFn> _rk;
        private static RkFn _rkTramp;

        private static bool InstallChartStart()
        {
            IntPtr target = MethodResolver.ByName("GameScene", "_rk");
            if (target == IntPtr.Zero) return false;
            byte[] prologue = Prologue(target);
            _rk = new NativeHook<RkFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&RkDetour,
            };
            _rk.Attach();
            _rkTramp = _rk.Trampoline;
            return Landed(Hook.ChartStart, target, prologue);
        }

        private static void DetachChartStart()
        {
            _rk?.Detach();
            _rk = null;
            _rkTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr RkDetour(IntPtr self, IntPtr methodInfo)
        {
            ChartStarts++;
            ModLog.Load("chart start");

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Restart();
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartStart, e);
                }
            }

            return _rkTramp(self, methodInfo);
        }
    }
}

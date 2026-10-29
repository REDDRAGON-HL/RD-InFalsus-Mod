using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr VaFn(IntPtr result, IntPtr name, IntPtr methodInfo);
        private static NativeHook<VaFn> _va;
        private static VaFn _vaTramp;

        private static bool InstallChartLoad()
        {
            IntPtr target = MethodResolver.ByName("_s", "_VA");
            if (target == IntPtr.Zero) return false;
            byte[] prologue = Prologue(target);
            _va = new NativeHook<VaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)&VaDetour,
            };
            _va.Attach();
            _vaTramp = _va.Trampoline;
            return Landed(Hook.ChartLoader, target, prologue);
        }

        private static void DetachChartLoad()
        {
            _va?.Detach();
            _va = null;
            _vaTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr VaDetour(IntPtr result, IntPtr name, IntPtr methodInfo)
        {
            VaCalls++;

            if (!Faulted)
            {
                try
                {
                    if (ChartCatalog.TryServe(name, result))
                    {
                        VaServed++;
                        ModLog.Load($"chart load: '{Memory.Text(name, 128)}' (served)");
                        return result;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartLoader, e);
                }
            }

            return _vaTramp(result, name, methodInfo);
        }
    }
}

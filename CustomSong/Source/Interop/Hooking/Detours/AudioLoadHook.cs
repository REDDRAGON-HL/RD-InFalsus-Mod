using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe partial class Hooks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ZgAFn(IntPtr record, IntPtr unused);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte DjaFn(IntPtr self, IntPtr name, IntPtr index);
        internal static delegate* unmanaged[Cdecl]<IntPtr, uint, void> CompleteRead;
        private static NativeHook<ZgAFn> _zga;
        private static ZgAFn _zgaTramp;
        private static NativeHook<DjaFn> _dja;
        private static DjaFn _djaTramp;

        private static bool InstallAudioLoad()
        {
            IntPtr zgA = MethodResolver.ByName("_hF", "_ZgA");
            if (zgA == IntPtr.Zero) return false;
            IntPtr bga = MethodResolver.ByName("_hF", "_bGA");
            if (bga == IntPtr.Zero) return false;
            IntPtr dja = MethodResolver.ByName("_BG", "_DJA");
            if (dja == IntPtr.Zero) return false;
            CompleteRead = (delegate* unmanaged[Cdecl]<IntPtr, uint, void>)bga;
            byte[] zgAPrologue = Prologue(zgA);
            _zga = new NativeHook<ZgAFn>
            {
                Target = zgA,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&ZgADetour,
            };
            _zga.Attach();
            _zgaTramp = _zga.Trampoline;
            byte[] djaPrologue = Prologue(dja);
            _dja = new NativeHook<DjaFn>
            {
                Target = dja,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&DjaDetour,
            };
            _dja.Attach();
            _djaTramp = _dja.Trampoline;
            return Landed(Hook.AudioRead, zgA, zgAPrologue) & Landed(Hook.AudioName, dja, djaPrologue);
        }

        private static void DetachAudioLoad()
        {
            _dja?.Detach();
            _dja = null;
            _djaTramp = null;
            _zga?.Detach();
            _zga = null;
            _zgaTramp = null;
            CompleteRead = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ZgADetour(IntPtr record, IntPtr unused)
        {
            ZgaCalls++;

            if (!Faulted)
            {
                try
                {
#if DEBUG
                    ReadBookkeeping.Observe(record);
#endif
                    if (AudioCatalog.TryServe(record)) { ZgaServed++; return; }
                }
                catch (Exception e)
                {
                    Fault(Hook.AudioRead, e);
                }
            }

            _zgaTramp(record, unused);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte DjaDetour(IntPtr self, IntPtr name, IntPtr index)
        {
            DjaCalls++;

            if (!Faulted)
            {
                try
                {
                    if (AudioCatalog.TryResolve(name, index)) { DjaHits++; return 1; }
                }
                catch (Exception e)
                {
                    Fault(Hook.AudioName, e);
                }
            }

            return _djaTramp(self, name, index);
        }
    }
}

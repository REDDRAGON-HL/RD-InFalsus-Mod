using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong.Auto
{
    internal static unsafe class AutoHooks
    {
        private static bool Faulted;
        internal static int Faults;
        internal static bool Writing => AutoSwitch.On && !Faulted;
        private static bool _asked;
        private delegate void OzFn(IntPtr self, double time, IntPtr queue, IntPtr methodInfo);
        private delegate byte NzFn(IntPtr self, IntPtr noteDict, IntPtr note, byte cursorOnNote,
                                   IntPtr outGrade, IntPtr methodInfo);
        private delegate void OzTimerFn(IntPtr self, double delta, byte inWindow, IntPtr methodInfo);
        private delegate void HbFn(IntPtr lnp, double chartTime, double trackTime, double unitsPerSecond,
                                   IntPtr laneLists, double window);
        private delegate void TrackFn(IntPtr self);
        private static NativeHook<OzFn> _oz;
        private static OzFn _ozTramp;
        private static NativeHook<NzFn> _nz;
        private static NzFn _nzTramp;
        private static NativeHook<OzTimerFn> _ozTimer;
        private static OzTimerFn _ozTimerTramp;
        private static NativeHook<HbFn> _hb;
        private static HbFn _hbTramp;
        private static NativeHook<TrackFn> _trackOnEnable;
        private static TrackFn _trackOnEnableTramp;
        private static NativeHook<TrackFn> _trackUpdate;
        private static TrackFn _trackUpdateTramp;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            AutoOffsets.Resolve();
            int installed = 0;
            int attempted = 0;
            void Count(bool ok) { attempted++; if (ok) installed++; }

            Count(Attach(ref _oz, "_VD", "_Oz", "_E", 0x5F4620,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, IntPtr, IntPtr, void>)&OzDetour,
                         out _ozTramp));
            Count(Attach(ref _nz, "_VD", "_nz", "_E", 0x5F7A90,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, IntPtr, IntPtr, byte>)&NzDetour,
                         out _nzTramp));
            Count(Attach(ref _ozTimer, "_VD", "_oz", "_E", 0x5F7DE0,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, byte, IntPtr, void>)&OzTimerDetour,
                         out _ozTimerTramp));
            Count(Attach(ref _hb, "LogicalNotePlayer", "_hb", "ifapp.Game", 0x52FC80,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, double, double, IntPtr, double, void>)&HbDetour,
                         out _hbTramp));
            Count(Attach(ref _trackOnEnable, "Track", "OnEnable", "ifapp.Game.Scenes.Game", 0x66EBC0,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&TrackOnEnableDetour,
                         out _trackOnEnableTramp));
            Count(Attach(ref _trackUpdate, "Track", "Update", "ifapp.Game.Scenes.Game", 0x66EBD0,
                         (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&TrackUpdateDetour,
                         out _trackUpdateTramp));
            ModLog.Load($"autoplay: {installed}/{attempted} hooks installed");
            if (installed < attempted)
                ModLog.Warn("autoplay: a hook is missing; whatever it drives will silently do nothing");
        }

        internal static void Uninstall()
        {
            _oz?.Detach(); _oz = null; _ozTramp = null;
            _nz?.Detach(); _nz = null; _nzTramp = null;
            _ozTimer?.Detach(); _ozTimer = null; _ozTimerTramp = null;
            _hb?.Detach(); _hb = null; _hbTramp = null;
            _trackOnEnable?.Detach(); _trackOnEnable = null; _trackOnEnableTramp = null;
            _trackUpdate?.Detach(); _trackUpdate = null; _trackUpdateTramp = null;
            _asked = false;
        }

        private static bool Attach<T>(ref NativeHook<T> hook, string type, string method, string namespaze,
                                      int rva, IntPtr detour, out T trampoline) where T : Delegate
        {
            trampoline = null;
            IntPtr target = MethodResolver.ByName(type, method);
            if (target == IntPtr.Zero)
                target = MethodResolver.ByRuntime(type, method, "Game.dll", namespaze);
            if (target == IntPtr.Zero)
            {
                IntPtr image = GameAssembly.Base;
                if (image != IntPtr.Zero) target = image + rva;
            }
            if (target == IntPtr.Zero) return false;
            hook = new NativeHook<T> { Target = target, Detour = detour };
            hook.Attach();
            trampoline = hook.Trampoline;
            ModLog.Load($"autoplay: {type}.{method} hooked");
            return true;
        }

        private static void Fault(string where, Exception e)
        {
            Faulted = true;
            Faults++;
            if (Faults > 1) return;
            ModLog.Error($"autoplay: {where} detour faulted, autoplay disabled: {ModLog.Describe(e)}");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OzDetour(IntPtr self, double time, IntPtr queue, IntPtr methodInfo)
        {
            try
            {
                if (!Faulted) AutoSong.Frame(self, time);
                else AutoSong.LetGo(self, time);
            }
            catch (Exception e)
            {
                Fault("_Oz", e);
            }

            _ozTramp(self, time, queue, methodInfo);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte NzDetour(IntPtr self, IntPtr noteDict, IntPtr note, byte cursorOnNote,
                                     IntPtr outGrade, IntPtr methodInfo)
        {
            if (Writing)
            {
                cursorOnNote = 1;

                try
                {
                    AutoSong.Current.Sky.AimAt(self, note);
                }
                catch (Exception e)
                {
                    Fault("_nz", e);
                }
            }

            return _nzTramp(self, noteDict, note, cursorOnNote, outGrade, methodInfo);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OzTimerDetour(IntPtr self, double delta, byte inWindow, IntPtr methodInfo)
        {
            _ozTimerTramp(self, delta, inWindow, methodInfo);
            if (!Writing) return;

            try
            {
                AutoSong.Current.Bar.Pin(self);
            }
            catch (Exception e)
            {
                Fault("_oz", e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void HbDetour(IntPtr lnp, double chartTime, double trackTime, double unitsPerSecond,
                                     IntPtr laneLists, double window)
        {
            if (!Faulted)
            {
                try
                {
                    AutoSong.Observe(lnp, chartTime);
                }
                catch (Exception e)
                {
                    Fault("_hb", e);
                }
            }

            _hbTramp(lnp, chartTime, trackTime, unitsPerSecond, laneLists, window);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void TrackOnEnableDetour(IntPtr self)
        {
            _trackOnEnableTramp(self);

            try
            {
                JudgementPoint.SuppressAt(self);
            }
            catch (Exception e)
            {
                JudgementPoint.NoteFailure(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void TrackUpdateDetour(IntPtr self)
        {
            _trackUpdateTramp(self);

            try
            {
                JudgementPoint.KeepOff(self);
            }
            catch (Exception e)
            {
                JudgementPoint.NoteFailure(e);
            }
        }
    }
}

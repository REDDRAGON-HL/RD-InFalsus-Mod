using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class AutoSwitchHook
    {
        private const int RvaSettingsAwake = 0x6CBF20;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AwakeFn(IntPtr self, IntPtr methodInfo);
        private static NativeHook<AwakeFn> _awake;
        private static AwakeFn _awakeTramp;
        private static bool _asked;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            IntPtr awake = MethodResolver.ByName("UiSettingGameplayPanel", "Awake");
            if (awake == IntPtr.Zero)
                awake = MethodResolver.ByRuntime("UiSettingGameplayPanel", "Awake", "Game.dll",
                                                 "ifapp.Game.UI.Settings");
            if (awake == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                awake = GameAssembly.Base + RvaSettingsAwake;

            if (awake == IntPtr.Zero)
            {
                ModLog.Warn("AUTO switch: the settings page could not be found, so the switch has " +
                            "no page to appear on");
                return;
            }

            _awake = new NativeHook<AwakeFn>
            {
                Target = awake,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&AwakeDetour,
            };
            _awake.Attach();
            _awakeTramp = _awake.Trampoline;
            ModLog.Load("AUTO switch: the settings page is hooked");
        }

        internal static void Detach()
        {
            _awake?.Detach();
            _awake = null;
            _awakeTramp = null;
            _asked = false;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void AwakeDetour(IntPtr self, IntPtr methodInfo)
        {
            _awakeTramp(self, methodInfo);

            try
            {
                AutoSwitch.Attach(self);
                AutoRow.Attach(self);
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO switch: the settings page could not be taken: " + ModLog.Describe(e));
            }
        }
    }
}

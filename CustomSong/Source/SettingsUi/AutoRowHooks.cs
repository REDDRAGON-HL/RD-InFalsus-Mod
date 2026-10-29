using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static unsafe class AutoRowHooks
    {
        private const int RvaPressed = 0x6D9100;
        private const int RvaHovered = 0x6D6230;
        private const int ClosureIndex = 0x10;
        private const int ClosureRow = 0x18;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PressFn(IntPtr closure, IntPtr button, IntPtr hit, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HoverFn(IntPtr row, IntPtr button, byte entered, IntPtr methodInfo);
        private static NativeHook<PressFn> _pressed;
        private static PressFn _pressedTramp;
        private static NativeHook<HoverFn> _hovered;
        private static HoverFn _hoveredTramp;
        private static bool _asked;

        internal static void Install()
        {
            if (_asked) return;
            _asked = true;
            IntPtr target = MethodResolver.ByName("_Jb", "_Ah");
            if (target == IntPtr.Zero)
                target = MethodResolver.ByRuntime("_Jb", "_Ah", "Game.dll", "ifapp.Game.UI.Settings");
            if (target == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                target = GameAssembly.Base + RvaPressed;
            IntPtr hover = MethodResolver.ByName("UiSettingsMultiselectorBar", "_ah");
            if (hover == IntPtr.Zero && GameAssembly.Base != IntPtr.Zero)
                hover = GameAssembly.Base + RvaHovered;

            if (target == IntPtr.Zero)
            {
                ModLog.Warn("AUTO row: the option row could not be found, so the switch will not respond");
                return;
            }

            _pressed = new NativeHook<PressFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&PressedDetour,
            };
            _pressed.Attach();
            _pressedTramp = _pressed.Trampoline;

            if (hover != IntPtr.Zero)
            {
                _hovered = new NativeHook<HoverFn>
                {
                    Target = hover,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, void>)&HoverDetour,
                };
                _hovered.Attach();
                _hoveredTramp = _hovered.Trampoline;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void HoverDetour(IntPtr row, IntPtr button, byte entered, IntPtr methodInfo)
        {
            _hoveredTramp?.Invoke(row, button, entered, methodInfo);

            try
            {
                if (AutoRow.Ours(row) && entered != 0) AutoRow.Point();
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the hover could not be read: " + ModLog.Describe(e));
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PressedDetour(IntPtr closure, IntPtr button, IntPtr hit, IntPtr methodInfo)
        {
            try
            {
                if (Memory.LooksLikeObject(closure))
                {
                    int index = Memory.I32(closure + ClosureIndex);

                    if (AutoRow.Choose(Memory.Ptr(closure + ClosureRow), index))
                        ModLog.Load($"AUTO switch: {(index != 0 ? "on" : "off")} (pressed)");
                }
            }
            catch (Exception e)
            {
                ModLog.WarnRelease("AUTO row: the press failed: " + ModLog.Describe(e));
            }

            _pressedTramp?.Invoke(closure, button, hit, methodInfo);
        }
    }
}

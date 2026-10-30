using System;

namespace CustomSong
{
    internal sealed unsafe class NativeHook<T> where T : Delegate
    {
        private IntPtr _target;

        private bool _attached;

        internal static IntPtr JumpTarget(IntPtr target)
        {
            if (target == IntPtr.Zero) return IntPtr.Zero;

            byte* at = (byte*)target;
            if (at[0] == 0xE9) return target + 5 + *(int*)(at + 1);
            if (at[0] == 0xEB) return target + 2 + *(sbyte*)(at + 1);
            if (at[0] == 0xFF && at[1] == 0x25) return *(IntPtr*)(target + 6 + *(int*)(at + 2));
            if (at[0] == 0x48 && at[1] == 0xB8 && at[10] == 0xFF && at[11] == 0xE0)
                return *(IntPtr*)(at + 2);
            return IntPtr.Zero;
        }

        private static bool AlreadyHooked(IntPtr target)
        {
            if (JumpTarget(target) == IntPtr.Zero) return false;

            ModLog.WarnRelease($"a method at 0x{target.ToInt64():X} was already hooked by another " +
                               "mod; it is left alone instead of stacking a second detour on it");
            return true;
        }

#if MELONLOADER
        private readonly MelonLoader.NativeUtils.NativeHook<T> _hook = new();

        public IntPtr Target { set { _target = value; _hook.Target = value; } }

        public IntPtr Detour { set => _hook.Detour = value; }

        public T Trampoline => _hook.Trampoline;

        public bool Attach()
        {
            if (AlreadyHooked(_target)) return false;
            _hook.Attach();
            _attached = true;
            return true;
        }

        public void Detach()
        {
            if (!_attached) return;

            _hook.Detach();
            _attached = false;
        }
#elif BEPINEX
        private static class Dobby
        {
            private static readonly IntPtr Library = Load();

            internal static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int> Hook =
                (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)
                    System.Runtime.InteropServices.NativeLibrary.GetExport(Library, "DobbyHook");

            internal static readonly delegate* unmanaged[Cdecl]<IntPtr, int> Destroy =
                (delegate* unmanaged[Cdecl]<IntPtr, int>)
                    System.Runtime.InteropServices.NativeLibrary.GetExport(Library, "DobbyDestroy");

            private static IntPtr Load()
            {
                if (System.Runtime.InteropServices.NativeLibrary.TryLoad("dobby.dll", out IntPtr loaded))
                    return loaded;

                return System.Runtime.InteropServices.NativeLibrary.Load(
                    System.IO.Path.Combine(BepInEx.Paths.BepInExAssemblyDirectory, "dobby.dll"));
            }
        }

        private T _trampoline;

        public IntPtr Target { set => _target = value; }

        public IntPtr Detour { private get; set; }

        public T Trampoline => _trampoline;

        public bool Attach()
        {
            if (_target == IntPtr.Zero || Detour == IntPtr.Zero) return false;
            if (AlreadyHooked(_target)) return false;

            IntPtr origin;
            if (Dobby.Hook(_target, Detour, &origin) != 0) return false;
            _trampoline = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(origin);
            _attached = true;
            return true;
        }

        public void Detach()
        {
            if (_target == IntPtr.Zero || !_attached) return;
            Dobby.Destroy(_target);
            _target = IntPtr.Zero;
            _trampoline = default;
            _attached = false;
        }
#else
#error Define MELONLOADER or BEPINEX.
#endif
    }
}

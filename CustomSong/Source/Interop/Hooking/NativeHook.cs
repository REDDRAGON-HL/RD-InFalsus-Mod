using System;

namespace CustomSong
{
    internal sealed unsafe class NativeHook<T> where T : Delegate
    {
#if MELONLOADER
        private readonly MelonLoader.NativeUtils.NativeHook<T> _hook = new();

        public IntPtr Target { set => _hook.Target = value; }

        public IntPtr Detour { set => _hook.Detour = value; }

        public T Trampoline => _hook.Trampoline;
        public void Attach() => _hook.Attach();
        public void Detach() => _hook.Detach();
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

        private IntPtr _target;
        private T _trampoline;

        public IntPtr Target { set => _target = value; }

        public IntPtr Detour { private get; set; }

        public T Trampoline => _trampoline;

        public void Attach()
        {
            if (_target == IntPtr.Zero || Detour == IntPtr.Zero) return;
            IntPtr origin;
            if (Dobby.Hook(_target, Detour, &origin) != 0) return;
            _trampoline = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(origin);
        }

        public void Detach()
        {
            if (_target == IntPtr.Zero) return;
            Dobby.Destroy(_target);
            _target = IntPtr.Zero;
            _trampoline = default;
        }
#else
#error Define MELONLOADER or BEPINEX.
#endif
    }
}

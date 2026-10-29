using System;
using System.Runtime.InteropServices;

namespace CustomSong
{
    internal static class GameAssembly
    {
        private const string Module = "GameAssembly.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandleW(string moduleName);
        private static IntPtr _base;

        internal static IntPtr Base
        {
            get
            {
                if (_base == IntPtr.Zero) _base = GetModuleHandleW(Module);
                return _base;
            }
        }
    }
}

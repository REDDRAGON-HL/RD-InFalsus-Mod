using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace InFalsusNoIotaLimit
{
    internal static class NoIotaLimitCore
    {
        internal static Action<string> Info;
        internal static Action<string> Warn;
        internal static Action<string> Error;

        private static IntPtr _fieldPtr;
        private static Type _iotaType;
        private static bool _loggedCap;
        private static bool _loggedError;
        private static bool _resolveFailedLogged;

        internal static void SetCap()
        {
            try
            {
                if (_fieldPtr == IntPtr.Zero) ResolveField();
                if (_fieldPtr == IntPtr.Zero) return;

                unsafe
                {
                    ushort current;
                    IL2CPP.il2cpp_field_static_get_value(_fieldPtr, &current);
                    if (current == ushort.MaxValue) return;

                    ushort cap = ushort.MaxValue;
                    IL2CPP.il2cpp_field_static_set_value(_fieldPtr, &cap);
                    if (!_loggedCap)
                    {
                        _loggedCap = true;
                        Info?.Invoke($"MaxTotalIotaCount {current} -> {cap}");
                    }
                }
            }
            catch (Exception e)
            {
                if (_loggedError) return;
                _loggedError = true;
                Error?.Invoke(e.ToString());
            }
        }

        private static void ResolveField()
        {
            if (_iotaType == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _iotaType = asm.GetType("ifapp.Game.Data.IotaInventory", false)
                             ?? asm.GetType("Il2Cppifapp.Game.Data.IotaInventory", false);
                    if (_iotaType != null) break;
                }

                if (_iotaType == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch (ReflectionTypeLoadException e) { types = e.Types; }

                        foreach (Type type in types)
                        {
                            if (type != null && type.Name == "IotaInventory")
                            {
                                _iotaType = type;
                                break;
                            }
                        }
                        if (_iotaType != null) break;
                    }
                }

                if (_iotaType == null) return;
            }

            FieldInfo field = _iotaType.GetField("NativeFieldInfoPtr_MaxTotalIotaCount",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                IntPtr info = (IntPtr)field.GetValue(null);
                if (info != IntPtr.Zero)
                {
                    _fieldPtr = info;
                    return;
                }
            }

            IntPtr cls = Il2CppClassPointerStore.GetNativeClassPointer(_iotaType);
            if (cls != IntPtr.Zero)
            {
                _fieldPtr = IL2CPP.GetIl2CppField(cls, "MaxTotalIotaCount");
                if (_fieldPtr != IntPtr.Zero) return;
            }

            if (!_resolveFailedLogged)
            {
                _resolveFailedLogged = true;
                Warn?.Invoke("could not resolve IotaInventory.MaxTotalIotaCount");
            }
        }

        internal static void PatchUiCapCode()
        {
            try
            {
                IntPtr module = GetModuleHandle("GameAssembly.dll");
                if (module == IntPtr.Zero)
                {
                    Warn?.Invoke("GameAssembly.dll not loaded; UI cap not patched");
                    return;
                }

                byte[] pattern = { 0x81, 0xFB, 0x0F, 0x27, 0x00, 0x00 };
                byte[] replacement = { 0x81, 0xFB, 0xFF, 0xFF, 0x00, 0x00 };

                int patched = 0;
                IntPtr addr = module;
                MEMORY_BASIC_INFORMATION mbi;
                UIntPtr mbiSize = (UIntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));

                while (VirtualQuery(addr, out mbi, mbiSize) != UIntPtr.Zero)
                {
                    if (mbi.AllocationBase != module) break;

                    bool executable = mbi.State == MEM_COMMIT &&
                        (mbi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ |
                                        PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;

                    if (executable && mbi.RegionSize.ToUInt64() > 0)
                    {
                        int size = (int)mbi.RegionSize;
                        byte[] buffer = new byte[size];
                        Marshal.Copy(mbi.BaseAddress, buffer, 0, size);

                        for (int i = 0; i <= size - pattern.Length; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < pattern.Length; j++)
                                if (buffer[i + j] != pattern[j]) { match = false; break; }
                            if (!match) continue;

                            IntPtr target = IntPtr.Add(mbi.BaseAddress, i);
                            uint oldProtect;
                            if (VirtualProtect(target, (UIntPtr)replacement.Length,
                                    PAGE_EXECUTE_READWRITE, out oldProtect))
                            {
                                Marshal.Copy(replacement, 0, target, replacement.Length);
                                VirtualProtect(target, (UIntPtr)replacement.Length, oldProtect, out _);
                                patched++;
                            }
                        }
                    }

                    addr = IntPtr.Add(mbi.BaseAddress, (int)mbi.RegionSize);
                }

                Info?.Invoke($"UI cap patch: {patched} site(s)");
            }
            catch (Exception e)
            {
                Error?.Invoke(e.ToString());
            }
        }

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_EXECUTE = 0x10;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_WRITECOPY = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize,
            uint flNewProtect, out uint lpflOldProtect);
    }
}
